using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.TechTree.Domain
{
    public interface ITechTreeRepository
    {
        public TechTree GetTreeById(string mapId, string id);


        public void SaveTree(string mapId, string id, TechTree tree);

    }
}
