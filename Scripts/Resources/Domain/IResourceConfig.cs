using System.Collections.Generic;

namespace SciencePotato.Scripts.Resources.Domain
{
	public interface IResourceConfig
	{
		string Name { get; }
		string Description { get; }
		int GrowInterval { get; }
		float BaseGrowth { get; }
		float BaseValue { get; }
		float BaseLimit { get; }
		List<string> DependentModifiers { get; }
	}
}