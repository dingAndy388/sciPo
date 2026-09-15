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
		public List<string> BuildingPrerequisites { get; set; } = new List<string>();
		public bool IsAttachment { get; set; }
		public bool IsHousing { get; set; }
		public int PopulationRadius { get; set; }
		public int PopulationCap { get; set; }
		public int PopulationGrowthInterval { get; set; }

		/// <summary>（v0.3 / WP-2.5）可训练单位 Id；表里省略时为空表（= 不可训练），而不是 null。</summary>
		public List<string> TrainableUnits { get; set; } = new List<string>();

		/// <summary>（v0.3 / WP-2.5）训练队列上限；表里省略时取 <see cref="DefaultTrainingQueueLimit"/>。</summary>
		public int TrainingQueueLimit { get; set; } = DefaultTrainingQueueLimit;

		/// <summary>（v0.3 / WP-2.6）升级目标建筑 Id；空 = 已是最高等级。</summary>
		public string UpgradeTo { get; set; } = "";

		/// <summary>（v0.3 / WP-2.6）升级消耗；表里省略时为空（免费）。</summary>
		public Dictionary<string, float> UpgradeCost { get; set; } = new Dictionary<string, float>();

		/// <summary>（v0.3 / WP-2.6）升级耗时（游戏日）；0 = 无升级。</summary>
		public float UpgradeDuration { get; set; }

		/// <summary>（v0.3 / WP-2.6）升级科技前置；表里省略时为空。</summary>
		public Dictionary<string, List<string>> UpgradeTechRequirements { get; set; } = new Dictionary<string, List<string>>();

		/// <summary>
		/// （v0.3 / WP-3.10）建筑维护费（资源名 → 每月数量）；表里省略时为空表 = 不维护。
		/// <para>**存量建筑表全部留空**（设计稿尚无建筑维护数值）：机制就位、数值不臆造。</para>
		/// </summary>
		public Dictionary<string, float> Maintenance { get; set; } = new Dictionary<string, float>();

		/// <summary>
		/// （v0.7.0 / WP-4.8）是否使用 **HP 模型**：设计稿里**住房与军事建筑**有 HP 且可被夺取。
		/// <para>与 <see cref="HP"/> 联动：<c>HasHP=false</c> 时 HP 字段被忽略（该建筑免疫伤害，攻击只记账）。</para>
		/// </summary>
		public bool HasHP { get; set; }

		/// <summary>最大 HP（设计稿未给数值 ⇒ 见 `log.md` §19.5 `U9`；`WP-7.6` 平衡时复核）。</summary>
		public float HP { get; set; }

		/// <summary>（v0.8.5 / WP-4.5）产出浮动幅度（0.2 = ±20%；0 = 不浮动）。</summary>
		public float OutputVariance { get; set; }

		/// <summary>（v0.8.7 / WP-4.2）效果半径（0 = 只作用自身；>0 = 作用于半径内其他生产建筑）。</summary>
		public int ModifierRange { get; set; }

	}
}