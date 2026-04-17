using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Infrastructure
{
    public class HexCubeCellSave
    {
        public HexCubePosition position { get; set; } 
        public string terrain { get; set; }
    }
}
