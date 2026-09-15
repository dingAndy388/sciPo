using SciencePotato.Scripts.Common.Domain;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.2）**地块存档 DTO**：地形 + 人口 + 占据物。
	/// <para>占据物分两个**具名槽位**（`Building` / `Unit`）而不是多态基类：System.Text.Json 不带
	/// 派生类型元数据，具名槽位既避免多态序列化的坑，也把"当前一格一个占据物"的模型显式写进存档
	/// （`WP-3.4` 把占用模型统一后，这里会扩展为集合）。</para>
	/// </summary>
	public class HexCubeCellSave
	{
		public HexCubePosition position { get; set; }
		public string terrain { get; set; }

		/// <summary>人口（`WP-2.3`：每格人口，住房半径内总计受上限约束）。</summary>
		public int Population { get; set; }

		public BuildingSaveDto Building { get; set; }
		public UnitSaveDto Unit { get; set; }

		/// <summary>
		/// （v0.3 / WP-3.6 / `E9`）**同格交战的进攻方**（`SaveVersion >= 3`）：被挑战方在 `Unit` 槽位，
		/// 攻进该格的单位在这里 —— 否则"打了一半的仗"读档后会凭空消失（`cell.Invader` 是交战状态的权威）。
		/// </summary>
		public UnitSaveDto Invader { get; set; }
	}
}
