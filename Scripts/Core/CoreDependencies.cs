using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.3）组合根的入参：所有"与引擎相关"的实现都由宿主（Godot 适配层 / 测试）注入，
	/// 核心只依赖接口，从而可以脱离 Godot 运行（`WIRE-01` / `DEP-07`）。
	/// </summary>
	public sealed class CoreDependencies
	{
		/// <summary>文件系统（存档、user:// 覆写配置）。</summary>
		public IFileSystem FileSystem { get; init; }

		/// <summary>配置表文本来源（Config/*.json）。</summary>
		public IConfigSource ConfigSource { get; init; }

		/// <summary>
		/// 地图存档仓库工厂：由宿主提供（Godot 层用 <c>GodotMapRepository</c>，测试用内存实现）。
		/// <para>之所以用工厂而不是直接实例：地图仓库需要按地形 Id 解析地形、并在读档时重建建筑/单位
		/// （`SaveRebuilder` 又需要建筑/单位配置表），而这些配置由组合根本身产出 —— 直接注入会形成
		/// "仓库 ↔ 配置"的构造循环；工厂是打断该循环的最小手段（见 §13.z 的装配顺序，`WP-3.2` 起改为传入整表）。</para>
		/// </summary>
		public Func<ConfigTables, IMapRepository> MapRepositoryFactory { get; init; }

		/// <summary>随机源（可注入种子以保证可复现）。</summary>
		public IRandom Random { get; init; }

		/// <summary>
		/// 生成器配置（<c>Config/Generator/Generator.tres</c>）的资源加载器：**可选**。
		/// <para>v0.3 / WP-1.4 起生成器配置的权威来源是配置表 <c>Config/Generator.json</c>；
		/// 本加载器只保留"编辑器里用 .tres 临时覆写"的能力，无头环境留空即可。</para>
		/// </summary>
		public IConfigLoader ResourceConfigLoader { get; init; }

		/// <summary>生成器 .tres 覆写目录（Godot 资源路径）；仅在 <see cref="ResourceConfigLoader"/> 非空时生效。</summary>
		public string GeneratorConfigPath { get; init; } = "res://Config/Generator";

		/// <summary>
		/// （v0.3 / WP-1.4）配置校验发现 **error** 时是否快速失败（默认 true，`WIRE-04`）。
		/// <para>测试/工具把它设为 false 即可在"配置有错"的前提下检查完整校验报告，
		/// 而不用去断言异常消息。</para>
		/// </summary>
		public bool FailOnConfigErrors { get; init; } = true;

		public string SessionId { get; init; } = "session";

		/// <summary>
		/// （v0.3 / WP-3.8 / `UNIT-14`）是否在地图生成后按地形概率刷新**敌方单位**（默认开 = 生产行为）。
		/// <para>无头用例把它关掉即可得到"没有敌人的干净沙盘"（用例要自己布置格位时避免随机敌人抢占），
		/// 而生产装配（`ServiceContainer`）与本项默认值一致地开启 —— 敌方封锁是玩法的一部分，不该只在测试里存在。</para>
		/// </summary>
		public bool EnableEnemySpawn { get; init; } = true;

		/// <summary>
		/// （v0.3 / WP-3.3）**统一存档单元**（可空 = 本宿主不使用统一存档，退回\"各仓各写各的文件\"）。
		/// <para>由宿主创建（Godot：`JsonSaveStore` + `GodotFileSystem` + `user://...`；无头：`SystemFileSystem`），
		/// 组合根只把它透传到 <see cref="CoreServices.SaveStore"/>。**全进程只允许一个实例** ——
		/// 两个实例写同一个文件会互相覆盖（各自持有不同的内存文档）。</para>
		/// </summary>
		public ISaveStore SaveStore { get; init; }

		/// <summary>
		/// （v0.6.0 / WP-5.1）各仓储的**存档分区前缀**（Godot 生产：`user://save/`；无头：内存路径）。
		/// <para>注入 <see cref="SaveStore"/> 时它只是**分区键前缀**（如 <c>user://save/tasks_</c> → 分区 <c>user://save/tasks_:{mapId}</c>）；
		/// 没有存档单元时它退化成真实文件路径前缀（既有测试的\"一图一文件\"语义）。</para>
		/// <para>为什么必须由宿主给出：Godot 侧要落在 <c>user://</c>，无头/测试要落在内存或临时目录 ——
		/// 核心代码里出现任何具体路径都是 `DEP-07` 的复发。</para>
		/// </summary>
		public string SaveRoot { get; init; } = "user://save/";

		/// <summary>
		/// （v0.6.5 / WP-5.10）**已装载的语言列表**（`Config/Strings.{locale}.json`）。
		/// 缺省 = <c>["zh", "en"]</c>；缺文件的语言会被跳过（不阻断启动）。
		/// </summary>
		public IEnumerable<string> Locales { get; init; } = new[] { "zh", "en" };

		/// <summary>（v0.6.5 / WP-5.10）默认语言（回退终点；缺省 <c>zh</c>）。</summary>
		public string DefaultLocale { get; init; } = "zh";

		/// <summary>（v0.6.5 / WP-5.10）启动语言（缺省 = 默认语言）。</summary>
		public string Locale { get; init; }

		/// <summary>
		/// （v0.6.5 / WP-5.10）**玩家表**：开局时参与本局的势力（人类 + AI）。
		/// <para><c>null</c> / 空 = 单人类玩家（owner=1，v0.5 及以前的隐式口径，保证既有用例语义不变）。</para>
		/// <para>AI 势力的具体数量与难度由 `WP-6.1` 的 AI 配置决定，本项只负责\"谁在这一局里\"。</para>
		/// </summary>
		public IEnumerable<PlayerContext> Players { get; init; }
	}
}

