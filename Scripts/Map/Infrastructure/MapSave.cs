using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.2）地图存档根对象。
	/// <para><see cref="SaveVersion"/> 的口径：**只增不改**。读档时按版本分支处理（旧档缺字段即取默认值），
	/// 无法识别的更高版本要**拒绝加载**而不是猜。</para>
	/// <para>（v0.3 / WP-3.3）地图**有意不并入**统一存档单元（`D53`）：它体量最大且与\"每格\"强耦合，
	/// 放进统一文件会让每次存档点都重写整张地图；因此这里保留自己的版本号与拒绝加载语义，
	/// 而\"统一存档单元\"负责时钟/任务/迷雾/资源/科技/修正器/事件。</para>
	/// </summary>
	public partial class MapSave
	{
		/// <summary>存档格式版本：1 = 只有地形（v0.3.4 之前）；2 = 含人口与占据物（v0.3.15 / WP-3.2）；
		/// 3 = 含同格交战的进攻方槽位（v0.3 / WP-3.6 / `E9`）。</summary>
		public int SaveVersion { get; set; } = CurrentVersion;

		/// <summary>
		/// 当前支持的存档格式版本。**只增不改**：旧档缺字段即取默认值（v2 档没有 `Invader` → 交战状态为空，
		/// 与"没在打仗"等价），因此无需为 v3 写迁移；更高版本仍然拒绝加载。
		/// </summary>
		public const int CurrentVersion = 3;

		public string Id { get; set; }
		public int width { get; set; }
		public int height { get; set; }
		public int seed { get; set; }
		public List<HexCubeCellSave> cells { get; set; } = new List<HexCubeCellSave>();
	}
}
