using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Domain
{
	public interface IMapGenerator
	{
		Map Generate(int width, int height, int seed);
	}
}
