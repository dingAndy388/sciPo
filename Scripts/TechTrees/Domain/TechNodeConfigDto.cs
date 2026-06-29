using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public class TechNodeConfigDto : ITechNodeConfig
	{
		public string Id { get; set; }
		public List<string> Prerequisites { get; set; }
		public float Cost { get; set; }
		public float Duration { get; set; }
		public List<Modifier> Modifiers { get; set; }
	}
}