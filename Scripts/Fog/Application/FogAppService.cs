using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Fog.Domain;
using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Fog.Application
{
	public class FogAppService
	{
		public const byte Unexplored = 0;
		public const byte Fogged = 1;
		public const byte Visible = 2;

		private readonly int _ownerId;
		private readonly IFogRepository _repo;

		private Dictionary<HexCubePosition, byte> _fogMatrix = new();
		private Dictionary<HexCubePosition, short> _visionCount = new();

		public FogAppService(int ownerId, IFogRepository repo)
		{
			_ownerId = ownerId;
			_repo = repo;
		}

		public void Clear(string mapId)
		{
			_fogMatrix.Clear();
			_visionCount.Clear();
			Save(mapId);
		}

		public void Load(string mapId)
		{
			var data = _repo.LoadFog(mapId, _ownerId);
			_fogMatrix.Clear();
			_visionCount.Clear();

			if (data?.MatrixData == null) return;

			foreach (var kvp in data.MatrixData)
			{
				var parts = kvp.Key.Split(',');
				if (parts.Length == 2 && int.TryParse(parts[0], out int q) && int.TryParse(parts[1], out int r))
				{
					_fogMatrix[new HexCubePosition(q, r)] = kvp.Value;
				}
			}
		}

		public byte GetVisibility(HexCubePosition pos)
		{
			return _fogMatrix.GetValueOrDefault(pos, Unexplored);
		}

		public void RevealArea(HexCubePosition center, int radius)
		{
			if (radius <= 0) return;

			foreach (var pos in GetHexPositionsInRadius(center, radius))
			{
				byte oldValue = _fogMatrix.GetValueOrDefault(pos, Unexplored);
				if (oldValue < Visible)
					_fogMatrix[pos] = Visible;

				short count = _visionCount.GetValueOrDefault(pos, (short)0);
				_visionCount[pos] = (short)(count + 1);
			}

			// Outer fogged ring: positions exactly at radius+1
			foreach (var pos in GetHexRing(center, radius + 1))
			{
				if (_fogMatrix.GetValueOrDefault(pos, Unexplored) == Unexplored)
					_fogMatrix[pos] = Fogged;
			}
		}

		public void ResetArea(HexCubePosition center, int radius)
		{
			if (radius <= 0) return;

			foreach (var pos in GetHexPositionsInRadius(center, radius))
			{
				short count = _visionCount.GetValueOrDefault(pos, (short)0);
				if (count <= 0) continue;

				count--;
				_visionCount[pos] = count;

				if (count <= 0)
				{
					_visionCount.Remove(pos);
					if (_fogMatrix.GetValueOrDefault(pos, Unexplored) == Visible)
						_fogMatrix[pos] = Fogged;
				}
			}
		}

		public void Save(string mapId)
		{
			var data = new FogSaveData
			{
				OwnerId = _ownerId,
				MatrixData = new Dictionary<string, byte>()
			};

			foreach (var kvp in _fogMatrix)
			{
				var (q, r) = kvp.Key.ToCoordinate();
				data.MatrixData[$"{q},{r}"] = kvp.Value;
			}

			_repo.SaveFog(mapId, _ownerId, data);
		}

		private IEnumerable<HexCubePosition> GetHexPositionsInRadius(HexCubePosition center, int radius)
		{
			for (int dq = -radius; dq <= radius; dq++)
			{
				int minDr = Math.Max(-radius, -dq - radius);
				int maxDr = Math.Min(radius, -dq + radius);
				for (int dr = minDr; dr <= maxDr; dr++)
					yield return new HexCubePosition(center.q + dq, center.r + dr);
			}
		}

		private IEnumerable<HexCubePosition> GetHexRing(HexCubePosition center, int radius)
		{
			if (radius <= 0) yield break;

			for (int dq = -radius; dq <= radius; dq++)
			{
				int minDr = Math.Max(-radius, -dq - radius);
				int maxDr = Math.Min(radius, -dq + radius);
				for (int dr = minDr; dr <= maxDr; dr++)
				{
					int dist = (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(-dq - dr)) / 2;
					if (dist == radius)
						yield return new HexCubePosition(center.q + dq, center.r + dr);
				}
			}
		}
	}
}