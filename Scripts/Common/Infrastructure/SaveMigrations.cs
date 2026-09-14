using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.3）**一次存档格式迁移**：把版本 <see cref="FromVersion"/> 的存档内容就地改写为
	/// <see cref="ToVersion"/>（原地修改 <see cref="SaveFile"/>，由存档单元负责写回）。
	/// <para>约定：① 只改自己关心的分区；② **不抛异常**（无法识别的旧内容保持原样，宁可少迁也不要毁档）；
	/// ③ 迁移是**幂等**的（重复应用同一版本不会重复改）。</para>
	/// </summary>
	public interface ISaveMigration
	{
		/// <summary>起始版本。</summary>
		int FromVersion { get; }

		/// <summary>目标版本（通常 = <see cref="FromVersion"/> + 1）。</summary>
		int ToVersion { get; }

		/// <summary>迁移名（写进存档留痕 + 用例断言）。</summary>
		string Name { get; }

		void Apply(SaveFile file);
	}

	/// <summary>（v0.3 / WP-3.3）内置迁移链：读档时若文件版本落后，按顺序依次升级到当前版本。</summary>
	public static class SaveMigrations
	{
		public const string TaskKeyMigration = "v1->v2 任务分区键改为 Type:UId:Id";
		public const string ClockIntoSaveHeader = "v2->v3 时钟并入存档文件头 + 事件状态落盘";

		/// <summary>内置迁移（升序）。</summary>
		public static IReadOnlyList<ISaveMigration> Default { get; } = new List<ISaveMigration>
		{
			new TaskKeyMigrationV1ToV2(),
			new ClockIntoSaveHeaderV2ToV3(),
		};

		/// <summary>按需追加迁移（用例自造\">当前版本\"的档时用来模拟未来迁移）。</summary>
		public static List<ISaveMigration> WithDefaults(params ISaveMigration[] extra)
		{
			var all = new List<ISaveMigration>(Default);
			if (extra != null) all.AddRange(extra.Where(m => m != null));
			return all;
		}
	}

	/// <summary>
	/// （v0.3 / WP-3.3）**v1 → v2**：任务分区（`tasks:*`）从\"以 `Id` 为键\"改为\"以 `Type:UId:Id` 为键\"。
	/// <para>背景 `TIME-03`：旧键会让\"两座同名建筑\"\"两个同种单位\"的任务快照**互相覆盖**（读档只回来一个）。
	/// 迁移把每个快照重新编键，并顺带丢弃历史 <c>null</c> 项（`TIME-04` 写坏的文件）。</para>
	/// </summary>
	public sealed class TaskKeyMigrationV1ToV2 : ISaveMigration
	{
		public int FromVersion => 1;

		public int ToVersion => 2;

		public string Name => SaveMigrations.TaskKeyMigration;

		public void Apply(SaveFile file)
		{
			if (file?.Sections == null) return;

			foreach (string key in file.Sections.Keys.Where(k => k.StartsWith("tasks:", StringComparison.Ordinal)).ToList())
			{
				string json = file.Sections[key];
				if (string.IsNullOrWhiteSpace(json)) continue;

				Dictionary<string, TaskSnapshot> snapshots;
				try
				{
					snapshots = JsonConvert.DeserializeObject<Dictionary<string, TaskSnapshot>>(json);
				}
				catch (JsonException)
				{
					continue; // 认不出来的内容保持原样（宁可少迁也不毁档）
				}

				if (snapshots == null) continue;

				var reindexed = new Dictionary<string, TaskSnapshot>(StringComparer.Ordinal);
				foreach (TaskSnapshot snapshot in snapshots.Values)
				{
					if (snapshot == null) continue;
					reindexed[snapshot.Key] = snapshot;
				}

				file.Sections[key] = JsonConvert.SerializeObject(reindexed, Formatting.Indented);
			}
		}
	}

	/// <summary>
	/// （v0.3 / WP-3.3）**v2 → v3**：时钟从\"独立分区\"并入**存档文件头**（<see cref="SaveFile.Day"/>）。
	/// <para>背景：时钟此前是一个单独文件（`clock_{sessionId}`），存档点要写两次、读档要读两次，
	/// 而且\"日期\"与\"世界状态\"可能来自不同时刻的两次写（不一致）。并入文件头后，
	/// 一份存档就是**同一时刻的一个整体**。</para>
	/// <para>同时：v3 起事件引擎的\"生效中事件 + 触发计数\"开始落盘（旧档没有该分区 → 语义等价于\"没有生效事件\"）。</para>
	/// </summary>
	public sealed class ClockIntoSaveHeaderV2ToV3 : ISaveMigration
	{
		public int FromVersion => 2;

		public int ToVersion => 3;

		public string Name => SaveMigrations.ClockIntoSaveHeader;

		public void Apply(SaveFile file)
		{
			if (file?.Sections == null) return;

			foreach (string key in file.Sections.Keys.Where(k => k.StartsWith("clock", StringComparison.Ordinal)).ToList())
			{
				string json = file.Sections[key];
				file.Sections.Remove(key);
				if (string.IsNullOrWhiteSpace(json)) continue;

				try
				{
					JToken token = JToken.Parse(json);
					double day = token["Day"]?.Value<double>() ?? token["CurrentDay"]?.Value<double>() ?? 0d;
					if (day > file.Day) file.Day = day; // 只接受\"更靠后\"的日期，避免把更早的分区写脏
				}
				catch (JsonException)
				{
					// 认不出来的时钟内容：丢弃该分区（时间从文件头取），但不让整份存档加载失败
				}
			}
		}
	}
}
