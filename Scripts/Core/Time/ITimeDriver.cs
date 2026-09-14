namespace SciencePotato.Scripts.Core.Time
{
	/// <summary>
	/// （v0.3 / WP-1.1）时间推进器：把"真实时间"换算为"游戏日"并推进 <see cref="GameClock"/>。
	/// Godot 侧由 Node 每帧调用（WP-1.2 的 GodotTimeDriver）；测试侧由手动驱动实现调用。
	/// </summary>
	public interface ITimeDriver
	{
		void Advance(double realDeltaSeconds);
	}
}
