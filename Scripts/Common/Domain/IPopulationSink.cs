namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.10 / `C9`、`RES-01`）**人口减员的执行口**：经济侧算出"这次该少几个人"，
	/// 由持有人口的模块（Map）真正把人扣掉。
	/// <para>为什么需要这条契约：减员的**判定**是经济规则（连续赤字 ≥ 36 月 → 年边界按缺口算 logistic 概率），
	/// 但它必须落到**地图上的人口**；而结算器在 Resources 模块，按 `D59` 不能反向依赖 Map
	/// （依赖方向固定为 Map/Units → Resources）。于是照抄维护需求的倒置手法：接口放 `Common/Domain`，
	/// Map 侧实现并注入 —— 结算器只认识"总人口"与"减员"两个动作。</para>
	/// <para>实现方的义务（写进契约，避免两处口径漂移）：① <see cref="GetPopulation"/> = 整图人口总计；
	/// ② <see cref="ApplyPopulationLoss"/> **按地块随机**扣人（不是固定从聚落中心扣 —— 饥荒不该总是死在中心格）、
	/// 绝不扣成负数、有变化就标脏（减员必须能被存档点落盘）。</para>
	/// </summary>
	public interface IPopulationSink
	{
		/// <summary>整图人口总计（地图不存在 → 0，不抛：评估跑在日边界上，抛异常会打断整批派发）。</summary>
		int GetPopulation(string mapId);

		/// <summary>按地块随机减少 <paramref name="amount"/> 人。</summary>
		/// <returns>实际减少的人数（人口不足时少于 <paramref name="amount"/>，不会为负）。</returns>
		int ApplyPopulationLoss(string mapId, int amount);
	}
}
