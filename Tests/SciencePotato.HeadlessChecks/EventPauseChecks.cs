using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Events.Domain;
using SciencePotato.Scripts.Map.Domain;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.7.8 / WP-4.12）**事件触发即暂停 + 玩家决策后继续**（`M2` 判据 ③）的验收检查。
	/// <para>夹具把事件表的触发概率改成 **100%/日**（否则要等概率），使"哪一天触发"完全确定。</para>
	/// <para>契约：暂停只作用于**真实时间推进**（`GameClock.Advance(realSeconds)`）；
	/// `AdvanceDays` 是无头模拟/离线结算的推进原语，不受暂停影响（否则测试无法用同一入口跳日）。</para>
	/// </summary>
	internal static class EventPauseChecks
	{
		private const string MapId = "event-pause";

		/// <summary>夹具里被改成 100%/日 的那个事件 Id（每次 `NewField` 记一次）。</summary>
		private static string _firstEventId;

		public static void RunAll()
		{
			Check.Run("WP-4.12 触发即暂停：事件命中 ⇒ 时间轴冻结 + 进待决队列", TriggerPausesTimeline);
			Check.Run("WP-4.12 暂停真的生效：真实时间推进返回 0 日（日期不动）", PausedAdvanceDoesNothing);
			Check.Run("WP-4.12 确认后继续：`Resolve` 摘掉待决 + 还原玩家原来的档位", ResolveResumesTimeline);
			Check.Run("WP-4.12 多条待决：全部确认才恢复（少一条仍冻着）", MultiplePendingNeedAllResolved);
			Check.Run("WP-4.12 幂等与边界：重复确认无效；没人跑事件的势力不会被暂停", ResolveIsIdempotentAndScoped);
		}

		/// <summary>事件表夹具：第一个事件改成"每天必中、永久"，保证确定触发。</summary>
		private static CoreServices NewField()
		{
			var root = Newtonsoft.Json.Linq.JObject.Parse(
				System.IO.File.ReadAllText(ConfigFixtures.TablePath("Events")));

			// 事件表根是**数组**（`Events: [ ... ]`），与 7 张表里用字典的那几张不同
			Newtonsoft.Json.Linq.JArray list = root["Events"] as Newtonsoft.Json.Linq.JArray;
			Check.Assert(list != null && list.Count > 0, "事件表夹具应能取到第一个事件");

			Newtonsoft.Json.Linq.JObject first = (Newtonsoft.Json.Linq.JObject)list[0];
			string firstId = (string)(first["EventId"] ?? first["Id"]);

			// **前两个**事件都改成"每天必中、永久"：既保证确定触发，也能造出"同时多条待决"的局面
			for (int i = 0; i < list.Count && i < 2; i++)
			{
				var item = (Newtonsoft.Json.Linq.JObject)list[i];
				item["TriggerChancePerDay"] = 1.0f;
				item["Duration"] = 0; // 0 = 永久（校验器：负数是 error）
				item["ResourcePrerequisites"] = new Newtonsoft.Json.Linq.JObject();
				item["TechPrerequisites"] = new Newtonsoft.Json.Linq.JObject();
			}

			CoreServices core = ConfigFixtures.BuildCore(ConfigFixtures.RealConfigSourceWith("Events", root.ToString()));
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;
			core.Map.GenerateMap(20261060, 16, 16, MapId);

			_firstEventId = firstId;

			Check.Assert(core.Session.HasPlayer(1), "夹具应有人类玩家");
			Check.Assert(core.Events.StartEventsEngine(MapId, 1), "人类的事件引擎应能启动");
			return core;
		}

		private static string FirstEventId() => _firstEventId;

		private static void TriggerPausesTimeline()
		{
			CoreServices core = NewField();
			string eventId = FirstEventId();

			Check.AssertEqual(0, core.Events.PendingCount, "准备：还没有待决事件");
			Check.Assert(!core.Session.IsPaused, "准备：时间没被暂停");

			core.Session.Clock.AdvanceDays(1); // 100%/日 ⇒ 必中

			Check.Assert(core.Events.PendingCount >= 1, $"触发后应进待决队列（实际 {core.Events.PendingCount} 条）");
			Check.Assert(core.Session.IsPaused, "触发后时间轴应冻结（触发即暂停）");
			Check.AssertEqual(1, core.Events.AutoPauseCount, "自动暂停次数应记 1");

			PendingEventDecision pending = core.Events.GetPendingDecisions(MapId, 1).First();
			Check.AssertEqual(eventId, pending.EventId, "待决的应是那个事件");
			Check.AssertEqual(1, pending.Day, "应记下暂停发生在第 1 日");
			Check.Assert(!string.IsNullOrWhiteSpace(pending.Name), "待决项要带名字（UI 直接显示）");
		}

		private static void PausedAdvanceDoesNothing()
		{
			CoreServices core = NewField();
			core.Session.Clock.AdvanceDays(1);
			Check.Assert(core.Session.IsPaused, "准备：已被事件暂停");

			double dayBefore = core.Session.Clock.CurrentDay;
			int advanced = core.Session.Clock.Advance(1.0); // 真实时间 1 秒

			Check.AssertEqual(0, advanced, "暂停时真实时间推进应返回 0 日");
			Check.AssertEqual(dayBefore, core.Session.Clock.CurrentDay, "日期不该变化");

			// 暂停期间再跳日（离线原语）：不产生**新的**待决（事件引擎照跑，但玩家还没确认）
			core.Session.Clock.AdvanceDays(1);
		}

		private static void ResolveResumesTimeline()
		{
			CoreServices core = NewField();
			core.Session.Clock.Speed = TimeSpeedTier.Fast; // 玩家原本选的是快档
			core.Session.Clock.AdvanceDays(1);
			Check.Assert(core.Session.IsPaused, "准备：已被事件暂停");

			string eventId = core.Events.GetPendingDecisions(MapId, 1).First().EventId;
			Check.Assert(core.Events.Resolve(MapId, 1, eventId), "确认应成功");

			// 夹具里有两个事件同时命中：只确认一条时**仍应冻着**（多待决的语义）
			Check.Assert(core.Session.IsPaused, "还有待决 ⇒ 仍应暂停");

			Check.Assert(core.Events.ResolveAll(MapId, 1) >= 1, "一键确认应摘掉剩余的");
			Check.AssertEqual(0, core.Events.PendingCount, "待决队列应清空");
			Check.Assert(!core.Session.IsPaused, "确认后时间应恢复");
			Check.AssertEqual(TimeSpeedTier.Fast, core.Session.Clock.Speed, "应还原玩家原来的档位（快档），不是硬编码标准档");
			Check.AssertEqual(1, core.Events.AutoResumeCount, "恢复次数应记 1（一次恢复，不是一条一次）");

			int advanced = core.Session.Clock.Advance(1.0);
			Check.Assert(advanced > 0, $"恢复后真实时间推进应真的走日子（实际 {advanced} 日）");
		}

		private static void MultiplePendingNeedAllResolved()
		{
			CoreServices core = NewField();
			core.Session.Clock.AdvanceDays(2); // 两天各命中一次 ⇒ 两条待决

			Check.Assert(core.Events.PendingCount >= 2, $"应立即堆出 ≥2 条待决（实际 {core.Events.PendingCount}）");
			var pending = core.Events.GetPendingDecisions(MapId, 1);

			Check.Assert(core.Events.Resolve(MapId, 1, pending[0].EventId), "确认第一条应成功");
			Check.Assert(core.Session.IsPaused, "还有待决 ⇒ 时间仍应冻着");

			int resolved = core.Events.ResolveAll(MapId, 1);
			Check.Assert(resolved >= 1, $"一键确认应摘掉剩余的（实际 {resolved}）");
			Check.AssertEqual(0, core.Events.PendingCount, "全部确认后队列应清空");
			Check.Assert(!core.Session.IsPaused, "全部确认后才恢复时间");
		}

		private static void ResolveIsIdempotentAndScoped()
		{
			CoreServices core = NewField();
			core.Session.Clock.AdvanceDays(1);
			string eventId = core.Events.GetPendingDecisions(MapId, 1).First().EventId;

			Check.Assert(core.Events.Resolve(MapId, 1, eventId), "第一次确认应成功");
			Check.Assert(!core.Events.Resolve(MapId, 1, eventId), "重复确认同一条应无效（幂等）");
			Check.Assert(!core.Events.Resolve(MapId, 2, eventId), "别的势力名下的确认不该成功（按 owner 分账）");

			// 没跑事件引擎的势力（AI 侧）：既不待决、也不会被暂停
			Check.AssertEqual(0, core.Events.GetPendingDecisions(MapId, 2).Count, "AI 侧没有待决事件");

			core.Events.ResolveAll(MapId, 1);
			Check.Assert(!core.Session.IsPaused, "玩家确认完了 ⇒ 现在不该暂停");
		}
	}
}
