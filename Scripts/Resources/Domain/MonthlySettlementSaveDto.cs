using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Resources.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.9 / `TIME-14`）**月度结算器的存档形态**：连续赤字月数。
	/// <para>为什么必须落盘：这个计数是 `WP-3.10`「连续赤字 ≥ 36 月 → logistic 减员」的**唯一输入**。
	/// 只在内存里的话，"读档一次赤字史清零" → 玩家可以靠反复读档永久免疫减员（`TIME-14` 家族的老毛病：
	/// 状态不落盘 = 规则不生效）。</para>
	/// <para>产出/需求/赤字明细**不落盘**：它们每次结算都能按配置与池子重新算出来，存下来只会造成两处真相。</para>
	/// </summary>
	public sealed class MonthlySettlementSaveDto
	{
		/// <summary>连续赤字月数：键与 `MonthlySettlementService` 的内存口径一致（`{mapId}_{ownerId}`）。</summary>
		public Dictionary<string, int> DeficitMonths { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
	}
}
