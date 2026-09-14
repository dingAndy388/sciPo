namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / WP-1.5 重标定）可被时间驱动的对象。
	/// <para><c>delta</c> 的单位是**游戏日**（不再是真实秒）：时间源逐日派发，
	/// 因此每日恰好调用一次（见 <see cref="SciencePotato.Scripts.Core.Time.TimeConstants.DaysPerTick"/>）；
	/// 任务的 <c>Target</c> 同样以"日"计（如 `Duration=30` ⇒ 30 个游戏日）。</para>
	/// </summary>
	public interface ITickable
	{
		void OnTick(float delta);
	}
}
