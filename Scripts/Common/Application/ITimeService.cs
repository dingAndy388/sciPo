using SciencePotato.Scripts.Common.Domain;

namespace SciencePotato.Scripts.Common.Application
{
	/// <summary>
	/// （v0.3 / WP-1.5 重标定）**游戏日节拍总线**：应用服务把周期性任务（<see cref="ITickable"/>）注册进来，
	/// 由时间源逐日派发。
	/// <para>口径变化（原为秒制）：<c>ITickable.OnTick(delta)</c> 的 <c>delta</c> 单位是**游戏日**，
	/// 且每过一个游戏日恰好派发一次（见 <see cref="SciencePotato.Scripts.Core.Time.TimeConstants.DaysPerTick"/>）。</para>
	/// <para>原 <c>Scale</c>（"每真实秒推进多少游戏秒"）已随 `A2` 移交给
	/// <see cref="SciencePotato.Scripts.Core.Time.GameClock"/> 的 <c>Speed</c>（三档 1/3/6 日每真实秒 + 暂停），
	/// 故此处删除 —— 曾经无人设置它（`WIRE-02`）。</para>
	/// <para>实现：无头/测试用 <see cref="SciencePotato.Scripts.Core.Time.GameTimeService"/>；
	/// Godot 侧由 <c>GodotTimeDriver</c> 每帧调用 <c>GameClock.Advance(realDelta)</c> 驱动同一份总线。</para>
	/// </summary>
	public interface ITimeService
	{
		/// <summary>当前游戏日（从 0 起，含不足一日的小数部分向下取整）。</summary>
		int CurrentDay { get; }

		/// <summary>注册周期任务（立即把 <see cref="IProgressTask"/> 的快照登记到任务仓储）。</summary>
		void Register(ITickable tickable);

		/// <summary>注销周期任务（同时从任务仓储移除快照）。</summary>
		void Unregister(ITickable tickable);

		/// <summary>
		/// （v0.3 / WP-2.2）**按实体 uid 批量注销**该实体名下的所有周期任务 —— 建筑被拆（`CON-06`）、单位阵亡时调用。
		/// <para>匹配规则：任务的 <see cref="IProgressTask.UId"/> 等于 uid，或（历史用法）任务的
		/// <see cref="IProgressTask.Id"/> 直接就是该实体的 uid（人口增长写在建筑 uid 上、单位移动写在单位 uid 上）。</para>
		/// </summary>
		/// <returns>被注销的任务数量。</returns>
		int UnregisterByUId(string uid);
	}
}
