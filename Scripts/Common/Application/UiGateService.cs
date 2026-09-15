using SciencePotato.Scripts.TechTree.Application;
using SciencePotato.Scripts.TechTree.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Application
{
	/// <summary>
	/// （v0.8.9 / `WP-4.14`）**UI 门控**：设计稿里"解锁资源面板""开启研究功能""解锁资源收获面板"这类
	/// **科技 → 界面能力**的开关，统一由本服务回答（UI 只查它，不自己判断科技进度）。
	/// <list type="bullet">
	/// <item>`resource_panel` —— 计数（数学树根）解锁资源面板；</item>
	/// <item>`research` —— 同一条解锁"研究"功能；</item>
	/// <item>`harvest_panel` —— 算术解锁资源收获面板（"查看资源的加减项"，配合 `WP-4.13`）。</item>
	/// </list>
	/// <para>数据来源 = 科技节点的 `UnlocksUi` 列表（见 `ITechNodeConfig`）：所以"哪条科技解锁哪个面板"
	/// 是**填表**的事，不是代码的事；`WP-7.3` 落地 93 节点表时按设计稿补齐即可。</para>
	/// </summary>
	public sealed class UiGateService
	{
		public const string ResourcePanel = "resource_panel";
		public const string Research = "research";
		public const string HarvestPanel = "harvest_panel";

		private readonly TechTreesAppService _tech;
		private readonly ITechTreesConfigRepository _configs;

		public UiGateService(TechTreesAppService tech, ITechTreesConfigRepository configs)
		{
			_tech = tech;
			_configs = configs;
		}

		/// <summary>该势力是否已解锁某个界面能力（任一**已研究**节点的 `UnlocksUi` 含该键即为真）。</summary>
		public bool IsUnlocked(string mapId, int ownerId, string uiKey)
		{
			if (_tech == null || _configs == null || string.IsNullOrWhiteSpace(uiKey)) return false;

			foreach (string treeId in _configs.GetTreeIds())
			{
				foreach (TechNode node in _tech.GetOrCreateTechTree(mapId, ownerId, treeId).Nodes.Values)
				{
					if (!node.Researched) continue;

					IReadOnlyList<string> unlocks = node.Config?.UnlocksUi;
					if (unlocks == null) continue;

					foreach (string key in unlocks)
						if (string.Equals(key, uiKey, System.StringComparison.OrdinalIgnoreCase)) return true;
				}
			}
			return false;
		}

		public bool IsResourcePanelUnlocked(string mapId, int ownerId) => IsUnlocked(mapId, ownerId, ResourcePanel);

		public bool IsResearchUnlocked(string mapId, int ownerId) => IsUnlocked(mapId, ownerId, Research);

		public bool IsHarvestPanelUnlocked(string mapId, int ownerId) => IsUnlocked(mapId, ownerId, HarvestPanel);
	}
}
