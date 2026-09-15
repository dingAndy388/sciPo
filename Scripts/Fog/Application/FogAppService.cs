using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Fog.Domain;
using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Fog.Application
{
	/// <summary>
	/// （v0.3 / `WP-3.2`；v0.8.9 / `WP-4.10` 改成**按 owner**）迷雾服务：每个势力一份"矩阵 + 视野计数"。
	/// <list type="number">
	/// <item>**永久清除语义**（设计稿）：`Visible`（当前可见）与 `Fogged`（探索过但当前不可见）分开存，
	/// 离开视野只降级到 `Fogged`、**永不回退到 `Unexplored`**；</item>
	/// <item>**视野计数**：`RevealArea` 给每个格子 +1、`ResetArea` −1，计数归零才降级 —— 两个单位重叠视野时
	/// 一个走开不会把另一个照亮的格子熄掉（`F1` 类问题的根因）；</item>
	/// <item>**按 owner 隔离**：`_states[ownerId]`，旧的无 owner 签名（`RevealArea(center,r)` 等）
	/// 一律指向构造时传入的 `_ownerId`（人类玩家）⇒ 既有 80+ 处调用与行为完全不变。</item>
	/// </list>
	/// </summary>
	public class FogAppService
	{
		public const byte Unexplored = 0;
		public const byte Fogged = 1;
		public const byte Visible = 2;

		/// <summary>（v0.8.9 / `WP-4.10`）单个势力的迷雾状态。</summary>
		private sealed class FogState
		{
			public readonly Dictionary<HexCubePosition, byte> Matrix = new();
			public readonly Dictionary<HexCubePosition, short> VisionCount = new();
		}

		private readonly int _ownerId;
		private readonly IFogRepository _repo;

		private readonly Dictionary<int, FogState> _states = new();

		private FogState State(int ownerId)
		{
			if (!_states.TryGetValue(ownerId, out FogState state))
			{
				state = new FogState();
				_states[ownerId] = state;
			}
			return state;
		}

		/// <summary>缺省 owner（人类玩家）的迷雾矩阵 —— 兼容层：旧调用方与单势力场景继续用它。</summary>
		private Dictionary<HexCubePosition, byte> _fogMatrix => State(_ownerId).Matrix;

		private Dictionary<HexCubePosition, short> _visionCount => State(_ownerId).VisionCount;

		/// <summary>（v0.8.9 / `WP-4.10`）已装载过迷雾状态的势力 Id（冒烟/验收用）。</summary>
		public IReadOnlyCollection<int> LoadedOwners => _states.Keys;

		public FogAppService(int ownerId, IFogRepository repo)
		{
			_ownerId = ownerId;
			_repo = repo;
		}

		public void Clear(string mapId) => Clear(mapId, _ownerId);

		/// <summary>（v0.8.9 / `WP-4.10`）清掉**指定势力**的迷雾并落盘。</summary>
		public void Clear(string mapId, int ownerId)
		{
			FogState state = State(ownerId);
			state.Matrix.Clear();
			state.VisionCount.Clear();
			Save(mapId, ownerId);
		}

		public void Load(string mapId) => Load(mapId, _ownerId);

		/// <summary>（v0.8.9 / `WP-4.10`）装载**指定势力**的迷雾（存档里每个 owner 一份分区）。</summary>
		public void Load(string mapId, int ownerId)
		{
			var data = _repo.LoadFog(mapId, ownerId);
			FogState state = State(ownerId);
			state.Matrix.Clear();
			state.VisionCount.Clear();

			if (data?.MatrixData == null) return;

			foreach (var kvp in data.MatrixData)
			{
				var parts = kvp.Key.Split(',');
				if (parts.Length == 2 && int.TryParse(parts[0], out int q) && int.TryParse(parts[1], out int r))
				{
					state.Matrix[new HexCubePosition(q, r)] = kvp.Value;
				}
			}
		}

		public byte GetVisibility(HexCubePosition pos) => GetVisibility(_ownerId, pos);

		/// <summary>（v0.8.9 / `WP-4.10`）**该势力**看到的是可见 / 迷雾 / 未探索（AI 与玩家各看各的）。</summary>
		public byte GetVisibility(int ownerId, HexCubePosition pos)
			=> State(ownerId).Matrix.GetValueOrDefault(pos, Unexplored);

		public void RevealArea(HexCubePosition center, int radius) => RevealArea(_ownerId, center, radius);

		/// <summary>（v0.8.9 / `WP-4.10`）**该势力**揭开一片区域（单位/建筑移动、建造完成时按 owner 调用）。</summary>
		public void RevealArea(int ownerId, HexCubePosition center, int radius)
			=> RevealInto(State(ownerId), center, radius);

		public void ResetArea(HexCubePosition center, int radius) => ResetArea(_ownerId, center, radius);

		/// <summary>（v0.8.9 / `WP-4.10`）**该势力**收起一片区域（离开视野 ⇒ 降级到迷雾，不回退到未探索）。</summary>
		public void ResetArea(int ownerId, HexCubePosition center, int radius)
			=> ResetInto(State(ownerId), center, radius);

		private void RevealInto(FogState state, HexCubePosition center, int radius)
		{
			if (radius <= 0) return;

			foreach (var pos in GetHexPositionsInRadius(center, radius))
			{
				byte oldValue = state.Matrix.GetValueOrDefault(pos, Unexplored);
				if (oldValue < Visible)
					state.Matrix[pos] = Visible;

				short count = state.VisionCount.GetValueOrDefault(pos, (short)0);
				state.VisionCount[pos] = (short)(count + 1);
			}

			// Outer fogged ring: positions exactly at radius+1
			foreach (var pos in GetHexRing(center, radius + 1))
			{
				if (state.Matrix.GetValueOrDefault(pos, Unexplored) == Unexplored)
					state.Matrix[pos] = Fogged;
			}
		}

		private void ResetInto(FogState state, HexCubePosition center, int radius)
		{
			if (radius <= 0) return;

			foreach (var pos in GetHexPositionsInRadius(center, radius))
			{
				short count = state.VisionCount.GetValueOrDefault(pos, (short)0);
				if (count <= 0) continue;

				count--;
				state.VisionCount[pos] = count;

				if (count <= 0)
				{
					state.VisionCount.Remove(pos);
					if (state.Matrix.GetValueOrDefault(pos, Unexplored) == Visible)
						state.Matrix[pos] = Fogged;
				}
			}
		}

		public void Save(string mapId) => Save(mapId, _ownerId);

		/// <summary>（v0.8.9 / `WP-4.10`）落盘**指定势力**的迷雾（键 = `fog:{mapId}_{ownerId}`）。</summary>
		public void Save(string mapId, int ownerId)
		{
			FogState state = State(ownerId);
			var data = new FogSaveData
			{
				OwnerId = ownerId,
				MatrixData = new Dictionary<string, byte>()
			};

			foreach (var kvp in state.Matrix)
			{
				var (q, r) = kvp.Key.ToCoordinate();
				data.MatrixData[$"{q},{r}"] = kvp.Value;
			}

			_repo.SaveFog(mapId, ownerId, data);
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