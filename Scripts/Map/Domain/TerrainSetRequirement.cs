using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.6.3 / WP-7.2a）**"可建/可行地块"的集合判定**：设计稿的该列是**列表**（如矿场 = 平原、山地），
	/// 语义是"**任一**匹配即可"，而不是"同时是平原又是山地"。
	/// <para>缺陷背景（内容工作暴露）：原实现把列表逐项包成单个 <see cref="TerrainRequirement"/> 再用
	/// <c>All()</c> 合并 ⇒ 两个以上地块的建筑**永远造不出来**（旧表恰好全是单地块，所以一直没被发现）；
	/// 而"列表为空 = 任意地块"这条口径也因此是隐式成立的（`All()` 对空集合为 true）—— 本类显式保留它。</para>
	/// </summary>
	public sealed class TerrainSetRequirement(Map map, HexCubePosition position, IEnumerable<string> terrains) : IRequirement
	{
		private readonly List<string> _terrains = terrains?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();

		public bool IsMet()
		{
			// 空列表 = 不限地块（与校验器的 warning 文案一致："可在任意地形放置"）
			if (_terrains.Count == 0) return true;

			// 通配 "*" = 任意地形
			if (_terrains.Any(t => t == "*")) return true;

			return _terrains.Any(t => map.VerifyTerrain(position, t));
		}
	}
}
