using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Fog.Application;
using SciencePotato.Scripts.Fog.Domain;
using SciencePotato.Scripts.Fog.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.9.7 / `WP-5.5`）**迷雾性能**（`H1`、`FOG-03`/`FOG-04`）的验收检查。
	/// <list type="bullet">
	/// <item>**半径模板缓存**：揭图不再每次现算坐标（构建次数与调用次数解耦，且结果与逐格展开一致）；</item>
	/// <item>**增量存档**：脏格数 = 真正变过的格子数，没变时整次跳过写盘；</item>
	/// <item>**紧凑存档**：`q/r` zigzag varint + Base64 ⇒ 文本体积显著小于旧 `"q,r": v` 口径；</item>
	/// <item>**语义不许退化**：永久清除（`Unexplored` 不回退）与老存档兼容两件旧承诺仍然成立。</item>
	/// </list>
	/// </summary>
	internal static class FogPerfChecks
	{
		private const string MapId = "fog-perf";

		public static void RunAll()
		{
			Check.Run("WP-5.5 半径模板：反复揭图不再重算（构建次数与调用次数解耦）", RadiusTemplateIsCached);
			Check.Run("WP-5.5 模板正确性：缓存模板与逐格展开一致（半径 0~6 全覆盖）", TemplateMatchesBruteForce);
			Check.Run("WP-5.5 增量存档：脏格数 = 新可见格数；无变化时整次跳过写盘", IncrementalSaveCountsDirtyCells);
			Check.Run("WP-5.5 紧凑存档：体积显著小于旧口径，且编解码往返一致", CompactSaveIsSmallerAndRoundTrips);
			Check.Run("WP-5.5 老存档兼容：旧 `MatrixData` 仍能读回（不破档）", LegacyFormatStillLoads);
			Check.Run("WP-5.5 永久清除语义：离开视野降级为 Fogged，且永不回退 Unexplored", FogNeverGoesBackToUnexplored);
		}

		private static void RadiusTemplateIsCached()
		{
			// 预热本例用到的半径（揭图 = Disc(radius) + Ring(radius+1)），再记录基线
			_ = FogGeometry.DiscOffsets(3);
			_ = FogGeometry.RingOffsets(4);

			int buildsBefore = FogGeometry.TemplateBuildCount;
			int cachedBefore = FogGeometry.CachedRadiusCount;

			(FogAppService fog, _, string dir) = NewFog();
			try
			{
				for (int i = 0; i < 200; i++)
					fog.RevealArea(new HexCubePosition(i % 20, i % 20), 3);

				Check.AssertEqual(buildsBefore, FogGeometry.TemplateBuildCount, "200 次揭图不应再构建任何模板（缓存命中）");
				Check.AssertEqual(cachedBefore, FogGeometry.CachedRadiusCount, "半径缓存数量不应增长");
			}
			finally { Cleanup(dir); }
		}

		private static void TemplateMatchesBruteForce()
		{
			for (int radius = 0; radius <= 6; radius++)
			{
				IReadOnlyList<(int Dq, int Dr)> disc = FogGeometry.DiscOffsets(radius);
				Check.AssertEqual(radius == 0 ? 0 : 3 * radius * (radius + 1) + 1, disc.Count, $"半径 {radius} 的格数");
				Check.Assert(disc.Distinct().Count() == disc.Count, $"半径 {radius} 的模板不应有重复偏移");

				foreach ((int dq, int dr) in disc)
					Check.Assert((Math.Abs(dq) + Math.Abs(dr) + Math.Abs(-dq - dr)) / 2 <= radius, $"半径 {radius} 内含越界偏移 ({dq},{dr})");

				if (radius >= 1)
				{
					IReadOnlyList<(int Dq, int Dr)> ring = FogGeometry.RingOffsets(radius);
					Check.AssertEqual(6 * radius, ring.Count, $"半径 {radius} 的环格数");
					foreach ((int dq, int dr) in ring)
						Check.AssertEqual(radius, (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(-dq - dr)) / 2, $"环上的偏移应在距离 {radius}");
				}
			}
		}

		private static void IncrementalSaveCountsDirtyCells()
		{
			(FogAppService fog, _, string dir) = NewFog();
			try
			{
				var center = new HexCubePosition(10, 10);
				int expected = 0;
				for (int dq = -3; dq <= 3; dq++)
					for (int dr = Math.Max(-3, -dq - 3); dr <= Math.Min(3, -dq + 3); dr++) expected++;
				for (int dq = -4; dq <= 4; dq++)
					for (int dr = Math.Max(-4, -dq - 4); dr <= Math.Min(4, -dq + 4); dr++)
						if ((Math.Abs(dq) + Math.Abs(dr) + Math.Abs(-dq - dr)) / 2 == 4) expected++;

				fog.RevealArea(center, 3);
				fog.Save(MapId);
				Check.AssertEqual(expected, fog.LastSavedDirtyCells, $"半径 3 揭图后脏格数应 = 内圈 + 外环（{expected}）");
				Check.Assert(!fog.LastSaveSkippedWrite, "首次存档必须写");
				Check.AssertEqual(expected, fog.LastSavedTotalCells, "矩阵里应正好这么多格");

				fog.Save(MapId);
				Check.AssertEqual(0, fog.LastSavedDirtyCells, "矩阵没变 ⇒ 脏格数 0");
				Check.Assert(fog.LastSaveSkippedWrite, "矩阵没变 ⇒ 跳过写盘");

				fog.RevealArea(center, 3);
				fog.Save(MapId);
				Check.AssertEqual(0, fog.LastSavedDirtyCells, "重复揭同一片区域不改变矩阵值 ⇒ 仍是 0 脏格");
			}
			finally { Cleanup(dir); }
		}

		private static void CompactSaveIsSmallerAndRoundTrips()
		{
			(FogAppService fog, FogRepository repo, string dir) = NewFog();
			try
			{
				fog.RevealArea(new HexCubePosition(50, 50), 12); // 469 + 78 格：足够看出体积差异
				fog.Save(MapId);

				FogSaveData saved = repo.LoadFog(MapId, 1);
				Check.Assert(!string.IsNullOrEmpty(saved.Compact), "新档必须写紧凑格式");
				Check.AssertEqual(FogCodec.Version, saved.Encoding, "编码版本标记");
				Check.AssertEqual(0, saved.MatrixData.Count, "新档不应再写旧的文本矩阵");

				int legacyEstimate = fog.LastSavedTotalCells * 12; // 旧口径 `"q,r": v, ` 每格约 12 字节
				Check.Assert(fog.LastSavedBytes * 2 < legacyEstimate,
					$"紧凑文本 {fog.LastSavedBytes} 字节应显著小于旧口径估算 {legacyEstimate} 字节");

				List<KeyValuePair<HexCubePosition, byte>> decoded = FogCodec.Decode(saved.Compact);
				Check.AssertEqual(fog.LastSavedTotalCells, decoded.Count, "解码后的格数应与矩阵一致");
				Check.Assert(decoded.Any(c => c.Key.q == 50 && c.Key.r == 50 && c.Value == FogAppService.Visible), "中心格应在紧凑存档里");
			}
			finally { Cleanup(dir); }
		}

		private static void LegacyFormatStillLoads()
		{
			(FogAppService fog, FogRepository repo, string dir) = NewFog();
			try
			{
				var legacy = new FogSaveData { OwnerId = 1 };
				legacy.MatrixData["3,4"] = FogAppService.Visible;
				legacy.MatrixData["5,6"] = FogAppService.Fogged;
				repo.SaveFog(MapId, 1, legacy);

				fog.Load(MapId, 1);
				Check.AssertEqual(FogAppService.Visible, fog.GetVisibility(1, new HexCubePosition(3, 4)), "旧格式的可见格应读回");
				Check.AssertEqual(FogAppService.Fogged, fog.GetVisibility(1, new HexCubePosition(5, 6)), "旧格式的迷雾格应读回");
				Check.AssertEqual(FogAppService.Unexplored, fog.GetVisibility(1, new HexCubePosition(9, 9)), "未探索格仍是未探索");
			}
			finally { Cleanup(dir); }
		}

		private static void FogNeverGoesBackToUnexplored()
		{
			(FogAppService fog, _, string dir) = NewFog();
			try
			{
				var center = new HexCubePosition(4, 4);
				fog.RevealArea(center, 2);
				Check.AssertEqual(FogAppService.Visible, fog.GetVisibility(center), "揭图后应可见");

				fog.ResetArea(center, 2);
				Check.AssertEqual(FogAppService.Fogged, fog.GetVisibility(center), "收起视野应降级为迷雾（而不是未探索）");
				Check.Assert(fog.GetVisibility(center) != FogAppService.Unexplored, "永久清除：永不回退到未探索");

				fog.RevealArea(center, 2);
				Check.AssertEqual(FogAppService.Visible, fog.GetVisibility(center), "再次揭图应恢复可见");
			}
			finally { Cleanup(dir); }
		}

		// ────────────────────────── 夹具 ──────────────────────────

		private static (FogAppService Fog, FogRepository Repo, string Dir) NewFog(int ownerId = 1)
		{
			string dir = Path.Combine(Path.GetTempPath(), "sp-wp55-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			var repo = new FogRepository(Path.Combine(dir, "fog_"));
			return (new FogAppService(ownerId, repo), repo, dir);
		}

		private static void Cleanup(string dir)
		{
			try
			{
				if (Directory.Exists(dir)) Directory.Delete(dir, true);
			}
			catch (IOException)
			{
				// 临时目录清理失败不影响验收结果
			}
		}
	}
}
