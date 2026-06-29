using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Events.Domain
{
	public class EventConfigDto : IEventConfig
	{
		public string EventId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public float TriggerChance { get; set; }
		public int Duration { get; set; }
		public List<Modifier> Modifiers { get; set; }
		public Dictionary<string, float> ResourcePrerequisites { get; set; }
		public Dictionary<string, List<string>> TechPrerequisites { get; set; }
	}
}