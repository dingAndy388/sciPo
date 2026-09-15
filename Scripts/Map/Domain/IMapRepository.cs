using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Domain
{
	public interface IMapRepository
	{
		/// <summary>（v0.9.7 / `WP-5.6`）上一次存档的**脏格数**（增量为 0 ⇒ 调用方跳过写盘）。</summary>
		int LastSavedDirtyCells { get; }

		/// <summary>（v0.9.7 / `WP-5.6`）上一次存档的**总格数**（脏格数 / 总格数 = 本次的实际写入比例）。</summary>
		int LastSavedTotalCells { get; }

		/// <summary>（v0.9.7 / `WP-5.6`）上一次存档是否因"无变化"而**整次跳过写盘**。</summary>
		bool LastSaveSkippedWrite { get; }

		/// <summary>（v0.9.7 / `WP-5.6`）上一次存档的耗时（毫秒；性能基线与用例只做宽松护栏，见 `D83`）。</summary>
		long LastSaveMilliseconds { get; }

		public void SaveMap(Map map);
		public Map LoadMap(string path);
		public void DeleteMap(Map map);
		public IEnumerable<Map> ListMaps();

	}
}
