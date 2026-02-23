using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Domain
{
	public struct HexCubePosition (int q, int r): IPosition
	{
		// Require Fix (readonly)
		public int q { get; set; } = q;
		public int r { get; set; } = r;

		public int DistenceTo(IPosition target)
		{
			if (target is HexCubePosition)
			{
				HexCubePosition pos = (HexCubePosition)target;
				return (Math.Abs(q-pos.q)+Math.Abs(r-pos.r)+Math.Abs(-q-r +pos.q+pos.r))/2;
			}
			else
			{
				return -1;
			}
		}

		public IEnumerable<IPosition> GetNeighbor()
		{
			List<IPosition> neighour = [];
			for (int i = -1; i < 2; i++)
				for (int j = -1; j < 2; j++)
					if(i!=j)
						neighour.Add(new HexCubePosition(q + i, r + j));
			return neighour;
		}

		public IPosition Translate(Vector2 factor)
		{
			return new HexCubePosition((int)factor.X, (int)factor.Y);
		}

		public (int,int) ToCoordinate()
		{
			return (q,r);
		}
	}
}
