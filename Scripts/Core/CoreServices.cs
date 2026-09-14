using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.3）组合根的产物：**已装配好的服务集合**（无状态）。
	/// 与 <see cref="GameSession"/>（只持状态）分离，避免"服务与状态混在同一个对象里"导致测试无法替换实现。
	/// <para>装配是增量的：本批次只装到地图（`WP-3.1`）；后续 `WP-1.4` 接入其余 6 张配置表，
	/// `WP-2.x` 依次接入 Construction / Units / TechTrees / Events 等应用服务。</para>
	/// </summary>
	public sealed class CoreServices
	{
		public GameSession Session { get; init; }

		public IConfigSource ConfigSource { get; init; }

		public ITerrainConfigRepository TerrainConfig { get; init; }

		public IMapGenerator MapGenerator { get; init; }

		public MapAppService Map { get; init; }
	}
}
