using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Resources.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using TechTreeDomain = SciencePotato.Scripts.TechTree.Domain;

namespace SciencePotato.Scripts.TechTree.Application
{
	public class TechTreesAppService
	{
		private readonly TechTreeDomain.ITechTreesRepository _repo;
		private readonly TechTreeDomain.ITechTreesConfigRepository _configRepo;
		private readonly ResourcesAppService _resource;
		private readonly ModifierAppService _modifier;
		private readonly ITimeService _time;

		public TechTreesAppService(
			TechTreeDomain.ITechTreesRepository repo,
			TechTreeDomain.ITechTreesConfigRepository configRepo,
			ResourcesAppService resourceAppService,
			ModifierAppService modifierAppService,
			ITimeService timeService)
		{
			_repo = repo;
			_configRepo = configRepo;
			_resource = resourceAppService;
			_modifier = modifierAppService;
			_time = timeService;
		}

		public TechTreeDomain.TechTree GetOrCreateTechTree(string mapId, int ownerId, string treeId)
		{
			var tree = _repo.GetTreeById(mapId, ownerId, treeId);
			if (tree != null) return tree;

			var config = _configRepo.GetTechTreeConfig(treeId);
			tree = new TechTreeDomain.TechTree(treeId, ownerId, config);
			_repo.SaveTree(mapId, ownerId, treeId, tree);
			return tree;
		}

		public TechTreeDomain.TechRequirement GetTechTreeRequirement(string mapId, int ownerId, string treeId, List<string> requirements)
		{
			var tree = GetOrCreateTechTree(mapId, ownerId, treeId);
			return new TechTreeDomain.TechRequirement(tree, requirements);
		}

		public void Research(string mapId, int ownerId, string treeId, string nodeId)
		{
			var tree = GetOrCreateTechTree(mapId, ownerId, treeId);

			float cost = tree.GetCost(nodeId);
			float duration = tree.GetDuration(nodeId);

			Consumption consumption = new("Idea", cost);
			var contract = _resource.CreateResourceConsumption(consumption, mapId, ownerId);

			if (!contract.IsConsumable()) return;

			contract.Consume();

			LinearTask task = new(0, duration, nodeId, "Research", false, "none", mapId, ownerId); // UID "none" — not an Occupant
			task.OnCompleted += () =>
			{
				tree.Research(nodeId);
				_repo.SaveTree(mapId, ownerId, treeId, tree);

				var modifiers = tree.GetModifiers(nodeId);
				if (modifiers.Count > 0)
				{
					_modifier.AddModifiers(mapId, ownerId, nodeId, modifiers);
				}

				_time.Unregister(task);
			};

			_time.Register(task);
		}

		public LinearTask CreateResearchTask(string mapId, int ownerId, TaskSnapshot snapshot)
		{
			var tree = GetOrCreateTechTree(mapId, ownerId, snapshot.Id);

			LinearTask task = new(snapshot.Progress, snapshot.Target, snapshot.Id, snapshot.Type, snapshot.IsCompleted, snapshot.UId, mapId, snapshot.OwnerId);
			task.OnCompleted += () =>
			{
				tree.Research(snapshot.Id);
				_repo.SaveTree(mapId, ownerId, snapshot.Id, tree);

				var modifiers = tree.GetModifiers(snapshot.Id);
				if (modifiers.Count > 0)
				{
					_modifier.AddModifiers(mapId, snapshot.OwnerId, snapshot.UId, modifiers);
				}

				_time.Unregister(task);
			};
			return task;
		}
	}
}