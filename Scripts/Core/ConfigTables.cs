using SciencePotato.Scripts.Construction.Domain;
using SciencePotato.Scripts.Events.Domain;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Resources.Domain;
using SciencePotato.Scripts.TechTree.Domain;
using SciencePotato.Scripts.Units.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.4）**7 张配置表**的装配产物：每张表一个仓库实例，全部由 <see cref="Config.ConfigTableLoader"/> 从
	/// <see cref="Common.Domain.IConfigSource"/> 的 JSON 文本装载。
	/// <para>表名与文件名一一对应（<c>Config/{表名}.json</c>）：
	/// <c>Terrains / Resources / Buildings / Units / TechTrees / Events / Generator</c>。</para>
	/// <para>任一张表装载失败（文件缺失 / JSON 非法）时对应属性为 null，失败原因记入 <see cref="Config.ConfigReport"/>；
	/// 因此使用方必须先看报告，而不是直接假设非空。</para>
	/// </summary>
	public sealed class ConfigTables
	{
		/// <summary>7 张表的名字（与 <c>Config/{name}.json</c> 一致，也是校验报告里的 Table 字段）。</summary>
		public static readonly IReadOnlyList<string> Names = new[]
		{
			"Terrains", "Resources", "Buildings", "Units", "TechTrees", "Events", "Generator",
		};

		public ITerrainConfigRepository Terrains { get; init; }

		public IResourcesConfigRepository Resources { get; init; }

		public IBuildingConfigRepository Buildings { get; init; }

		public IUnitsRepository Units { get; init; }

		public ITechTreesConfigRepository TechTrees { get; init; }

		public IEventConfigRepository Events { get; init; }

		/// <summary>地图生成器配置表（<c>Config/Generator.json</c>，字段 <c>Density</c>）。</summary>
		public IMapGeneratorConfig Generator { get; init; }

		/// <summary>已成功装载的表数量（0..7），用于启动日志与自检。</summary>
		public int LoadedCount => Names.Count(IsLoaded);

		/// <summary>
		/// （v0.6.0 / WP-5.3）**地图外观参数**（格子步长 / 贴图目录）：来自地形表的根字段，
		/// 表现层据此算出格位与贴图路径 —— 换美术只改这张表。表缺失时返回内置兜底值（不返回 null）。
		/// </summary>
		public IMapAppearanceConfig Appearance
			=> Terrains as IMapAppearanceConfig ?? TerrainAppearance.Defaults;

		/// <summary>
		/// （v0.6.4 / WP-5.9）**开局布置参数**（出生点间距 / 开局单位 / 开局资源 / 揭示半径）：来自 Generator 表的 `Start` 段；
		/// 表缺失时返回内置缺省（间距 20 / 揭示 3 / 1 个工人），绝不返回 null。
		/// </summary>
		public IStartSetupConfig Start => Generator as IStartSetupConfig ?? new GeneratorConfigDto();

		public bool IsLoaded(string tableName)
		{
			return tableName switch
			{
				"Terrains" => Terrains != null,
				"Resources" => Resources != null,
				"Buildings" => Buildings != null,
				"Units" => Units != null,
				"TechTrees" => TechTrees != null,
				"Events" => Events != null,
				"Generator" => Generator != null,
				_ => false,
			};
		}

		public IEnumerable<ITerrainData> AllTerrains() => Terrains?.GetAll() ?? Enumerable.Empty<ITerrainData>();

		public IEnumerable<IResourceConfig> AllResources()
			=> Resources?.GetResourcesPoolConfig()?.Resources ?? Enumerable.Empty<IResourceConfig>();

		public IEnumerable<IBuildingConfig> AllBuildings() => Buildings?.GetAll() ?? Enumerable.Empty<IBuildingConfig>();

		public IEnumerable<IUnitConfig> AllUnits() => Units?.GetAll() ?? Enumerable.Empty<IUnitConfig>();

		public IEnumerable<IEventConfig> AllEvents() => Events?.GetAllEvents() ?? Enumerable.Empty<IEventConfig>();

		public IEnumerable<string> TreeIds() => TechTrees?.GetTreeIds() ?? Enumerable.Empty<string>();
	}
}
