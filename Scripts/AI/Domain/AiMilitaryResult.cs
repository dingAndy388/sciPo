using SciencePotato.Scripts.Common.Domain;

namespace SciencePotato.Scripts.AI.Domain
{
	/// <summary>
	/// （v0.7.5 / WP-6.4）**一次军事行动的产物**：这一拍投多少军费、训练了什么、有没有把人拉回来守家。
	/// </summary>
	public sealed class AiMilitaryResult
	{
		public string MapId { get; init; }

		public int OwnerId { get; init; }

		public int Day { get; init; }

		public AiThreatLevel Threat { get; init; }

		/// <summary>本拍计划投在军事上的资源比例（<see cref="AiMilitaryPolicy"/>）。</summary>
		public float MilitaryShare { get; init; }

		/// <summary>真去训练了哪个单位（<c>null</c> = 没训练）。</summary>
		public string TrainedUnit { get; init; }

		/// <summary>承训的军营（uid）。</summary>
		public string TrainingBuildingUId { get; init; }

		/// <summary>本拍被叫回来守家的单位数。</summary>
		public int DefendersRegrouped { get; init; }

		public string Reason { get; init; }

		public bool Trained => TrainedUnit != null;

		public override string ToString()
			=> $"owner={OwnerId} {Day}日 威胁={Threat} 军费={MilitaryShare:0.##} " +
			   $"{(Trained ? $"训练 {TrainedUnit}" : "未训练")}{(DefendersRegrouped > 0 ? $"；守家 {DefendersRegrouped} 人回撤" : string.Empty)} —— {Reason}";
	}
}
