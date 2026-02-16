using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Domain
{
	public interface IPosition
	{
		public int DistenceTo(IPosition target);
		public IPosition Translate(Vector2 factor);
		public IEnumerable<IPosition> GetNeighbor();
	}
}
