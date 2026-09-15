using Newtonsoft.Json;
using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	/// <summary>
	/// （v0.9.7 / `WP-5.6`）**地图存档的增量合并**（`MAP-07`）。
	/// <para>问题：存档点每写一次就把**整张地图**重新映射成 DTO（10k 格 ⇒ 每次都是 10k 个小对象 + 2.4 MB 文本），
	/// 而实际变化的格子通常只有个位数（人口 / 一格地形 / 一栋建筑的 HP）。</para>
	/// <para>做法：把"上一次落盘的 DTO"留在仓储里，与本次映射结果**逐格比较**（比较只用廉价字段 + 占据物 DTO 的
	/// 序列化），未变的格子**复用旧 DTO**，只替换变过的那几格 ⇒</para>
	/// <list type="bullet">
	/// <item><see cref="MapSaveMerger"/> 返回**精确的脏格数**（用例据此断言，不靠猜）；</item>
	/// <item>脏格数为 0 时调用方**整次跳过写盘**（空闲存档点零成本 —— 这是最大的收益）；</item>
	/// <item>物理文件仍是"一张图一个 JSON"（`D53` 口径），因此"有变化时"文本仍是全量重写 —— 这一点在 `D122` 里写明，
	/// 不假装它是增量 IO。</item>
	/// </list>
	/// </summary>
	public static class MapSaveMerger
	{
		/// <summary>把 <paramref name="current"/> 合并进 <paramref name="previous"/>，返回（合并结果, 脏格数, 总格数）。</summary>
		public static (MapSave Merged, int DirtyCells, int TotalCells) Merge(MapSave previous, MapSave current)
		{
			if (current == null) return (null, 0, 0);

			// 首次存档 / 头部不一致（换图、格式版本变化）⇒ 全量
			if (previous == null || previous.cells == null || previous.cells.Count == 0
				|| previous.SaveVersion != current.SaveVersion || previous.Id != current.Id
				|| previous.width != current.width || previous.height != current.height || previous.seed != current.seed)
				return (current, current.cells.Count, current.cells.Count);

			var byPosition = new Dictionary<HexCubePosition, HexCubeCellSave>(previous.cells.Count);
			foreach (HexCubeCellSave cell in previous.cells) byPosition[cell.position] = cell;

			var merged = new MapSave
			{
				SaveVersion = current.SaveVersion,
				Id = current.Id,
				seed = current.seed,
				width = current.width,
				height = current.height,
			};

			int dirty = 0;
			foreach (HexCubeCellSave cell in current.cells)
			{
				if (byPosition.TryGetValue(cell.position, out HexCubeCellSave old) && SameCell(old, cell))
				{
					merged.cells.Add(old); // 未变 ⇒ 复用旧 DTO（不产生新分配）
				}
				else
				{
					merged.cells.Add(cell);
					dirty++;
				}
			}

			return (merged, dirty, current.cells.Count);
		}

		/// <summary>两格是否等价（地形 / 人口 / 三种占据物 DTO）。</summary>
		private static bool SameCell(HexCubeCellSave a, HexCubeCellSave b)
		{
			if (ReferenceEquals(a, b)) return true;
			if (a.terrain != b.terrain || a.Population != b.Population) return false;
			return SameDto(a.Building, b.Building) && SameDto(a.Unit, b.Unit) && SameDto(a.Invader, b.Invader);
		}

		/// <summary>占据物 DTO 的比较：都为 null 视为相同，否则按序列化文本比较（占据物只占少数格子，成本可忽略）。</summary>
		private static bool SameDto(object a, object b)
		{
			if (a == null && b == null) return true;
			if (a == null || b == null) return false;
			return JsonConvert.SerializeObject(a) == JsonConvert.SerializeObject(b);
		}
	}
}
