using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Construction.Domain
{
	public class BuildingConfigDto : IBuildingConfig
	{
		/// <summary>（v0.3 / WP-2.5）训练队列上限的设计缺省值（每个建筑 5 个排队名额）。</summary>
		public const int DefaultTrainingQueueLimit = 5;

		public string BuildingId { get; set; }
		public string Name { get; set; }
		public Dictionary<string, float> ResourceCost { get; set; }
		public List<string> TerrainRequirements { get; set; }
		public Dictionary<string, List<string>> TechRequirements { get; set; }
		public List<Modifier> Modifiers { get; set; }
		public float Duration { get; set; }
		public List<string> Actions { get; set; }
		public int VisionRadius { get; set; }
		public bool IsHousing { get; set; }
		public int PopulationRadius { get; set; }
		public int PopulationCap { get; set; }
		public int PopulationGrowthInterval { get; set; }

		/// <summary>（v0.3 / WP-2.5）可训练单位 Id；表里省略时为空表（= 不可训练），而不是 null。</summary>
		public List<string> TrainableUnits { get; set; } = new List<string>();

		/// <summary>（v0.3 / WP-2.5）训练队列上限；表里省略时取 <see cref="DefaultTrainingQueueLimit"/>。</summary>
		public int TrainingQueueLimit { get; set; } = DefaultTrainingQueueLimit;
	}
}