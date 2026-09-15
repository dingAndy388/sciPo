using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Fog.Domain
{
	/// <summary>
	/// （v0.9.7 / `WP-5.5`）**六边形半径模板**（`H1`）：把"半径内的偏移量 / 半径环的偏移量"算一次并缓存，
	/// 之后每次揭图只做"中心 + 偏移"的加法。
	/// <para>为什么值得做：`RevealArea` 在每个单位每次移动/每次日节拍都会被调用（冒烟实测：一次开局揭图调用数百次），
	/// 而旧实现每次都从 `dq/dr` 双重循环里现算一遍数组（半径 10 ⇒ 每次 331 个格子的坐标运算 + Math.Abs）。
	/// 缓存后模板构建次数与"调用次数"解耦（用例直接断言 <see cref="TemplateBuildCount"/> 不随调用增长）。</para>
	/// </summary>
	public static class FogGeometry
	{
		private static readonly Dictionary<int, List<(int Dq, int Dr)>> _disc = new();
		private static readonly Dictionary<int, List<(int Dq, int Dr)>> _ring = new();
		private static readonly object _gate = new();

		/// <summary>模板真正被构建的次数（用例用它证明"复用了模板"，而不是每次重算）。</summary>
		public static int TemplateBuildCount { get; private set; }

		/// <summary>已缓存的半径个数。</summary>
		public static int CachedRadiusCount
		{
			get { lock (_gate) return _disc.Count; }
		}

		/// <summary>半径内的偏移量（不含中心；中心由调用方自己加，省一次分配）。</summary>
		public static IReadOnlyList<(int Dq, int Dr)> DiscOffsets(int radius)
		{
			if (radius <= 0) return System.Array.Empty<(int, int)>();

			lock (_gate)
			{
				if (_disc.TryGetValue(radius, out List<(int Dq, int Dr)> cached)) return cached;

				var offsets = new List<(int Dq, int Dr)>((2 * radius + 1) * (2 * radius + 1));
				offsets.Add((0, 0));
				for (int dq = -radius; dq <= radius; dq++)
				{
					int minDr = System.Math.Max(-radius, -dq - radius);
					int maxDr = System.Math.Min(radius, -dq + radius);
					for (int dr = minDr; dr <= maxDr; dr++)
					{
						if (dq == 0 && dr == 0) continue;
						offsets.Add((dq, dr));
					}
				}

				_disc[radius] = offsets;
				TemplateBuildCount++;
				return offsets;
			}
		}

		/// <summary>半径**环**上的偏移量（`radius <= 0` 返回空）。</summary>
		public static IReadOnlyList<(int Dq, int Dr)> RingOffsets(int radius)
		{
			if (radius <= 0) return System.Array.Empty<(int, int)>();

			lock (_gate)
			{
				if (_ring.TryGetValue(radius, out List<(int Dq, int Dr)> cached)) return cached;

				var offsets = new List<(int Dq, int Dr)>(radius * 6);
				for (int dq = -radius; dq <= radius; dq++)
				{
					int minDr = System.Math.Max(-radius, -dq - radius);
					int maxDr = System.Math.Min(radius, -dq + radius);
					for (int dr = minDr; dr <= maxDr; dr++)
					{
						int dist = (System.Math.Abs(dq) + System.Math.Abs(dr) + System.Math.Abs(-dq - dr)) / 2;
						if (dist == radius) offsets.Add((dq, dr));
					}
				}

				_ring[radius] = offsets;
				TemplateBuildCount++;
				return offsets;
			}
		}
	}
}
