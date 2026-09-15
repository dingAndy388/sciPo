namespace SciencePotato.Scripts.AI.Domain
{
	/// <summary>
	/// （v0.7.4 / WP-6.3）**AI 的行动出口**：拿到一次 <see cref="AiDecision"/> 后去"下单"
	/// （建造 / 科研；军事归 `WP-6.4`）。
	/// <para>`WP-6.2` 的 `AiService` 只判断不执行（`D100`）；执行侧实现本接口，由组合根挂上。
	/// 好处：① 判断可单测（纯函数）；② 执行必须走**与玩家相同的应用服务**（`A-AI-2` 不作弊），
	/// 因此"能不能建/能不能研究"由那些服务的校验说了算，AI 没有后门。</para>
	/// </summary>
	public interface IAiActionSink
	{
		void Execute(AiDecision decision);
	}
}
