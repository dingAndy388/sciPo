using Newtonsoft.Json;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Resources.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Resources.Application
{
	/// <summary>
	/// （v0.3 / WP-3.9）**月度结算器的存档/读档**（与 `EventAppService.SaveEvents` 同一套路）。
	/// <para>两件事必须一起做对：</para>
	/// <list type="number">
	/// <item>**连续赤字月数落盘**（`settlement:{mapId}` 分区）—— 否则读档 = 减员史清零（见 <see cref="MonthlySettlementSaveDto"/>）；</item>
	/// <item>**月结任务重建**（`RestoreSettlementTask`）—— `LoadWorld` 会 `ITimeService.Reset()` 清空订阅者，
	/// 不重挂则"读档后经济从此不再结算"（与 `D54` 的事件日节拍同族）。进度按快照回填，月结时间点不整体后移。</item>
	/// </list>
	/// </summary>
	public sealed partial class MonthlySettlementService
	{
		/// <summary>统一存档里的分区键（约定 `<子系统>:<业务 id>`）。</summary>
		public static string SectionKey(string mapId) => $"settlement:{mapId}";

		/// <summary>**存档点**：把该地图的连续赤字月数写进统一存档分区（标脏，等待存档点统一原子落盘）。</summary>
		/// <returns>是否真的写了（没有存档单元 → false）。</returns>
		public bool SaveSettlement(string mapId)
		{
			if (_store == null || string.IsNullOrWhiteSpace(mapId)) return false;

			var dto = new MonthlySettlementSaveDto();
			foreach (KeyValuePair<string, int> entry in _deficitMonths)
				if (entry.Key.StartsWith(mapId + "_", StringComparison.Ordinal))
					dto.DeficitMonths[entry.Key] = entry.Value;

			_store.WriteSection(SectionKey(mapId), JsonConvert.SerializeObject(dto, Formatting.Indented));
			return true;
		}

		/// <summary>
		/// **读档**：恢复连续赤字月数，并清掉月结任务的幂等登记（时间总线已被 `Reset()` 清空，必须重挂）。
		/// </summary>
		/// <returns>是否从存档里读到了结算状态。</returns>
		public bool RestoreSettlement(string mapId, int ownerId)
		{
			if (string.IsNullOrWhiteSpace(mapId)) return false;

			// 时间总线已作废：本次读档必须重挂月结任务（幂等标志一并清掉，`D54` 同族）
			_started.Remove(Key(mapId, ownerId));

			if (_store == null) return false;

			string json = _store.ReadSection(SectionKey(mapId));
			if (string.IsNullOrWhiteSpace(json)) return false;

			MonthlySettlementSaveDto dto;
			try
			{
				dto = JsonConvert.DeserializeObject<MonthlySettlementSaveDto>(json);
			}
			catch (JsonException)
			{
				return false; // 认不出的分区：按"没有赤字史"处理，不让读档失败
			}

			if (dto == null) return false;

			// 以盘上状态为准：先清掉该地图的内存态再回填
			foreach (string key in _deficitMonths.Keys.Where(k => k.StartsWith(mapId + "_", StringComparison.Ordinal)).ToList())
				_deficitMonths.Remove(key);

			foreach (KeyValuePair<string, int> entry in dto.DeficitMonths ?? new Dictionary<string, int>())
				_deficitMonths[entry.Key] = entry.Value;

			return true;
		}

		/// <summary>
		/// **按快照重建月结任务 + 回填进度**（`WorldSaveService.RestoreTasks` 调用，与资源/人口/单位任务同一口径）。
		/// </summary>
		/// <returns>重建的任务数（0 或 1）。</returns>
		public int RestoreSettlementTask(string mapId, int ownerId, IReadOnlyDictionary<string, float> progressByKey)
		{
			float progress = 0f;
			progressByKey?.TryGetValue(TaskSnapshot.BuildKey(TaskType, "none", TaskId), out progress);

			return StartSettlement(mapId, ownerId, progress) ? 1 : 0;
		}
	}
}
