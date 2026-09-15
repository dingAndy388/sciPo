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

		/// <summary>
		/// （v0.6.0 / WP-5.3）贴图名（不含目录与扩展名）；空 = 用 <see cref="Id"/>。
		/// <para>给美术留出"文件名与地形 Id 不一致"的余地，同时让"换图"不需要改代码。</para>
		/// </summary>
		string Sprite { get; set; }

		/// <summary>（v0.6.0 / WP-5.3）占位色（`#RRGGBB` 或 `#RRGGBBAA`）；缺贴图时用它画纯色格。</summary>
		string Color { get; set; }
	}
}
