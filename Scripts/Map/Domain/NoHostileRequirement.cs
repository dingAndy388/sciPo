using SciencePotato.Scripts.Common.Domain;

namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.8 / `UNIT-14`）**"该格没有被敌方单位封锁"**这条设计规则的可判定化形式
	/// （与 <see cref="TerrainRequirement"/> 同一族）。
	/// <para>design/unit.md「地块封锁」：敌方单位存活时，玩家单位无法进入该地块、无法在该地块建造建筑、
	/// 该地块的资源无法被采集；敌方单位死亡后恢复。占用的**机制**是 `MapCell.Occupant`（一格一占据物），
	/// 本类把它表达成上层可复用的语义，避免"是否能建造"的判据散落成各处 `IsClear` 的隐式约定。</para>
	/// <para>地图缺失（未加载）时视为满足 —— 此时没有任何占据物，也就没有封锁。</para>
	/// </summary>
	public class NoHostileRequirement(Map map, HexCubePosition position) : IRequirement
	{
		private readonly Map _map = map;
		private readonly HexCubePosition _position = position;

		public bool IsMet()
		{
			if (_map == null) return true;
			return !_map.IsHostileAt(_position);
		}
	}
}
