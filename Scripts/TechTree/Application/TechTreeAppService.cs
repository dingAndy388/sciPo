using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Resources.Application;
using SciencePotato.Scripts.TechTree.Domain;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.TechTree.Application
{
    public class TechTreeAppService(ITechTreeRepository repo, ResourcesAppService resourceAppService)
    {

        private ITechTreeRepository _repo = repo;
		private ResourcesAppService _resource = resourceAppService;

		public TechRequirement GetTechTreeRequirement(string mapId, string treeId, List<string> requirements)
        {
            var tree = _repo.GetTreeById(mapId, treeId);
            return new TechRequirement(tree, requirements);
        }

        public void Research(string mapId, string treeId, string nodeId)
        {
            var tree = _repo.GetTreeById(mapId, treeId);

			Consumption consumption = new("Idea",tree.GetCost(nodeId));

            var contract = _resource.CreateResourceConsumption(consumption);

            if(contract.IsConsumable())
            {
                contract.Consume(); 
				LinearTask task = new LinearTask(0,tree.GetDuration(nodeId),nodeId,"Research",false,0);
                task.OnCompleted += () =>
                {
                    tree.Research(nodeId);
                };
			}
        }
    }
}
