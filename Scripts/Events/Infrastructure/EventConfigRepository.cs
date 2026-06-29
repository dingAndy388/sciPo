using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Events.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Events.Infrastructure
{
	public class EventConfigRepository : GenericConfigRepository<IEventsConfig, EventsConfigDto>, IEventConfigRepository
	{
		private IEventsConfig _config;

		public EventConfigRepository(string json) : base(json)
		{
			base.Load();
			_config = base.Data;
		}

		public List<IEventConfig> GetAllEvents()
		{
			return _config?.Events ?? new List<IEventConfig>();
		}
	}
}