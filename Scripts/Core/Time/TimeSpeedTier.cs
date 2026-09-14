namespace SciencePotato.Scripts.Core.Time
{
	/// <summary>
	/// （v0.3 / WP-1.1）时间流速档位。枚举值即"每个真实秒推进的游戏日数"（0 = 暂停）。
	/// 设计口径：标准 = 1 秒 1 天；第二档 = 1 秒 3 天；第三档 = 1 秒 6 天。
	/// </summary>
	public enum TimeSpeedTier
	{
		/// <summary>暂停（游戏时间冻结，暂停期间的真实时间被丢弃）。</summary>
		Paused = 0,

		/// <summary>标准：1 日 / 真实秒（半分钟 = 一个月，一分钟 = 两个月）。</summary>
		Standard = 1,

		/// <summary>第二档：3 日 / 真实秒（十秒 = 一个月，一分钟 = 六个月）。</summary>
		Fast = 3,

		/// <summary>第三档：6 日 / 真实秒（五秒 = 一个月，一分钟 = 一年）。</summary>
		Fastest = 6,
	}
}
