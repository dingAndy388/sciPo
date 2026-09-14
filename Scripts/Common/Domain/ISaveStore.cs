using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.3）**统一存档单元**（`I3`、`DEP-06`、`TIME-08`）。
	/// <para>改造前：时钟、任务、迷雾、资源、科技、修正器各写各的文件 —— 五套路径规范、五份序列化代码、
	/// 五个版本号，且**没有原子性**（写到一半崩溃即半个存档）与**没有版本迁移**（改结构即破档）。</para>
	/// <para>本接口把它们收敛成\"一个会话一份文件 + 若干具名分区（section）\"：
	/// 每个分区仍由各自的仓储负责**内容格式**（JSON 文本，互不干涉），但**落盘时机、文件名、版本号、原子写**
	/// 全部由存档单元统一承担。这样\"谁在什么时候真正写盘\"只有一个答案，也让 `saveVersion` 迁移有了唯一入口。</para>
	/// <para>分区键的约定：`<子系统>:<业务 id>`（如 `tasks:world-map`、`resources:world-map_1`）；
	/// 旧的文件路径语义随之消失（路径由存档单元决定）。</para>
	/// </summary>
	public interface ISaveStore
	{
		/// <summary>存档文件的格式版本（见 <c>SaveFile.CurrentVersion</c>）。</summary>
		int SaveVersion { get; }

		/// <summary>
		/// 显式读盘（含版本迁移）。首次访问 <see cref="Day"/>/分区时会自动读，因此调用它是**可选**的 ——
		/// 存在的意义是让\"更高版本 → 拒绝加载\"可以在读档流程的**第一步**被捕获（而不是在某个子系统的读中途爆）。
		/// </summary>
		/// <exception cref="SaveVersionTooNewException">文件版本高于本程序支持的版本。</exception>
		void Load();

		/// <summary>存档点的游戏日（**并入文件头**：此前它是一个独立的时钟文件）。</summary>
		double Day { get; }

		/// <summary>写入游戏日（标脏，等待 <see cref="Commit"/>）。</summary>
		void SetDay(double day);

		/// <summary>该分区是否存在。</summary>
		bool HasSection(string key);

		/// <summary>读取分区内容；不存在返回 <c>null</c>（不抛异常）。</summary>
		string ReadSection(string key);

		/// <summary>写入分区内容（标脏，等待 <see cref="Commit"/>）。</summary>
		void WriteSection(string key, string json);

		/// <summary>删除分区；返回是否命中。</summary>
		bool DeleteSection(string key);

		/// <summary>列出全部分区键（排查/验收用）。</summary>
		IReadOnlyList<string> ListSections();

		/// <summary>本次加载实际应用的迁移名（升序；空 = 档已是当前版本）。</summary>
		IReadOnlyList<string> AppliedMigrations { get; }

		/// <summary>
		/// **原子落盘**：有改动时先写临时文件再替换正式文件。返回是否真的写了盘。
		/// <para>为什么是\"先临时后替换\"：直接覆写正式文件时，进程在写到一半被杀会留下**半个存档**
		/// （既读不出新内容，也丢了旧内容）。替换是文件系统级的原子操作，因此旧档要么完整、要么被完整换掉。</para>
		/// </summary>
		bool Commit();
	}

	/// <summary>
	/// （v0.3 / WP-3.3）**存档版本高于本程序支持的上限**：拒绝加载而不是猜未知字段的语义
	/// （猜错的代价是静默丢数据，比\"打不开存档\"严重得多）。
	/// </summary>
	public sealed class SaveVersionTooNewException(int fileVersion, int supportedVersion)
		: Exception($"存档版本 {fileVersion} 高于本程序支持的 {supportedVersion}，拒绝加载（请升级游戏或使用旧档）")
	{
		public int FileVersion { get; } = fileVersion;

		public int SupportedVersion { get; } = supportedVersion;
	}
}
