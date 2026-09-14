using Newtonsoft.Json;
using SciencePotato.Scripts.Common.Domain;
using System;
using System.IO;

namespace SciencePotato.Scripts.Core.Save
{
	/// <summary>
	/// （v0.3 / WP-3.2）游戏日存档。
	/// <para>为什么需要它：地图/任务/迷雾/资源/科技各有自己的写入点，唯独\"当前游戏日\"没有归属 ——
	/// 不存它，读档后时间从 0 重新开始，所有按日推进的任务都会错位。</para>
	/// <para>（v0.3 / WP-3.3）接入 <see cref="ISaveStore"/> 后**不再自成一份文件**：
	/// 日期并入统一存档的**文件头**（`SaveFile.Day`），因为\"日期\"与\"世界状态\"必须来自同一个时刻，
	/// 两份文件就会出现\"地图是第 30 日、时钟是第 31 日\"这种无法解释的状态。</para>
	/// </summary>
	public interface IClockRepository
	{
		/// <summary>读取存档的当前日（无存档返回 0）。</summary>
		double LoadDay(string sessionId);

		/// <summary>写入当前日。</summary>
		void SaveDay(string sessionId, double day);
	}

	/// <summary>（v0.3 / WP-3.2 + WP-3.3）`IClockRepository` 的 JSON 实现（按会话分文件，或写入统一存档文件头）。</summary>
	public sealed class FileClockRepository(string filePathPrefix, ISaveStore store = null) : IClockRepository
	{
		private readonly string _prefix = filePathPrefix;
		private readonly ISaveStore _store = store;

		public double LoadDay(string sessionId)
		{
			if (_store != null) return _store.Day; // 统一存档：日期在文件头

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
			if (_store != null)
			{
				_store.SetDay(day); // 标脏 → 存档点统一原子落盘
				return;
			}

			File.WriteAllText(Path(sessionId), JsonConvert.SerializeObject(new ClockSave { Day = day }, Formatting.Indented));
		}

		private string Path(string sessionId) => _prefix + (string.IsNullOrWhiteSpace(sessionId) ? "session" : sessionId);

		private sealed class ClockSave
		{
			public double Day { get; set; }
		}
	}
}