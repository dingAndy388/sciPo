using System.Collections.Generic;

namespace SciencePotato.Scripts.Events.Domain
{
	public interface IEventConfigRepository
	{
		List<IEventConfig> GetAllEvents();
	}
}