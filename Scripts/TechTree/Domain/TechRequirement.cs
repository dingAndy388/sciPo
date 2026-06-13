using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.TechTree.Domain
{
    public class TechRequirement(TechTree tree,List<string> requirements) : IRequirement
    {
        private TechTree _tree = tree;
        private List<string> _requirements = requirements;

        public bool IsMet()
        {
            return _requirements.All(c=> _tree.IsReasearched(c));
        }
    }
}
