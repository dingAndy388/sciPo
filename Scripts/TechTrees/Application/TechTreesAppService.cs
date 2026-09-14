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

		/// <summary>
		/// 取得（必要时创建）某玩家的某棵树；**每次都挂上跨树前置解析器**（`WP-2.1`）。
		/// </summary>
		public TechTreeDomain.TechTree GetOrCreateTechTree(string mapId, int ownerId, string treeId)
		{
			var tree = _repo.GetTreeById(mapId, ownerId, treeId);
			if (tree != null)
			{
				AttachCrossTreeLookup(mapId, ownerId, tree);
				return tree;
			}

			var config = _configRepo.GetTechTreeConfig(treeId);
			tree = new TechTreeDomain.TechTree(treeId, ownerId, config);
			AttachCrossTreeLookup(mapId, ownerId, tree);
			_repo.SaveTree(mapId, ownerId, treeId, tree);
			return tree;
		}

		/// <summary>
		/// （v0.3 / WP-2.1）跨树前置的解析器：`(treeId, nodeId) → 是否已研究`。
		/// <para>同树（含 `TreeId` 为空）直接查本树；跨树走 <c>_repo.GetTreeById</c> —— **只读**载入兄弟树，
		/// 不用 `GetOrCreateTechTree`，否则"查询能否研究"会变成"顺手建一棵树并存盘"。</para>
		/// <para>兄弟树尚未创建 / 未研究 → 未满足（fail closed，见 <see cref="TechTreeDomain.TechTree.AttachResearchLookup"/>）。</para>
		/// </summary>
		private void AttachCrossTreeLookup(string mapId, int ownerId, TechTreeDomain.TechTree tree)
		{
			tree.AttachResearchLookup((otherTreeId, otherNodeId) =>
			{
				if (string.IsNullOrWhiteSpace(otherTreeId) || otherTreeId == tree.TreeId)
					return tree.IsResearched(otherNodeId);

				var other = _repo.GetTreeById(mapId, ownerId, otherTreeId);
				return other != null && other.IsResearched(otherNodeId);
			});
		}

		/// <summary>
		/// （v0.3 / WP-2.1）**能否研究**的对外入口（M0-2 ① 的验收面：物理树根节点在数学树研究后才可研究）。
		/// </summary>
		public bool CanResearch(string mapId, int ownerId, string treeId, string nodeId)
			=> GetOrCreateTechTree(mapId, ownerId, treeId).CanResearch(nodeId);

		public TechTreeDomain.TechRequirement GetTechTreeRequirement(string mapId, int ownerId, string treeId, List<string> requirements)
		{
			var tree = GetOrCreateTechTree(mapId, ownerId, treeId);
			return new TechTreeDomain.TechRequirement(tree, requirements);
		}

		public void Research(string mapId, int ownerId, string treeId, string nodeId)
		{
			var tree = GetOrCreateTechTree(mapId, ownerId, treeId);

			// （v0.3 / WP-2.1）前置未满足直接拒绝：否则会"先扣 Idea、再在完成回调里被 `Tree.Research` 静默丢掉"，
			// 玩家付出资源却什么也没得到。跨树前置（`TECH-07`）在本方法里第一次真正生效。
			if (!tree.CanResearch(nodeId)) return;

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