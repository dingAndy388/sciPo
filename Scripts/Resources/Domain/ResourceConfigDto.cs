using System.Collections.Generic;

namespace SciencePotato.Scripts.Resources.Domain
{
	public class ResourceConfigDto : IResourceConfig
	{
		public string Name { get; set; }
		public string Description { get; set; }
		public int GrowInterval { get; set; }
		public float BaseGrowth { get; set; }
		public float BaseValue { get; set; }
		public float BaseLimit { get; set; }
		public List<string> DependentModifiers { get; set; }
	}
}