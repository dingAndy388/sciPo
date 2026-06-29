using System;

namespace SciencePotato.Scripts.Common.Domain
{
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