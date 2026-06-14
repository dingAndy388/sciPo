using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.TechTree.Domain
{
    public class TechTree(string id)
    {
        private string _id = id;
        private Dictionary<string, TechNode> _nodes;
        private HashSet<string> _researchedId;

        public string GetTreeId()
        {
            return _id;
        }

        public bool IsReasearched(string id)
        {
            if (!_nodes.ContainsKey(id))
                return false;
            return _nodes[id].Researched;
        }

        public void Research(string id)
        {
            if (_nodes.ContainsKey(id))
            {
                _nodes[id].Research(_researchedId);
                _researchedId.Add(_nodes[id].Id);
            }
        }

        public float GetCost(string id)
        {
            if (!_nodes.ContainsKey(id))
                return 0;
            return _nodes[id].Cost;
		}

		public float GetDuration(string nodeId)
		{
			if (!_nodes.ContainsKey(nodeId))
				return 0;
			return _nodes[nodeId].Duration;
		}
	}
}
