using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.IO;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>（v0.3 / WP-1.3）组合根的验收检查：装配是否成功、配置缺失是否快速失败。</summary>
	internal static class CoreBootstrapChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-1.3 CoreBootstrap：装配后可生成地图且常驻内存", WiresMapService);
			Check.Run("WP-1.3 CoreBootstrap：地形配置缺失时快速失败", FailsFastWithoutTerrains);
			Check.Run("WP-1.3 CoreBootstrap：会话持有的时钟可推进（日边界可用）", SessionClockAdvances);
		}

		private static CoreServices BuildCore(InMemoryConfigSource configSource)
		{
			var repo = new InMemoryMapRepository();
			return CoreBootstrap.Build(new CoreDependencies
			{
				FileSystem = new InMemoryFileSystem(),
				ConfigSource = configSource,
				Random = new SystemRandom(1234),
				ResourceConfigLoader = null, // 无头环境：生成器回退默认配置
				MapRepositoryFactory = _ => repo,
				SessionId = "test",
			});
		}

		private static InMemoryConfigSource RealTerrainConfig()
		{
			string path = Path.Combine(Check.FindRepoRoot(), "Config", "Terrains.json");
			var source = new InMemoryConfigSource();
			source.Inject("Terrains", File.ReadAllText(path));
			return source;
		}

		private static void WiresMapService()
		{
			CoreServices core = BuildCore(RealTerrainConfig());

			Check.Assert(core.Map != null, "应装配出 MapAppService");
			Check.Assert(core.Session != null, "应装配出 GameSession");
			Check.AssertEqual("test", core.Session.SessionId, "会话 Id");
			Check.AssertEqual(TimeSpeedTier.Standard, core.Session.Clock.Speed, "默认流速档位");

			core.Map.GenerateMap(20260914, 6, 6, "boot-map");

			Check.Assert(core.Session.Maps.IsLoaded("boot-map"), "生成的地图应常驻会话缓存");

			int cells = 0;
			foreach (MapCell _ in core.Map.GetAllCells("boot-map")) cells++;
			Check.AssertEqual(36, cells, "地图格数");

			MapCell first = core.Map.GetMapCell("boot-map", new HexCubePosition(0, 0));
			Check.Assert(first != null && first.Terrain != null, "每格都应有地形（生成器已填充）");
		}

		private static void FailsFastWithoutTerrains()
		{
			bool threw = false;
			string message = null;
			try
			{
				BuildCore(new InMemoryConfigSource());
			}
			catch (InvalidOperationException ex)
			{
				threw = true;
				message = ex.Message;
			}

			Check.Assert(threw, "缺少地形配置表时应快速失败");
			Check.Assert(message != null && message.Contains("Terrains"), "错误信息应指明缺失的配置表");
		}

		private static void SessionClockAdvances()
		{
			CoreServices core = BuildCore(RealTerrainConfig());
			int days = 0;
			core.Session.Clock.DayElapsed += _ => days++;

			core.Session.Advance(30.0); // 标准档 = 30 游戏日

			Check.AssertEqual(30, days, "会话时钟派发日数");
			Check.AssertEqual("0年2月1日", core.Session.Clock.Format(), "会话时钟日期");

			core.Session.IsPaused = true;
			Check.AssertEqual(0, core.Session.Advance(60.0), "暂停后不应推进");
			core.Session.IsPaused = false;
		}
	}
}
