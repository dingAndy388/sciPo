using System.Collections.Generic;

namespace SciencePotato.Scripts.Events.Domain
{
	public interface IEventsConfig
	{
		List<IEventConfig> Events { get; }
	}
}