using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Events.Domain
{
	public interface IEventConfig
	{
		string EventId { get; }
		string Name { get; }
		string Description { get; }
		float TriggerChance { get; }
		int Duration { get; }
		List<Modifier> Modifiers { get; }
		Dictionary<string, float> ResourcePrerequisites { get; }
		Dictionary<string, List<string>> TechPrerequisites { get; }
	}
}