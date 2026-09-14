using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Events.Domain
{
	public class EventConfigDto : IEventConfig
	{
		public string EventId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }

		/// <summary>（v0.3 / WP-2.8 改名）触发概率 **%/日**：0.2%/日 = 0.002（旧名 `TriggerChance` 已废弃）。</summary>
		public float TriggerChancePerDay { get; set; }

		public int Duration { get; set; }
		public List<Modifier> Modifiers { get; set; }
		public Dictionary<string, float> ResourcePrerequisites { get; set; }
		public Dictionary<string, List<string>> TechPrerequisites { get; set; }
	}
}