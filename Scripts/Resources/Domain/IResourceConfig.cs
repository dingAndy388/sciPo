using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Resources.Domain
{
    public interface IResourceConfig
    {
        string Name { get; set; }
        string Description { get; set; }
        int GrowInteval { get; set; }
        float BaseGrowth { get; set; }
        float BaseValue { get; set; }
        float BaseLimit { get; set; }

        List<Modifier> DependentModifier { get; set; }
    }
}
