using System;
using System.Collections.Generic;
using System.Numerics;

namespace SciencePotato.Scripts.Common.Domain
{
	public struct HexCubePosition(int q, int r)
	{
		// Require Fix (readonly)
		public int q { get; set; } = q;
		public int r { get; set; } = r;

		public int DistenceTo(HexCubePosition target)
		{
			return (Math.Abs(q - target.q) + Math.Abs(r - target.r) + Math.Abs(-q - r + target.q + target.r)) / 2;
		}

		public IEnumerable<HexCubePosition> GetNeighbor()
		{
			List<HexCubePosition> neighour = [];
			for (int i = -1; i < 2; i++)
				for (int j = -1; j < 2; j++)
					if (i != j)
						neighour.Add(new HexCubePosition(q + i, r + j));
			return neighour;
		}

		public HexCubePosition Translate(Vector2 factor)
		{
			return new HexCubePosition((int)factor.X, (int)factor.Y);
		}

		public (int, int) ToCoordinate()
		{
			return (q, r);
		}

		public override bool Equals(object obj)
		{
			return obj is HexCubePosition pos && pos.ToCoordinate() == ToCoordinate();
		}

		public override int GetHashCode() => HashCode.Combine(q, r);
		public static bool operator ==(HexCubePosition left, HexCubePosition right) => left.Equals(right);
		public static bool operator !=(HexCubePosition left, HexCubePosition right) => !left.Equals(right);
	}
}
