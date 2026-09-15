using SciencePotato.Scripts.Common.Domain;

namespace SciencePotato.Scripts.AI.Domain
{
	/// <summary>
	/// （v0.7.4 / WP-6.3）**一次经济分配的结果**：本轮到底研究/建造了什么（没做也要说明为什么）。
	/// <para>与 <see cref="AiDecision"/>（为什么这么想）配成一对 ⇒ `N3` 复盘时"想 → 做 → 结果"三段齐全。</para>
	/// </summary>
	public sealed class AiEconomyResult
	{
		public string MapId { get; init; }

		public int OwnerId { get; init; }

		public int Day { get; init; }

		/// <summary>本轮开始的建造（建筑 Id；<c>null</c> = 没建）。</summary>
		public string BuildOrder { get; init; }

		/// <summary>建造落点。</summary>
		public HexCubePosition? BuildPosition { get; init; }

		/// <summary>被派去建造的单位（`BuilderBinding`）。</summary>
		public string BuilderUId { get; init; }

		/// <summary>本轮开始的研究（科技树 Id）。</summary>
		public string ResearchTree { get; init; }

		/// <summary>本轮开始的研究（节点 Id）。</summary>
		public string ResearchNode { get; init; }

		/// <summary>人类可读的说明（为什么没建/没研究也写这里）。</summary>
		public string Reason { get; init; }

		public bool Built => BuildOrder != null;

		public bool Researched => ResearchNode != null;

		public override string ToString()
		{
			string built = Built ? $"建造 {BuildOrder}@({BuildPosition?.q},{BuildPosition?.r})" : "未建造";
			string research = Researched ? $"研究 {ResearchTree}:{ResearchNode}" : "未研究";
			return $"owner={OwnerId} {Day}日 {built}；{research} —— {Reason}";
		}
	}
}
