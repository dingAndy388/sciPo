using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.3）组合根的产物：**已装配好的服务集合**（无状态）。
	/// 与 <see cref="GameSession"/>（只持状态）分离，避免"服务与状态混在同一个对象里"导致测试无法替换实现。
	/// <para>v0.3 / WP-1.4 起 <see cref="Tables"/> 持有 7 张配置表，<see cref="ConfigReport"/> 持有启动期校验结论
	/// （error 才会阻断启动，warning 仅提示）。装配是增量的：`WP-2.x` 会依次把 Construction / Units /
	/// TechTrees / Events 的应用服务接到这些表上。</para>
	/// </summary>
	public sealed class CoreServices
	{
		public GameSession Session { get; init; }

		public IConfigSource ConfigSource { get; init; }

		/// <summary>7 张配置表（Terrains / Resources / Buildings / Units / TechTrees / Events / Generator）。</summary>
		public ConfigTables Tables { get; init; }

		/// <summary>启动期配置校验报告（真实配置要求 0 error）。</summary>
		public ConfigReport ConfigReport { get; init; }

		public IMapGenerator MapGenerator { get; init; }

		public MapAppService Map { get; init; }
	}
}

