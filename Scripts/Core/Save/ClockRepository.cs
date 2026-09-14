using Newtonsoft.Json;
using System;
using System.IO;

namespace SciencePotato.Scripts.Core.Save
{
	/// <summary>
	/// （v0.3 / WP-3.2）游戏日存档：**只存时钟**（`WP-3.3` 会把它并入统一存档单元）。
	/// <para>为什么单独一份：地图/任务/迷雾/资源/科技各有自己的写入点，唯独"当前游戏日"没有归属 ——
	/// 不存它，读档后时间从 0 重新开始，所有按日推进的任务都会错位。</para>
	/// </summary>
	public interface IClockRepository
	{
		/// <summary>读取存档的当前日（无存档返回 0）。</summary>
		double LoadDay(string sessionId);

		/// <summary>写入当前日。</summary>
		void SaveDay(string sessionId, double day);
	}

	/// <summary>（v0.3 / WP-3.2）`IClockRepository` 的 JSON 实现（按会话分文件）。</summary>
	public sealed class FileClockRepository(string filePathPrefix) : IClockRepository
	{
		private readonly string _prefix = filePathPrefix;

		public double LoadDay(string sessionId)
		{
			string path = Path(sessionId);
			if (!File.Exists(path)) return 0d;

			try
			{
				var save = JsonConvert.DeserializeObject<ClockSave>(File.ReadAllText(path));
				return save?.Day ?? 0d;
			}
			catch (JsonException)
			{
				return 0d; // 损坏的存档：按"第 0 日"处理，由上层决定是否提示（不静默改数据）
			}
		}

		public void SaveDay(string sessionId, double day)
		{
			File.WriteAllText(Path(sessionId), JsonConvert.SerializeObject(new ClockSave { Day = day }, Formatting.Indented));
		}

		private string Path(string sessionId) => _prefix + (string.IsNullOrWhiteSpace(sessionId) ? "session" : sessionId);

		private sealed class ClockSave
		{
			public double Day { get; set; }
		}
	}
}