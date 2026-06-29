using Godot;
using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	public partial class GodotTimeService : Node, ITimeService
	{
		public float Scale { get; set; } = 1f;

		private ITaskRepository _taskRepo;
		private List<ITickable> _subscribers = new();

		public void SetTaskRepository(ITaskRepository taskRepo)
		{
			_taskRepo = taskRepo;
		}

		public void Register(ITickable tickable)
		{
			_subscribers.Add(tickable);

			if (tickable is IProgressTask p && _taskRepo != null)
			{
				var snapshot = p.GetSnapshot();
				_taskRepo.AddTask(snapshot.MapId, snapshot);
			}
		}

		public void Unregister(ITickable tickable)
		{
			_subscribers.Remove(tickable);

			if (tickable is IProgressTask p && _taskRepo != null)
			{
				var snapshot = p.GetSnapshot();
				_taskRepo.RemoveTask(snapshot.MapId, snapshot);
			}
		}

		public override void _Process(double delta)
		{
			float scaledDelta = (float)delta * Scale;

			for (int i = _subscribers.Count - 1; i >= 0; i--)
			{
				var sub = _subscribers[i];
				sub.OnTick(scaledDelta);

				if (sub is IProgressTask p && _taskRepo != null)
				{
					var snapshot = p.GetSnapshot();
					_taskRepo.AddTask(snapshot.MapId, snapshot);
				}
			}
		}
	}
}