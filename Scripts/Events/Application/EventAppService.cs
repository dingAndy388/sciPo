using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Events.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.TechTree.Application;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Events.Application
{
	public class EventAppService
	{
		private readonly IEventConfigRepository _eventRepo;
		private readonly ResourcesAppService _resources;
		private readonly TechTreesAppService _tech;
		private readonly ModifierAppService _modifier;
		private readonly ITimeService _time;
		private readonly IRandom _random;

		public EventAppService(
			IEventConfigRepository eventRepo,
			ResourcesAppService resources,
			TechTreesAppService tech,
			ModifierAppService modifier,
			ITimeService time,
			IRandom random)
		{
			_eventRepo = eventRepo;
			_resources = resources;
			_tech = tech;
			_modifier = modifier;
			_time = time;
			_random = random;
		}

		public void StartEventsEngine(string mapId, int ownerId)
		{
			var task = new IntervalTask(0, 1f, $"evt_{mapId}_{ownerId}", "EventTick", "none", mapId, ownerId);
			task.OnCompleted += () => TickEvents(mapId, ownerId);
			_time.Register(task);
		}

		private void TickEvents(string mapId, int ownerId)
		{
			var events = _eventRepo.GetAllEvents();
			foreach (var evt in events)
			{
				if (!_random.ProbCodition(evt.TriggerChance))
					continue;

				if (!AllPrerequisitesMet(mapId, ownerId, evt))
					continue;

				ConsumePrerequisites(mapId, ownerId, evt);

				if (evt.Modifiers != null && evt.Modifiers.Count > 0)
				{
					_modifier.AddModifiers(mapId, ownerId, evt.EventId, evt.Modifiers);
				}

				if (evt.Duration > 0)
				{
					var expireTask = new LinearTask(0, evt.Duration, evt.EventId, "EventExpire", false, "none", mapId, ownerId);
					expireTask.OnCompleted += () =>
					{
						_modifier.RemoveModifiersBySourceId(mapId, ownerId, evt.EventId);
						_time.Unregister(expireTask);
					};
					_time.Register(expireTask);
				}
			}
		}

		private bool AllPrerequisitesMet(string mapId, int ownerId, IEventConfig evt)
		{
			if (evt.ResourcePrerequisites != null)
			{
				foreach (var kvp in evt.ResourcePrerequisites)
				{
					var contract = _resources.CreateResourceConsumption(new Consumption(kvp.Key, kvp.Value), mapId, ownerId);
					if (!contract.IsConsumable()) return false;
				}
			}

			if (evt.TechPrerequisites != null)
			{
				foreach (var kvp in evt.TechPrerequisites)
				{
					var req = _tech.GetTechTreeRequirement(mapId, ownerId, kvp.Key, kvp.Value);
					if (!req.IsMet()) return false;
				}
			}

			return true;
		}

		private void ConsumePrerequisites(string mapId, int ownerId, IEventConfig evt)
		{
			if (evt.ResourcePrerequisites != null)
			{
				foreach (var kvp in evt.ResourcePrerequisites)
				{
					var contract = _resources.CreateResourceConsumption(new Consumption(kvp.Key, kvp.Value), mapId, ownerId);
					contract.Consume();
				}
			}
		}
	}
}