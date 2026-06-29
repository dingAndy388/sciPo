using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.TechTree.Domain
{
	public interface ITechTreeConfig
	{
		Dictionary<string,ITechNodeConfig> Techs { get; }
	}
}
