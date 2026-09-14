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

		/// <summary>
		/// （v0.3 / WP-2.3）列出**半径内**的全部坐标（含中心格；radius=1 → 7 格，radius=0 → 仅中心）。
		/// <para>人口上限（营地「半径 1 格内总计 9 人」）、未来的范围效果（`WP-4.2`）与迷雾展开
		/// （`FogAppService.GetHexPositionsInRadius`）用的是同一套六边形展开规则 —— 这里把它提到领域层做单点定义，
		/// 避免各模块各写一份（旧实现散落在 `ConstructionAppService` 与 `FogAppService`，两处都已复制）。</para>
		/// </summary>
		public IEnumerable<HexCubePosition> InRadius(int radius)
		{
			if (radius < 0) yield break;

			for (int dq = -radius; dq <= radius; dq++)
			{
				int minDr = Math.Max(-radius, -dq - radius);
				int maxDr = Math.Min(radius, -dq + radius);
				for (int dr = minDr; dr <= maxDr; dr++)
					yield return new HexCubePosition(q + dq, r + dr);
			}
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
