using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public interface ITechNodeConfig
	{
		string Id { get; }
		List<string> Prerequisites { get; }
		float Cost { get; }
		float Duration { get; }
		List<Modifier> Modifiers { get; }
	}
}
