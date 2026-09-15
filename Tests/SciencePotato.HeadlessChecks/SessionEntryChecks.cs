using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.9.6 / `WP-5.11`）**会话入口**（新开局 / 存档 / 读档 / 退出 + 自动存档点）的验收检查。
	/// <para>关键主张：① 宿主只跟 `SessionEntryService` 打交道；② **读档按玩家表逐 owner 恢复**（`U7`：
	/// 旧口径只恢复一个 owner ⇒ 多 AI 局读档后 AI 的月结/成长任务全丢）；③ 自动存档点与月结同节拍。</para>
	/// </summary>
	internal static class SessionEntryChecks
	{
		private const string MapId = "entry-map";

		public static void RunAll()
		{
			Check.Run("WP-5.11 新开局：生成地图 + 全部势力被编排器接管（出生点/资源池/月结）", NewGameStartsEveryFaction);
			Check.Run("WP-5.11 自动存档点：每 30 日一次（不足间隔不写盘）", AutoSaveFollowsMonthBeat);
			Check.Run("WP-5.11 存档 / 读档 / 退出：状态可回来，退出后不再在局内", SaveLoadAndQuit);
			Check.Run("WP-5.11 `U7` 多势力读档：每个 owner 的周期任务都要恢复", LoadRestoresEveryOwner);
			Check.Run("WP-5.11 坏入口：空地图 Id / 不存在的地图 / 未开局 都返回 false 且不抛异常", InvalidEntryIsRejected);
			Check.Run("WP-5.11 i18n：入口层的失败键都有文案", EntryKeysHaveText);
		}

		// ────────────────────────── 用例 ──────────────────────────

		private static void NewGameStartsEveryFaction()
		{
			CoreServices core = NewSession();

			Check.Assert(core.Entry.NewGame(MapId, 20261700, 30, 30), $"新开局应成功（{core.Entry.LastError}）");
			Check.Assert(core.Entry.InGame, "开局后应处于局内");
			Check.AssertEqual(MapId, core.Entry.CurrentMapId, "当前地图 Id");
			Check.Assert(core.Orchestrator.IsStarted(MapId), "编排器应接管该地图");
			Check.AssertEqual(2, core.Orchestrator.LastStartedPlayerCount, "人类 + AI 两个势力都应被接管");
			Check.Assert(core.Map.GetAllCells(MapId).Any(), "地图应真的生成出来");

			var owners = core.Orchestrator.ReportsOf(MapId).Select(r => r.OwnerId).ToList();
			Check.Assert(owners.Contains(1) && owners.Contains(2), "两个 owner 都应有开局报告");
		}

		private static void AutoSaveFollowsMonthBeat()
		{
			CoreServices core = NewSession();
			core.Entry.NewGame(MapId, 20261701, 30, 30);

			core.Session.Clock.AdvanceDays(30);
			Check.Assert(core.Entry.AutoSaveIfDue(), "满 30 日应触发自动存档");
			Check.AssertEqual(30, core.Entry.LastAutoSaveDay, "自动存档日");
			Check.AssertEqual(1, core.Entry.AutoSaveCount, "自动存档次数");

			core.Session.Clock.AdvanceDays(10);
			Check.Assert(!core.Entry.AutoSaveIfDue(), "不足间隔不应写盘");
			Check.AssertEqual(1, core.Entry.AutoSaveCount, "自动存档次数不应变");

			core.Session.Clock.AdvanceDays(20); // 第 60 日
			Check.Assert(core.Entry.AutoSaveIfDue(), "满 60 日应再次触发");
			Check.AssertEqual(60, core.Entry.LastAutoSaveDay, "第二次自动存档日");
			Check.AssertEqual(2, core.Entry.AutoSaveCount, "自动存档次数");
		}

		private static void SaveLoadAndQuit()
		{
			CoreServices core = NewSession();
			core.Entry.NewGame(MapId, 20261702, 30, 30);
			core.Session.Clock.AdvanceDays(30);

			Check.Assert(core.Entry.Save(), "存档应成功");
			Check.AssertEqual(30, core.Entry.LastSavedDay, "存档日");

			Check.Assert(core.Entry.Quit(), "退出应成功");
			Check.Assert(!core.Entry.InGame, "退出后不应还在局内");
			Check.AssertEqual(30, core.Entry.LastSavedDay, "退出前应先落盘");

			Check.Assert(core.Entry.Load(MapId), $"读档应成功（{core.Entry.LastError}）");
			Check.Assert(core.Entry.InGame, "读档后应在局内");
			Check.Assert(core.Orchestrator.IsStarted(MapId), "读档后编排器应重新接管（`D80` 幂等）");
		}

		private static void LoadRestoresEveryOwner()
		{
			CoreServices core = NewSession();
			core.Entry.NewGame(MapId, 20261703, 30, 30);
			core.Session.Clock.AdvanceDays(30);

			int total = core.Tasks.GetCurrentTasks(MapId).Count();
			string before = DescribeTasks(core);
			string[] snapshotBefore = SnapshotKeys(core);
			string[] allBefore = AllKeys(core);

			Check.Assert(snapshotBefore.Length > 0, $"读档前应有快照类任务：{before}");
			Check.Assert(core.Tasks.GetCurrentTasks(MapId).Any(t => t.OwnerId == 2),
				$"AI 势力也应有周期任务——否则本用例锁不住 `U7`：{before}");

			Check.Assert(core.Entry.Save(), "读档前应先存档");

			// 读档（入口层按玩家表逐 owner 恢复）
			Check.Assert(core.Entry.Load(MapId), $"读档应成功（{core.Entry.LastError}）");
			string after = DescribeTasks(core);

			// ① **快照类任务逐条复原**（`U7` 的正题：按玩家表逐 owner 恢复，一条不漏、一条不多）
			string[] snapshotAfter = SnapshotKeys(core);
			Check.Assert(snapshotBefore.SequenceEqual(snapshotAfter),
				$"`U7`：快照类任务读档后应逐条复原。读档前 {before}；读档后 {after}");

			// ② 只允许新增**单位循环**：它们在存档里不是任务（`RestoreUnitTasks` 按"在场的单位"重建），
			//    因此读档后每个可动单位都会多出一条 `UnitMove` —— 这是口径，不是 bug。
			string[] allAfter = AllKeys(core);
			var added = allAfter.Except(allBefore).ToList();
			Check.Assert(added.All(key => key.Contains(":UnitMove:") || key.Contains(":UnitAttack:")),
				$"读档后除单位循环外不应新增任何任务，实际新增：{string.Join(" | ", added)}（读档前 {before}；读档后 {after}）");
			Check.Assert(allAfter.Length >= total, "任务总数不应减少");

			// ③ 事件引擎只挂人类（`G8`）：旧实现给每个 owner 都挂 ⇒ AI 多出一条 `EventTick`（本次收口的回归锁）
			var nonHumanEventTasks = core.Tasks.GetCurrentTasks(MapId)
				.Where(t => t.Type == "EventTick" && t.OwnerId != core.Session.HumanOwnerId).ToList();
			Check.AssertEqual(0, nonHumanEventTasks.Count,
				$"事件引擎只应挂在人类势力上，实际：{string.Join(" | ", nonHumanEventTasks.Select(t => t.OwnerId + ":" + t.Id))}");

			// ④ 恢复条数仍要多于人类一方（旧口径只恢复人类那一份）
			Check.Assert(core.WorldSave.LastRestoredTaskCount > snapshotBefore.Count(k => k.StartsWith("1:")),
				$"恢复条数必须多于人类一方的条数（否则就是回到了单 owner 读档）：{after}");
		}

		private static void InvalidEntryIsRejected()
		{
			CoreServices core = NewSession();

			Check.Assert(!core.Entry.Save(), "未开局就存档应被拒");
			Check.AssertEqual(SessionEntryService.SessionEntryKeys.NoGame, core.Entry.LastError, "未开局的拒绝键");
			Check.Assert(!core.Entry.Quit(), "未开局就退出应被拒");

			Check.Assert(!core.Entry.NewGame("", 1, 10, 10), "空地图 Id 应被拒");
			Check.AssertEqual(SessionEntryService.SessionEntryKeys.NoMap, core.Entry.LastError, "空地图的拒绝键");

			Check.Assert(!core.Entry.Load("no_such_map"), "不存在的地图读档应失败（而不是进空世界）");
			Check.Assert(core.Entry.LastError != null, "失败时应给出原因键");
		}

		private static void EntryKeysHaveText()
		{
			CoreServices core = NewSession();

			FieldInfo[] keys = typeof(SessionEntryService.SessionEntryKeys)
				.GetFields(BindingFlags.Public | BindingFlags.Static);
			Check.Assert(keys.Length > 0, "`SessionEntryKeys` 应有常量键");
			foreach (FieldInfo field in keys)
			{
				string key = (string)field.GetValue(null);
				Check.Assert(core.I18n.HasKey(key), $"键 `{key}` 必须有文案（zh）");
			}
		}

		// ────────────────────────── 夹具 ──────────────────────────

		/// <summary>把"当前任务清单"按 owner 分组打成一行（失败消息里直接看出多了/少了哪条）。</summary>
		private static string DescribeTasks(CoreServices core)
		{
			var groups = core.Tasks.GetCurrentTasks(MapId)
				.GroupBy(t => t.OwnerId)
				.OrderBy(g => g.Key)
				.Select(g => $"owner={g.Key}[{string.Join(",", g.Select(t => t.Type + ":" + t.Id).OrderBy(x => x))}]");
			return string.Join(" ", groups);
		}

		/// <summary>全部任务键（`owner:type:id`，排序后便于比较）。</summary>
		private static string[] AllKeys(CoreServices core)
			=> core.Tasks.GetCurrentTasks(MapId)
				.Select(t => $"{t.OwnerId}:{t.Type}:{t.Id}")
				.OrderBy(x => x, System.StringComparer.Ordinal)
				.ToArray();

		/// <summary>
		/// **快照类任务**：存档里逐条记录、读档必须逐条复原的那批。
		/// <para>排除 `UnitMove`/`UnitAttack`：单位循环在存档里不是"任务"（`RestoreUnitTasks` 按在场单位重建），
		/// 所以读档后每个可动单位都会多出一条 —— 这是口径（见用例 ② 的说明），不属于 `U7` 的判据。</para>
		/// </summary>
		private static string[] SnapshotKeys(CoreServices core)
			=> AllKeys(core).Where(key => !key.Contains(":UnitMove:") && !key.Contains(":UnitAttack:")).ToArray();

		private static CoreServices NewSession()
		{
			// （v0.9.6 / WP-5.11）会话入口需要**统一存档单元**：周期任务/资源/科技…都写进它的分区，
			// 读档才有东西可恢复（没有它时 `CoreServices.Tasks` 为 null ⇒ `U7` 根本无从验证）。
			string root = "mem://session-" + Guid.NewGuid().ToString("N") + "/";
			var store = new JsonSaveStore(new InMemoryFileSystem(), root + "world.save");

			CoreServices core = ConfigFixtures.BuildRealCore(saveStore: store, saveRoot: root);
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Session.AddPlayer(PlayerContext.Ai(2));
			return core;
		}
	}
}
