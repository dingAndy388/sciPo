using Newtonsoft.Json;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Core.Time;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.3 / WP-2.2）**任务体系最小修**（`A7` 部分 / `TIME-02/03/04/09`）的验收检查。
	/// <para>三条主张：① 存储键唯一且稳定（同名不同实例不再互相覆盖）；② 完成或宿主消失即回收（不再空转、不留残影）；
	/// ③ 旧档可读且会被迁移（不静默丢数据）。</para>
	/// <para>用真实 <see cref="TaskRepository"/>（临时目录 + 真实文件）而不是替身：本 WP 修的正是"键与文件内容"，
	/// 只有看到落盘的 JSON 才能证明"残留 null / 残影条目"确实消失。</para>
	/// </summary>
	internal static class TaskLifecycleChecks
	{
		private const string Map = "m1";

		public static void RunAll()
		{
			Check.Run("WP-2.2 存储键：同名不同实例的任务不再互相覆盖（`TIME-03`）", SameIdDifferentUIdDoNotOverwrite);
			Check.Run("WP-2.2 完成即回收：一次性任务完成后自动离开订阅列表与任务文件（`TIME-09`）", CompletedTaskIsReclaimed);
			Check.Run("WP-2.2 真删除：注销的任务不再以 null 残留在文件里（`TIME-04`）", RemoveLeavesNoNullPlaceholder);
			Check.Run("WP-2.2 旧档迁移：以 Id 为键的历史任务文件被重新编键、null 项被丢弃", LegacyTaskFileIsMigrated);
			Check.Run("WP-2.2 范围注销：实体消失时它名下的所有任务一次清掉（`TIME-02` / `CON-06`）", RangeUnregisterByUId);
			Check.Run("WP-2.2 日边界一致性：任务文件条目数 == 活跃任务数（无残影、无丢失）", FileMatchesLiveTaskCount);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void SameIdDifferentUIdDoNotOverwrite()
		{
			Harness h = NewHarness();
			try
			{
				var first = new LinearTask(0, 90, "camp", "Construction", false, "uid-a", Map, 0);
				var second = new LinearTask(0, 90, "camp", "Construction", false, "uid-b", Map, 0);
				h.Bus.Register(first);
				h.Bus.Register(second);

				Check.AssertEqual(2, h.Repo.GetCurrentTasks(Map).Count, "任务文件条目数（旧实现只剩 1 条）");
				Check.AssertEqual("Construction:uid-a:camp", first.GetSnapshot().Key, "存储键格式（`Type:UId:Id`）");

				h.Clock.AdvanceDays(10);

				List<TaskSnapshot> snapshots = h.Repo.GetCurrentTasks(Map);
				Check.AssertEqual(2, snapshots.Count, "推进 10 日后的条目数");
				Check.Assert(snapshots.All(s => s.Progress == 10f), "两条任务各自累计到 10 日（互不覆盖）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void CompletedTaskIsReclaimed()
		{
			Harness h = NewHarness();
			try
			{
				int completed = 0;
				var task = new LinearTask(0, 5, "camp", "Construction", false, "uid-a", Map, 0);
				task.OnCompleted += () => completed++;
				h.Bus.Register(task);

				h.Clock.AdvanceDays(4);
				Check.AssertEqual(1, h.Bus.SubscriberCount, "完成前应仍在订阅列表");
				Check.AssertEqual(1, h.Repo.GetCurrentTasks(Map).Count, "完成前的快照数");
				Check.AssertEqual(0, completed, "第 4 日不应完成");

				h.Clock.AdvanceDays(1);
				Check.AssertEqual(1, completed, "第 5 日完成一次");
				Check.AssertEqual(0, h.Bus.SubscriberCount, "完成后应自动离开订阅列表（旧实现常驻）");
				Check.AssertEqual(0, h.Repo.GetCurrentTasks(Map).Count, "完成任务的快照应被回收（旧实现留下 IsCompleted=true 的残影）");

				h.Clock.AdvanceDays(30);
				Check.AssertEqual(1, completed, "完成回调只触发一次");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void RemoveLeavesNoNullPlaceholder()
		{
			Harness h = NewHarness();
			try
			{
				var task = new IntervalTask(0, 30, "Food", "ResourceGrowth", "none", Map, 1);
				h.Bus.Register(task);
				h.Clock.AdvanceDays(1);
				Check.AssertEqual(1, h.Repo.GetCurrentTasks(Map).Count, "注册后应有一条快照");

				h.Bus.Unregister(task); // 宿主被移除时的显式注销
				Check.AssertEqual(0, h.Repo.GetCurrentTasks(Map).Count, "显式注销后快照应被删除");

				string json = File.ReadAllText(h.FilePath);
				Check.Assert(!json.Contains("null"), "任务文件里不应出现 null 占位（`TIME-04`）");

				h.Clock.AdvanceDays(5);
				Check.AssertEqual(0, h.Repo.GetCurrentTasks(Map).Count, "已注销的任务不应被写回文件");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void LegacyTaskFileIsMigrated()
		{
			Harness h = NewHarness();
			try
			{
				// 旧口径：键 = `Id`（此处 `camp`），并且历史实现留下的 `null` 占位
				File.WriteAllText(h.FilePath,
					"{\"camp\":{\"MapId\":\"m1\",\"OwnerId\":0,\"Progress\":12.0,\"Target\":90.0,\"Id\":\"camp\","
					+ "\"Type\":\"Construction\",\"UId\":\"uid-legacy\",\"IsCompleted\":false},\"ghost\":null}");

				List<TaskSnapshot> tasks = h.Repo.GetCurrentTasks(Map);
				Check.AssertEqual(1, tasks.Count, "null 项应被丢弃（旧实现读回含 null 的列表）");
				Check.AssertEqual("Construction:uid-legacy:camp", tasks[0].Key, "迁移后的键");

				Dictionary<string, TaskSnapshot> raw = ReadEntries(h.FilePath);
				Check.Assert(raw.ContainsKey("Construction:uid-legacy:camp"), "文件应已按新键重写（迁移而非忽略）");
				Check.Assert(!raw.ContainsKey("camp"), "旧的 Id 键不应残留（否则新增/删除会打在两个键上）");
				Check.AssertEqual(12f, raw["Construction:uid-legacy:camp"].Progress, "迁移不得改动进度（不静默丢数据）");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void RangeUnregisterByUId()
		{
			Harness h = NewHarness();
			try
			{
				// 同一建筑名下：建造任务（`UId` = 建筑 uid）+ 人口增长任务（历史用法：`Id` = 建筑 uid）
				var buildB1 = new LinearTask(0, 90, "camp", "Construction", false, "b1", Map, 0);
				var growthB1 = new IntervalTask(0, 300, "b1", "PopulationGrowth", "none", Map, 0);
				var buildB2 = new LinearTask(0, 90, "camp", "Construction", false, "b2", Map, 0);
				var growthB2 = new IntervalTask(0, 300, "b2", "PopulationGrowth", "none", Map, 0);
				h.Bus.Register(buildB1);
				h.Bus.Register(growthB1);
				h.Bus.Register(buildB2);
				h.Bus.Register(growthB2);
				h.Clock.AdvanceDays(1);

				int removed = h.Bus.UnregisterByUId("b1");

				Check.AssertEqual(2, removed, "建筑 b1 名下被注销的任务数（建造 + 人口增长）");
				Check.AssertEqual(2, h.Bus.SubscriberCount, "另一座建筑的任务不受影响");
				Check.AssertEqual(2, h.Repo.GetCurrentTasks(Map).Count, "任务文件同步收缩");
				Check.AssertEqual(0, h.Bus.UnregisterByUId("b1"), "重复注销是幂等的");
			}
			finally { Cleanup(h.Dir); }
		}

		private static void FileMatchesLiveTaskCount()
		{
			Harness h = NewHarness();
			try
			{
				var keep = new IntervalTask(0, 30, "Food", "ResourceGrowth", "none", Map, 1);
				var shortOne = new LinearTask(0, 3, "camp", "Construction", false, "u1", Map, 0);
				var shortTwo = new LinearTask(0, 7, "school", "Construction", false, "u2", Map, 0);
				h.Bus.Register(keep);
				h.Bus.Register(shortOne);
				h.Bus.Register(shortTwo);

				h.Clock.AdvanceDays(20);

				List<TaskSnapshot> snapshots = h.Repo.GetCurrentTasks(Map);
				Check.AssertEqual(h.Bus.SubscriberCount, snapshots.Count, "文件条目数应等于活跃任务数");
				Check.AssertEqual(1, snapshots.Count, "两条一次性任务完成后只剩月结任务");
				Check.AssertEqual("ResourceGrowth:none:Food", snapshots[0].Key, "剩下的是资源月结任务");
				Check.AssertEqual(20f, snapshots[0].Progress, "月结任务进度不受其它任务回收影响");
			}
			finally { Cleanup(h.Dir); }
		}

		// ────────────────────────── 夹具 ──────────────────────────

		private sealed class Harness
		{
			public GameClock Clock;
			public GameTimeService Bus;
			public TaskRepository Repo;
			public string FilePath;
			public string Dir;
		}

		/// <summary>真实任务仓储 + 真实时间总线，落在独立临时目录（每个用例一份，互不干扰）。</summary>
		private static Harness NewHarness()
		{
			string dir = Path.Combine(Path.GetTempPath(), "science-potato-wp22-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);

			var clock = new GameClock { MaxDaysPerAdvance = int.MaxValue }; // `D28`：跨 >30 日的推进必须放开单帧上限
			var repo = new TaskRepository(Path.Combine(dir, "tasks_"));
			var bus = new GameTimeService(clock, repo);

			return new Harness
			{
				Clock = clock,
				Bus = bus,
				Repo = repo,
				FilePath = Path.Combine(dir, "tasks_" + Map),
				Dir = dir,
			};
		}

		private static Dictionary<string, TaskSnapshot> ReadEntries(string path)
			=> JsonConvert.DeserializeObject<Dictionary<string, TaskSnapshot>>(File.ReadAllText(path));

		private static void Cleanup(string dir)
		{
			try
			{
				if (Directory.Exists(dir)) Directory.Delete(dir, true);
			}
			catch (IOException)
			{
				// 临时目录清理失败不影响验收结果
			}
		}
	}
}
