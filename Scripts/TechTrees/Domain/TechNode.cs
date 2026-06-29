using Newtonsoft.Json;
using System.Collections.Generic;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public class TechNode
	{
		public string Id { get; }
		[JsonIgnore]
		public ITechNodeConfig Config { get; private set; }
		public bool Researched { get; private set; }

		[JsonConstructor]
		private TechNode(string id, bool researched)
		{
			Id = id;
			Researched = researched;
		}

		public TechNode(ITechNodeConfig config)
		{
			Id = config.Id;
			Config = config;
			Researched = false;
		}

		public void HydrateConfig(ITechNodeConfig config)
		{
			Config = config;
		}

		public void MarkResearched()
		{
			Researched = true;
		}
	}
}
