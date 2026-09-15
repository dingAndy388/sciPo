using SciencePotato.Scripts.Core.Save;
using SciencePotato.Scripts.Map.Application;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.9.6 / `WP-5.11`）**会话入口**：新开局 / 存档 / 读档 / 退出 + 自动存档点 —— 宿主（Godot 层与冒烟）
	/// 只需要这一个对象，不必自己拼 <c>GenerateMap</c> + <c>StartMap</c> + <c>WorldSave</c> 的调用顺序。
	/// <list type="bullet">
	/// <item>**新开局**：生成地图 → 交给 <see cref="SessionOrchestrator.StartMap"/>（出生点/资源池/月结/事件/AI 全由它登记，`D80` 幂等）；</item>
	/// <item>**读档**：<see cref="WorldSaveService.LoadWorld(string, IReadOnlyList{int})"/> **按玩家表逐 owner 恢复**
	/// （`U7`：旧口径只恢复一个 owner ⇒ 多 AI 局读档后 AI 的周期任务全丢）；</item>
	/// <item>**自动存档点**：每 <see cref="AutoSaveIntervalDays"/> 游戏日一次（宿主推进时间后调 <see cref="AutoSaveIfDue"/>）；</item>
	/// <item>**退出**：先存档再停地图（不留"最后一次操作没落盘"）。</item>
	/// </list>
	/// </summary>
	public sealed class SessionEntryService
	{
		/// <summary>自动存档间隔（游戏日）——设计口径：一个月一次（与月结算同节拍，读档后"最多丢一个月"）。</summary>
		public const int AutoSaveIntervalDays = 30;

		private readonly GameSession _session;
		private readonly MapAppService _map;
		private readonly SessionOrchestrator _orchestrator;
		private readonly WorldSaveService _worldSave;

		public SessionEntryService(
			GameSession session,
			MapAppService map,
			SessionOrchestrator orchestrator,
			WorldSaveService worldSave)
		{
			_session = session;
			_map = map;
			_orchestrator = orchestrator;
			_worldSave = worldSave;
		}

		/// <summary>是否已在一局里（`NewGame` / `Load` 成功后为真，`Quit` 后为假）。</summary>
		public bool InGame { get; private set; }

		/// <summary>当前地图 Id（未开局为 <c>null</c>）。</summary>
		public string CurrentMapId { get; private set; }

		/// <summary>最近一次成功存档的游戏日（未存档 = -1）。</summary>
		public int LastSavedDay { get; private set; } = -1;

		/// <summary>最近一次**自动**存档的游戏日（未发生 = -1）。</summary>
		public int LastAutoSaveDay { get; private set; } = -1;

		/// <summary>自动存档次数（用例用它锁"不该存的时候没存"）。</summary>
		public int AutoSaveCount { get; private set; }

		/// <summary>最近一次失败的原因键（成功时 <c>null</c>）。</summary>
		public string LastError { get; private set; }

		/// <summary>新开局：生成地图并把所有势力交给编排器（出生点/开局单位/资源池/月结/事件/AI）。</summary>
		public bool NewGame(string mapId, int seed, int width, int height)
		{
			if (string.IsNullOrWhiteSpace(mapId)) { LastError = SessionEntryKeys.NoMap; return false; }

			_map.GenerateMap(seed, width, height, mapId);
			IReadOnlyList<PlayerStartReport> reports = _orchestrator.StartMap(mapId);
			if (reports == null || reports.Count == 0)
			{
				LastError = SessionEntryKeys.StartFailed;
				InGame = false;
				return false;
			}

			CurrentMapId = mapId;
			InGame = true;
			LastSavedDay = -1;
			LastAutoSaveDay = -1;
			AutoSaveCount = 0;
			LastError = null;
			return true;
		}

		/// <summary>存档（读档/退出的前提）：统一存档点，一次原子落盘。</summary>
		public bool Save()
		{
			if (!InGame || string.IsNullOrWhiteSpace(CurrentMapId)) { LastError = SessionEntryKeys.NoGame; return false; }

			_worldSave.SaveWorld(CurrentMapId);
			LastSavedDay = _session.CurrentDay;
			LastError = null;
			return true;
		}

		/// <summary>读档：逐 owner 恢复后重启编排器（重启是幂等的，见 `D80`）。</summary>
		public bool Load(string mapId)
		{
			if (string.IsNullOrWhiteSpace(mapId)) { LastError = SessionEntryKeys.NoMap; return false; }

			bool ok = _worldSave.LoadWorld(mapId, _orchestrator.OwnerIds);
			// `LoadWorld` 对"没有存档"返回 true（它的语义是"没有可恢复的东西"）——入口层要求更严：
			// 地图必须真的存在，否则"读档成功"会让宿主进到一个空世界。
			if (!ok || _session.Maps.Get(mapId) == null)
			{
				LastError = _worldSave.LastLoadError ?? SessionEntryKeys.LoadFailed;
				InGame = false;
				return false;
			}

			_orchestrator.ResetEngines(mapId); // （v0.9.9）读档把时间轴订阅清空了 ⇒ AI 的"已挂上"痕迹也要作废，否则 StartMap 不会重挂
			_orchestrator.StartMap(mapId);
			CurrentMapId = mapId;
			InGame = true;
			LastSavedDay = _session.CurrentDay;
			LastAutoSaveDay = _session.CurrentDay;
			LastError = null;
			return true;
		}

		/// <summary>
		/// 自动存档点：距上次自动存档满 <see cref="AutoSaveIntervalDays"/> 游戏日就存一次。
		/// <para>宿主（`GodotTimeDriver` / 冒烟 / 用例）在推进时间后调用；未开局或不足间隔时返回 false 且**不写盘**。</para>
		/// </summary>
		public bool AutoSaveIfDue()
		{
			if (!InGame || string.IsNullOrWhiteSpace(CurrentMapId)) return false;

			int day = _session.CurrentDay;
			if (LastAutoSaveDay >= 0 && day - LastAutoSaveDay < AutoSaveIntervalDays) return false;

			if (!Save()) return false;

			LastAutoSaveDay = day;
			AutoSaveCount++;
			return true;
		}

		/// <summary>退出：先存档再停地图（`StopMap` 摘掉订阅，避免"退了还在跑"）。</summary>
		public bool Quit()
		{
			if (!InGame || string.IsNullOrWhiteSpace(CurrentMapId))
			{
				LastError = SessionEntryKeys.NoGame;
				return false;
			}

			Save();
			_orchestrator.StopMap(CurrentMapId);
			InGame = false;
			CurrentMapId = null;
			return true;
		}

		/// <summary>入口层的失败原因键（表现层 `I18n.T(key)`；文案在 `Config/Strings.*.json`）。</summary>
		public static class SessionEntryKeys
		{
			public const string NoMap = "session.no_map";
			public const string NoGame = "session.no_game";
			public const string StartFailed = "session.start_failed";
			public const string LoadFailed = "session.load_failed";
		}
	}
}
