using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Domain
{
	public interface IMapGeneratorConfig
	{
		double Density { get; set; }
	}
}
