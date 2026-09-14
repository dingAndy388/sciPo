using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Resources.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Resources.Application
{
	public partial class ResourcesAppService
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
				// 口径（v0.3 / WP-1.5）：GrowInterval 的单位是**游戏日**，30 = 月结（C2 / TIME-14 的最小口径）。
				// 判定只看 GrowInterval：`BaseGrowth=0` 但依赖 Modifier 的资源（Idea 全靠建筑/科技产出）
				// 同样必须按月到账，否则建筑 Modifiers 永远无处落地（M0-2 ②「School 250 idea/月」）。
				float intervalDays = resource.GrowInterval > 0 ? resource.GrowInterval : 0f;
				if (intervalDays <= 0f) continue;

				// v0.3 / WP-3.2：任务创建抽到 StartGrowthTask（新建与读档恢复共用同一份实现）
				StartGrowthTask(mapId, ownerId, resource, intervalDays, 0f);
			}
		}

		/// <summary>
		/// 注册单条资源的月结任务（新建 = 进度 0；读档恢复 = 回填存档进度，`WP-3.2`）。
		/// </summary>
		private void StartGrowthTask(string mapId, int ownerId, IResourceConfig resource, float intervalDays, float initialProgress)
		{
			var task = new IntervalTask(initialProgress, intervalDays, resource.Name, "ResourceGrowth", "none", mapId, ownerId);

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
