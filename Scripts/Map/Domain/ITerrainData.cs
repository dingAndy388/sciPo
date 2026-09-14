namespace SciencePotato.Scripts.Map.Domain
{
	public interface ITerrainData
	{
		string Id { get; set; }
		string Name { get; set; }
		float Weight { get; set; }
		float MoveCost { get; set; }

		/// <summary>（v0.3 / WP-0.4）是否可通行：取代 "MoveCost == 0 即不可通行" 的魔法值语义。</summary>
		bool Passable { get; set; }

		/// <summary>（v0.3 / WP-0.4）解锁该地形通行所需的科技节点 Id；空字符串表示无需解锁。</summary>
		string UnlockTech { get; set; }
	}
}
