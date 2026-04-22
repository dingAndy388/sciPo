using System.Collections.Generic;
using System.Numerics;

namespace SciencePotato.Scripts.Map.Domain
{
	public interface IPosition
	{
		public int DistenceTo(IPosition target);
		public IPosition Translate(Vector2 factor);
		public IEnumerable<IPosition> GetNeighbor();
		public (int, int) ToCoordinate();
	}
}
