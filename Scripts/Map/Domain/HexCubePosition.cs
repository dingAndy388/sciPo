using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Domain
{
	public readonly struct HexCubePosition (int q, int e): IPosition
	{
		int q { get; } = q;
		int r { get; } = e;

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
	}
}
