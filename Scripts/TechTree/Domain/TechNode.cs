using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.TechTree.Domain
{
    public class TechNode(string id,List<string> prerequisites, float cost)
    {
        public List<string> Prerequisites { get; set; } = prerequisites;

        public bool Researched {get; private set;} = false;

        public string Id { get; } = id;
        public float Cost { get; private set; } = cost;

        public List<IModifier> Modifiers { get; set; }

        public void Research(HashSet<string> unlockedId)
        {
            if (Prerequisites.All(c => unlockedId.Contains(c)))
            {
                Researched = true;
            }
        }

    }
}
