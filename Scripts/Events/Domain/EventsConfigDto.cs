using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Events.Domain
{
	public class EventsConfigDto : IEventsConfig
	{
		[JsonProperty("Events")]
		public List<EventConfigDto> EventsData { get; set; }

		List<IEventConfig> IEventsConfig.Events
			=> EventsData?.Select(e => (IEventConfig)e).ToList()
			   ?? new List<IEventConfig>();
	}
}