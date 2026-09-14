using System.Collections.Generic;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public interface ITechTreesConfigRepository
	{
		ITechNodeConfig GetTechNodeConfig(string treeId, string nodeId);
		ITechTreeConfig GetTechTreeConfig(string treeId);

		/// <summary>（v0.3 / WP-1.4）枚举全部科技树 Id：启动期校验（表非空 / 前置闭合）需要全表视图。</summary>
		IEnumerable<string> GetTreeIds();
	}
}