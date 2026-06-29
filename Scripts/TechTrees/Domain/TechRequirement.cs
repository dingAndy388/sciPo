using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public class TechRequirement : IRequirement
	{
		private readonly TechTree _tree;
		private readonly List<string> _requirements;

		public TechRequirement(TechTree tree, List<string> requirements)
		{
			_tree = tree;
			_requirements = requirements;
		}

		public bool IsMet()
		{
			return _requirements.All(c => _tree.IsResearched(c));
		}
	}
}