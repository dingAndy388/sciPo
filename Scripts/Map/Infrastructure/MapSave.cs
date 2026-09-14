using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.2）地图存档根对象。
	/// <para><see cref="SaveVersion"/> 的口径：**只增不改**。读档时按版本分支处理（旧档缺字段即取默认值），
	/// 无法识别的更高版本要**拒绝加载**而不是猜（`WP-3.3` 会把迁移钩子集中到统一存档单元）。</para>
	/// </summary>
	public partial class MapSave
	{
		/// <summary>存档格式版本：1 = 只有地形（v0.3.4 之前）；2 = 含人口与占据物（v0.3.15 / WP-3.2）。</summary>
		public int SaveVersion { get; set; } = CurrentVersion;

		public const int CurrentVersion = 2;

		public string Id { get; set; }
		public int width { get; set; }
		public int height { get; set; }
		public int seed { get; set; }
		public List<HexCubeCellSave> cells { get; set; } = new List<HexCubeCellSave>();
	}
}
