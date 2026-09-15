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
		/// <summary>
		/// （v0.3 / WP-3.10 / `C9`）**年度窗口的累计赤字**（键同 <see cref="DeficitMonths"/>）。
		/// <para>为什么也要落盘：缺口率 `r = Σ赤字 / Σ需求` 的两个分量都是"上次评估以来"的累计值。
		/// 只存连续赤字月数的话，读档后窗口从 0 重新攒 ⇒ 第一个年边界的 r 会被算小
		/// （反复读档就能把减员概率压下来）—— 与"赤字史清零"同一类漏洞，只是更隐蔽。</para>
		/// </summary>
		public Dictionary<string, float> YearDeficit { get; set; } = new Dictionary<string, float>(StringComparer.Ordinal);

		/// <summary>（v0.3 / WP-3.10 / `C9`）**年度窗口的累计需求**（<see cref="YearDeficit"/> 的分母，同一窗口）。</summary>
		public Dictionary<string, float> YearDemand { get; set; } = new Dictionary<string, float>(StringComparer.Ordinal);


	}
}
