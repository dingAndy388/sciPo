using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Domain
{
	public interface IBuildingConfig
	{
		string BuildingId { get; }
		string Name { get; }
		Dictionary<string, float> ResourceCost { get; }
		List<string> TerrainRequirements { get; }
		Dictionary<string, List<string>> TechRequirements { get; }
		List<Modifier> Modifiers { get; }
		float Duration { get; }
		List<string> Actions { get; }
		int VisionRadius { get; }
		bool IsHousing { get; }
		int PopulationRadius { get; }
		int PopulationCap { get; }
		int PopulationGrowthInterval { get; }

		/// <summary>
		/// （v0.3 / WP-2.5）**可训练单位 Id 列表** —— 设计稿规定所有单位由建筑产出
		/// （工坊 → 工人；军营 → 民兵 / 弓箭手；学院 lv.II → 学者）。空列表 = 该建筑不能训练。
		/// <para>与 <see cref="Actions"/> 的分工：<c>Actions</c> 是"能力门控"（UI 按钮显示与否），
		/// 本字段是"可训练什么"（服务端校验的权威名单）。</para>
		/// </summary>
		List<string> TrainableUnits { get; }

		/// <summary>
		/// （v0.3 / WP-2.5）**训练队列上限**（设计稿：每个建筑 5 个排队名额，同时只训练 1 个）。
		/// 缺省 = <see cref="BuildingConfigDto.DefaultTrainingQueueLimit"/>。
		/// </summary>
		int TrainingQueueLimit { get; }
	}
}
