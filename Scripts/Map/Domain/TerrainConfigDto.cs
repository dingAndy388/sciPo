namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>（v0.3 / WP-0.4）地形配置表的一行（JSON DTO）。</summary>
	public class TerrainConfigDto : ITerrainData
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public float Weight { get; set; }
		public float MoveCost { get; set; }

		/// <summary>是否可通行；缺省 true（显式表达，避免依赖 MoveCost 的魔法值）。</summary>
		public bool Passable { get; set; } = true;

		/// <summary>解锁通行所需的科技节点 Id（如水域 = buoyancy）；空表示无需解锁。</summary>
		public string UnlockTech { get; set; }
	}
}
