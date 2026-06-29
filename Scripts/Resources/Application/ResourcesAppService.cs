using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Resources.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Resources.Application
{
	public class ResourcesAppService
	{
		private readonly IResourcesRepository _repo;
		private readonly IResourcesConfigRepository _configRepo;
		private readonly ITimeService _time;
		private readonly IModifierRepository _modifierRepo;

		public ResourcesAppService(
			IResourcesRepository repo,
			IResourcesConfigRepository configRepo,
			ITimeService timeService,
			IModifierRepository modifierRepo)
		{
			_repo = repo;
			_configRepo = configRepo;
			_time = timeService;
			_modifierRepo = modifierRepo;
		}

		public ResourcesPool GetOrCreatePool(string mapId, int ownerId)
		{
			var pool = _repo.LoadResourcesPool(mapId, ownerId);
			if (pool != null)
			{
				pool.InitializeFromConfig(_configRepo.GetResourcesPoolConfig());
				return pool;
			}

			var config = _configRepo.GetResourcesPoolConfig();
			pool = new ResourcesPool(ownerId);
			pool.InitializeFromConfig(config);
			_repo.SaveResources(mapId, ownerId, pool);
			StartGrowthTasks(mapId, ownerId, pool, config);
			return pool;
		}

		public IConsumable CreateResourceConsumption(Consumption consumption, string mapId, int ownerId)
		{
			var pool = GetOrCreatePool(mapId, ownerId);
			return new ResourcesConsumption(pool, consumption.Type, consumption.Amount);
		}

		public void AddResource(string type, float amount, string mapId, int ownerId)
		{
			var pool = GetOrCreatePool(mapId, ownerId);
			pool.AddValue(type, amount);
			_repo.SaveResources(mapId, ownerId, pool);
		}

		private void StartGrowthTasks(string mapId, int ownerId, ResourcesPool pool, IResourcesPoolConfig config)
		{
			if (config?.Resources == null) return;

			foreach (var resource in config.Resources)
			{
				if (resource.GrowInterval <= 0 || resource.BaseGrowth <= 0) continue;

				var task = new IntervalTask(
					0, resource.GrowInterval, resource.Name, "ResourceGrowth", "none", mapId, ownerId);

				task.OnCompleted += () =>
				{
					var modifiers = new ModifierManager(_modifierRepo.LoadModifiers(mapId, ownerId));
					var targets = resource.DependentModifiers?.ToArray() ?? new string[0];
					float growth = modifiers.GetValue(targets, resource.BaseGrowth);

					var currentPool = _repo.LoadResourcesPool(mapId, ownerId);
					if (currentPool == null) return;

					float current = currentPool.GetValue(resource.Name);
					float limit = currentPool.GetLimit(resource.Name);

					if (current < limit)
					{
						currentPool.AddValue(resource.Name, growth);
						_repo.SaveResources(mapId, ownerId, currentPool);
					}
				};

				_time.Register(task);
			}
		}
	}
}
