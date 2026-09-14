namespace SciencePotato.Scripts.Core.Time
{
	/// <summary>
	/// （v0.3 / WP-1.1；WP-1.2 起为 Godot 侧唯一入口）时间推进器：把"真实时间"换算为"游戏日"并推进 <see cref="GameClock"/>。
	/// <para>实现：Godot 侧 <c>GodotTimeDriver</c>（Node，每帧调用）；测试侧 <c>ManualTimeDriver</c>（手动推进）。</para>
	/// </summary>
	public interface ITimeDriver
	{
		/// <summary>按真实秒推进；返回本次实际派发的**游戏日数**（0 = 暂停或参数非法）。</summary>
		int Advance(double realDeltaSeconds);
	}
}
