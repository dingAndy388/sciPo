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

		/// <summary>（v0.3 / WP-2.10）领域事件总线（可空 = 无人订阅）。</summary>
		private readonly IDomainEventBus _events;

		/// <summary>
		/// （v0.3 / WP-2.9）**科技树常驻内存**（`(mapId, ownerId, treeId)` → 实例）。
		/// <para>为什么必须缓存：研究是**有状态**的（"本树正在研究哪个节点"决定并发槽位，`F1`），
		/// 若每次都从磁盘反序列化一棵新树，槽位状态会在两次调用之间丢失 —— 树内串行就形同虚设。
		/// 同一份存档只应由一个应用服务实例持有（单写者假设，与 `TaskRepository` 一致）；
		/// 正式的"会话级注册表 + 存档点"归 `WP-3.3`。</para>
		/// </summary>
		private readonly Dictionary<string, TechTreeDomain.TechTree> _trees = new(StringComparer.OrdinalIgnoreCase);

		public TechTreesAppService(
			TechTreeDomain.ITechTreesRepository repo,
			TechTreeDomain.ITechTreesConfigRepository configRepo,
			ResourcesAppService resourceAppService,
			ModifierAppService modifierAppService,
			ITimeService timeService,
			IDomainEventBus eventBus = null)
		{
			_repo = repo;
			_configRepo = configRepo;
			_resource = resourceAppService;
			_modifier = modifierAppService;
			_time = timeService;
			_events = eventBus;
		}

		/// <summary>
		/// 取得（必要时创建）某玩家的某棵树；**每次都挂上跨树前置解析器**（`WP-2.1`）。
		/// <para>（v0.3 / WP-2.9）同一 `(mapId, ownerId, treeId)` 返回**同一个内存实例**（见 <c>_trees</c> 的说明）。</para>
		/// </summary>
		public TechTreeDomain.TechTree GetOrCreateTechTree(string mapId, int ownerId, string treeId)
		{
			string cacheKey = CacheKey(mapId, ownerId, treeId);
			if (_trees.TryGetValue(cacheKey, out var cached))
			{
				AttachCrossTreeLookup(mapId, ownerId, cached);
				return cached;
			}

			var tree = _repo.GetTreeById(mapId, ownerId, treeId);
			if (tree != null)
			{
				_trees[cacheKey] = tree;
				AttachCrossTreeLookup(mapId, ownerId, tree);
				return tree;
			}

			var config = _configRepo.GetTechTreeConfig(treeId);
			tree = new TechTreeDomain.TechTree(treeId, ownerId, config);
			_trees[cacheKey] = tree;
			AttachCrossTreeLookup(mapId, ownerId, tree);
			_repo.SaveTree(mapId, ownerId, treeId, tree);
			return tree;
		}

		private static string CacheKey(string mapId, int ownerId, string treeId)
			=> $"{mapId}|{ownerId}|{treeId}";

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
		/// <para>注意：这是"够不够格"（前置满足、未研究），不含并发槽位；开工判定见
		/// <see cref="CanStartResearch"/>。</para>
		/// </summary>
		public bool CanResearch(string mapId, int ownerId, string treeId, string nodeId)
			=> GetOrCreateTechTree(mapId, ownerId, treeId).CanResearch(nodeId);

		/// <summary>
		/// （v0.3 / WP-2.9）**能否开工研究**：`CanResearch` + 本树并发槽位（默认树内串行）。
		/// 表现层的"研究"按钮应当用它（`WP-4.14`）；`CanResearch` 用于"还差哪个前置"的提示。
		/// </summary>
		public bool CanStartResearch(string mapId, int ownerId, string treeId, string nodeId)
			=> GetOrCreateTechTree(mapId, ownerId, treeId).CanStartResearch(nodeId);

		/// <summary>（v0.3 / WP-2.9）本树正在研究中的节点（UI/调试面板与断言用）。</summary>
		public IReadOnlyCollection<string> GetInProgress(string mapId, int ownerId, string treeId)
			=> GetOrCreateTechTree(mapId, ownerId, treeId).InProgress;

		/// <summary>（v0.3 / WP-2.9）本树的研究并发上限（来自 `Concurrency` 配置）。</summary>
		public int GetConcurrency(string mapId, int ownerId, string treeId)
			=> GetOrCreateTechTree(mapId, ownerId, treeId).Concurrency;

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

			// （v0.3 / WP-2.9）**树内串行**：并发槽位已满（默认 1）或该节点已在研究中 → 拒绝。
			// 三棵树互相独立 ⇒ 最多 3 项并行；`Concurrency` 可配（为"一树多研发"预留）。
			if (!tree.MarkResearchStarted(nodeId)) return;

			float cost = tree.GetCost(nodeId);
			float duration = tree.GetDuration(nodeId);

			Consumption consumption = new("Idea", cost);
			var contract = _resource.CreateResourceConsumption(consumption, mapId, ownerId);

			if (!contract.IsConsumable())
			{
				tree.MarkResearchFinished(nodeId); // 资源不够 → 归还槽位（不能因为一次失败请求把树堵住）
				return;
			}

			contract.Consume();

			// 任务键 = `Research:{treeId}:{nodeId}`（`WP-2.2`）：`UId` 承载**所属科技树** ——
			// 旧实现把 `UId` 填成 "none"、`Id` 填 nodeId，续跑时误把 nodeId 当 treeId（`TECH-01`/`TECH-04`）。
			LinearTask task = new(0, duration, nodeId, "Research", false, treeId, mapId, ownerId);
			task.OnCompleted += () =>
			{
				tree.Research(nodeId);
				_repo.SaveTree(mapId, ownerId, treeId, tree);

				var modifiers = tree.GetModifiers(nodeId);
				if (modifiers.Count > 0)
				{
					_modifier.AddModifiers(mapId, ownerId, nodeId, modifiers);
				}

				// 推送（v0.3 / WP-2.10 / `TECH-05`）：研究完成原本没有任何推送（UI 提示/内容联动都靠轮询）
				_events?.Publish(new ResearchCompletedEvent(mapId, ownerId, treeId, nodeId));

				_time.Unregister(task);
			};

			_time.Register(task);
		}

		/// <summary>
		/// （v0.3 / WP-2.6 + WP-2.9）读档续跑研究：**树 Id 从快照的 `UId` 取**（新口径）；
		/// 旧快照（`UId` 为空或 `none`）无法判断所属树 → 返回 null 由调用方决定策略（见 §18.4.2）。
		/// </summary>
		public LinearTask CreateResearchTask(string mapId, int ownerId, TaskSnapshot snapshot)
		{
			if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.UId) || snapshot.UId == "none") return null;

			string treeId = snapshot.UId;
			var tree = GetOrCreateTechTree(mapId, ownerId, treeId);

			LinearTask task = new(snapshot.Progress, snapshot.Target, snapshot.Id, snapshot.Type, snapshot.IsCompleted, snapshot.UId, mapId, snapshot.OwnerId);
			tree.MarkResearchStarted(snapshot.Id); // 重新占用研究槽位（`WP-2.9`）

			task.OnCompleted += () =>
			{
				tree.Research(snapshot.Id);
				_repo.SaveTree(mapId, ownerId, treeId, tree);

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