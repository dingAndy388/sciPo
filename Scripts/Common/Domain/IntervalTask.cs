using SciencePotato.Scripts.Common.Application;
using System;

namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / WP-1.5 重标定）周期任务：每过 <c>Target</c> **游戏日**触发一次并循环。
	/// <para>构造参数 <c>interval</c> 的单位是游戏日（旧实现为秒）。时间源逐日调用 <c>OnTick(1)</c>，
	/// 因此"第 30 日首次结算"这类口径天然成立（从 0 计时，无需对齐月边界）。</para>
	/// <para>完成回调由注册方自行 <see cref="ITimeService.Unregister"/>（循环任务的回收见 `A7`）。</para>
	/// </summary>
	public class IntervalTask : IProgressTask
	{
		private readonly string _mapId;
		private readonly int _ownerId;

		public float Progress { get; set; }
		public float Target { get; set; }
		public string Id { get; set; }
		public string Type { get; set; }
		public bool IsCompleted { get; set; }
		public string UId { get; set; }

		public event Action OnCompleted;

		public IntervalTask(float progress, float interval, string id, string type, string uid, string mapId, int ownerId)
		{
			Progress = progress;
			Target = interval;
			Id = id;
			Type = type;
			UId = uid;
			IsCompleted = false;
			_mapId = mapId;
			_ownerId = ownerId;
		}

		public TaskSnapshot GetSnapshot()
		{
			return new TaskSnapshot
			{
				MapId = _mapId,
				OwnerId = _ownerId,
				Progress = Progress,
				Target = Target,
				Id = Id,
				Type = Type,
				UId = UId,
				IsCompleted = IsCompleted
			};
		}

		public void OnTick(float delta)
		{
			if (!IsCompleted)
			{
				Progress += delta;
				if (Progress >= Target)
				{
					Progress -= Target;
					OnCompleted?.Invoke();
				}
			}
		}
	}
}