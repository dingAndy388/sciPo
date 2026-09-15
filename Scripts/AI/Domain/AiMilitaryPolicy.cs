using System;

namespace SciencePotato.Scripts.AI.Domain
{
	/// <summary>
	/// （v0.7.5 / WP-6.4）**军费分级策略**：AI 该把多少资源投在军事上（`design/AI.md` 军事机制）。
	/// <para>口径（`D103`，源自设计稿 line 38/78/110 + 用户裁定 `D93` 的"60 年 = 360 日"）：</para>
	/// <list type="bullet">
	/// <item>**不造兵窗口内**（`day &lt; noMilitaryDays`）：**0%** —— AI 前期只顾发展；</item>
	/// <item>窗口之后、**看不见敌人**：**5%**（设计稿"60 年后 0%（无威胁）→ 取 5% 作为基础守备"）；</item>
	/// <item>窗口之后、**看得见敌人**：**10% → 15% → 20%** 随威胁等级递增（设计稿"玩家暴露后 10~20%"）。</item>
	/// </list>
	/// <para>这是**唯一出处**：`AiService`（决策记录里的"计划军费占比"）与 `AiMilitaryService`（真去造兵）
	/// 都调它，避免"决策说的数"与"实际花的数"两套口径。</para>
	/// </summary>
	public static class AiMilitaryPolicy
	{
		/// <summary>无威胁时的基础军费（窗口后）。</summary>
		public const float BaseShare = 0.05f;

		/// <summary>各威胁等级下的军费占比（窗口后；`None` 用 <see cref="BaseShare"/>）。</summary>
		public static float ShareFor(AiThreatLevel threat) => threat switch
		{
			AiThreatLevel.None => BaseShare,
			AiThreatLevel.Low => 0.10f,
			AiThreatLevel.Medium => 0.15f,
			AiThreatLevel.High => 0.20f,
			AiThreatLevel.Lethal => 0.20f,
			_ => BaseShare,
		};

		/// <summary>
		/// 本拍计划投入军事的资源比例：窗口内 / 生存优先 → **0**（先把经济和口粮拉起来，`design/AI.md`"生存优先"）。
		/// </summary>
		public static float ShareFor(int day, int noMilitaryDays, AiThreatLevel threat, AiFocus focus)
		{
			if (focus == AiFocus.Survival) return 0f;
			if (day < noMilitaryDays) return 0f;
			return Math.Max(0f, ShareFor(threat));
		}

		/// <summary>威胁等级是否到了"该把人拉回来守家"的程度（`design/AI.md` 的"优先防御"）。</summary>
		public static bool ShouldRegroup(AiThreatLevel threat) => threat >= AiThreatLevel.High;
	}
}
