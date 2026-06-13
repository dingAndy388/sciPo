using SciencePotato.Scripts.TechTree.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.TechTree.Application
{
    public class TechTreeAppService(ITechTreeRepository repo)
    {

        private ITechTreeRepository _repo = repo;

        public TechRequirement GetTechTreeRequirement(string treeId, List<string> requirements)
        {
            var tree = _repo.GetTreeById(treeId);
            return new TechRequirement(tree, requirements);
        }

        public void Research(string treeId, string nodeId)
        {
            var tree = _repo.GetTreeById(treeId);
            tree.Research(nodeId);
        }
    }
}
