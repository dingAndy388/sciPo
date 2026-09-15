using Godot;
using SciencePotato.Scripts.AI.Domain;
using SciencePotato.Scripts.Core;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Presentation;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Dev
{
	/// <summary>
	/// （v0.6.0 / WP-5.2）**无头冒烟报告**：在真实引擎里把"一局跑起来"的每一步走一遍，并用退出码回答能不能过。
	/// <para>为什么需要它：无头用例（`Tests/…HeadlessChecks`）验的是**纯 C# 核心**，永远碰不到 Godot 适配层 ——
	/// 资源加载、autoload 装配顺序、场景脚本注入、`user://` 落盘这些问题只有真引擎跑得出来。
	/// 本节点把这些收在一个可重复、可判定的入口里（`--headless` 即可，无需人眼看）。</para>
	/// <para>运行方式（用户参数 `--smoke` 让 `ServiceContainer` 换用独立的 `user://save/smoke.json`，
	/// 不覆盖开发中的正式档）：</para>
	/// <code>
	/// Godot_v4.6-stable_mono_win64.exe --headless --path &lt;repo&gt; res://Scene/Dev/smoke_report.tscn --quit-after 2000 -- --smoke
	/// </code>
	/// <para>退出码：0 = 全部通过；1 = 有失败项（stdout 里有 `[SMOKE][FAIL]` 行）。</para>
	/// </summary>
	public partial class SmokeReport : Node
	{
		private const string MapId = "smoke";

		/// <summary>（v0.6.0 / WP-5.8）冒烟地图规模：可用 `--size=WxH` 覆盖（默认 73×143 = 10439 格，`D76`）。</summary>
		private int _mapWidth = DevMapUi.DefaultWidth;

		private int _mapHeight = DevMapUi.DefaultHeight;

		private int _seed = 20260914;

		/// <summary>（v0.6.0 / WP-5.8）各阶段实测耗时（毫秒），最后一行汇总打印。</summary>
		private readonly Dictionary<string, ulong> _timings = new();

		private readonly List<string> _failures = new();
		private int _passed;

		public override void _Ready()
		{
			GD.Print("=== Science Potato · Godot 无头冒烟报告 ===");

			// ⓪ 干净开局：删掉上一次冒烟的存档。否则第二次运行会**读到上一次的存档**
			//（资源池已存在 → 成长任务不再登记），结果不可复现 —— 冒烟报告必须是可重复的。
			string savePath = ProjectSettings.GlobalizePath("user://save/smoke.json");
			GD.Print($"[SMOKE] 冒烟存档：{savePath}（存在={FileAccess.FileExists("user://save/smoke.json")}）");
			if (FileAccess.FileExists("user://save/smoke.json") && DirAccess.RemoveAbsolute(savePath) == Error.Ok)
				GD.Print("[SMOKE] 已清除上一次冒烟存档");

			ServiceContainer container = ServiceContainer.Instance;
			if (container == null || container.Core == null)
			{
				GD.PrintErr("[SMOKE][FAIL] ServiceContainer 未装配（autoload 失败？）");
				Finish();
				return;
			}

			CoreServices core = container.Core;

			// ① 参数（v0.6.0 / WP-5.8）：--size=WxH / --seed=N 让同一次冒烟能测不同规模与种子
			ReadArguments();
			ulong memoryBefore = OS.GetStaticMemoryUsage();
			GD.Print($"[SMOKE] 规模 {_mapWidth}×{_mapHeight}={_mapWidth * _mapHeight} 格，seed={_seed}；起始静态内存 {memoryBefore / 1024.0 / 1024.0:0.0} MB");

			// ② 冻结时间：冒烟自己按"日"推进，避免 GodotTimeDriver 每帧也在推进（结果不可复现）
			core.Session.IsPaused = true;
			core.Session.Clock.MaxDaysPerAdvance = int.MaxValue;

			Check("装配：全部应用服务就位", ServicesReady(core));
			Check("配置：0 error", core.ConfigReport != null && core.ConfigReport.ErrorCount == 0);
			Check("外观：格子步长/贴图目录来自配置表（换美术不改代码）", AppearanceMatchesConfigFile(core));
			Check("玩家表：至少 1 个人类势力", core.Session.Players.Count >= 1 && core.Session.HumanOwnerId >= 1);

			// ③ 生成正式规模地图并计时（`R1` 的规模数据点；`WP-5.8` 把这里的数字固化成基线）
			ulong startedMs = Time.GetTicksMsec();
			core.Map.GenerateMap(_seed, _mapWidth, _mapHeight, MapId);
			_timings["generate"] = Time.GetTicksMsec() - startedMs;

			int cells = 0;
			foreach (Map.Domain.MapCell _ in core.Map.GetAllCells(MapId)) cells++;
			GD.Print($"[SMOKE] 地图 {MapId}：{cells} 格，生成耗时 {_timings["generate"]} ms，" +
					 $"生成后静态内存 {OS.GetStaticMemoryUsage() / 1024.0 / 1024.0:0.0} MB");
			Check($"地图：生成 {_mapWidth * _mapHeight} 格（实际 {cells}）", cells == _mapWidth * _mapHeight);
			Check($"地图：生成耗时 < {GenerateBudgetMs} ms（当前 {_timings["generate"]}）", _timings["generate"] < GenerateBudgetMs);

			// ④ 启动全部势力的子系统（会话级编排，`WP-4.18`）
			startedMs = Time.GetTicksMsec();
			IReadOnlyList<PlayerStartReport> started = core.Orchestrator.StartMap(MapId);
			_timings["start"] = Time.GetTicksMsec() - startedMs;
			Check("编排：每个势力都启动了资源池与月结", started.Count == core.Session.Players.Count);
			Check("开局：人类拿到了出生点与开局单位", started[0].Spawn.HasValue && started[0].InitialUnitCount > 0);
			foreach (PlayerStartReport report in started) GD.Print($"[SMOKE] {report}");

			// 出生点明细（M1 的"看得见自己家"依赖它）
			var humanReport = started[0];
			if (humanReport.Spawn.HasValue)
				GD.Print($"[SMOKE] 出生点：人类 ({humanReport.Spawn.Value.q},{humanReport.Spawn.Value.r})" +
						 $"，间距配置 ≥ {core.Tables.Start.MinSpawnDistance}，开局单位 {humanReport.InitialUnitCount} 个");

			// 周期任务明细：每个势力 = 月结 1 条 + 每个可成长资源 1 条（资源表 `GrowInterval`）；
			// 事件引擎只有人类有（`G8`）；AI 决策循环只给非人类（v0.7.3 / WP-6.2）
			int growthTasksPerPlayer = 0;
			foreach (var resource in core.Tables.AllResources())
				if (resource.GrowInterval > 0f) growthTasksPerPlayer++;

			int aiPlayers = 0;
			foreach (PlayerContext player in core.Session.Players)
				if (!player.IsHuman) aiPlayers++;

			int expectedTasks = core.Session.Players.Count * (1 + growthTasksPerPlayer) + 1 + aiPlayers;
			GD.Print($"[SMOKE] 周期任务：实际 {core.Time.SubscriberCount} 条（期望 势力{core.Session.Players.Count} ×（月结1+资源成长{growthTasksPerPlayer}）" +
					 $" + 人类事件1 + AI决策{aiPlayers} = {expectedTasks}）");
			Check("编排：周期任务数 = 势力 ×（月结 + 资源成长）+ 人类事件 + AI 决策", core.Time.SubscriberCount == expectedTasks);

			bool humanEvents = core.Events != null && core.Events.IsEngineStarted(MapId, core.Session.HumanOwnerId);
			Check("编排：人类玩家的事件引擎已启动", humanEvents);

			// ④ 推进 3 个月：月结/资源成长/事件都要真的跑起来
			int subscribersBefore = core.Time.SubscriberCount;
			startedMs = Time.GetTicksMsec();
			core.Session.Clock.AdvanceDays(TimeConstants.DaysPerMonth * 3);
			_timings["advance90"] = Time.GetTicksMsec() - startedMs;
			GD.Print($"[SMOKE] 启动后周期任务 {subscribersBefore} 条；推进 90 日：日期={core.Session.Clock.Format()}，周期任务 → {core.Time.SubscriberCount}，" +
					 $"月结次数={core.Settlement?.SettledCount ?? 0}，掷骰次数={core.Events?.RollCount ?? 0}");

			// 资源池实测值（证明"月结 + 建筑/科技修正器"真的把钱算到了玩家账上，而不是只有报告对象）
			if (core.Resources != null)
			{
				var poolSnapshot = new List<string>();
				foreach (var resource in core.Tables.AllResources())
				{
					float value = core.Resources.GetOrCreatePool(MapId, core.Session.HumanOwnerId).GetValue(resource.Name);
					poolSnapshot.Add($"{resource.Name}={value}");
				}
				GD.Print($"[SMOKE] 玩家资源池：{string.Join("，", poolSnapshot)}");
			}

			Check("时间：90 日后月结至少发生 1 次", core.Settlement != null && core.Settlement.SettledCount >= 1);
			Check("时间：人类玩家的月结报告可读", core.Settlement != null && core.Settlement.LastReport(MapId, core.Session.HumanOwnerId) != null);

			// AI 决策循环确实在跑（v0.7.3 / WP-6.2）：90 日 ≥ 3 个节拍 ⇒ 应有决策记录
			if (aiPlayers > 0 && core.AiService != null)
			{
				var aiDecisionLines = new List<string>();
				foreach (PlayerContext player in core.Session.Players)
				{
					if (player.IsHuman) continue;
					AiDecision last = core.AiService.LastDecision(MapId, player.OwnerId);
					aiDecisionLines.Add(last == null ? $"{player.DisplayName}：无决策" : last.ToString());
				}
				GD.Print($"[SMOKE] AI 决策：共 {core.AiService.DecisionCount} 次 —— {string.Join(" | ", aiDecisionLines)}");
				Check("编排：AI 势力已按节拍产出决策（WP-6.2）", core.AiService.DecisionCount >= aiPlayers);
			}

			// ⑤ 存档点 → 读档点（真实 user:// 落盘 + 任务恢复）
			startedMs = Time.GetTicksMsec();
			core.WorldSave.SaveWorld(MapId);
			_timings["save"] = Time.GetTicksMsec() - startedMs;

			startedMs = Time.GetTicksMsec();
			bool loaded = core.WorldSave.LoadWorld(MapId, core.Session.HumanOwnerId);
			_timings["load"] = Time.GetTicksMsec() - startedMs;
			GD.Print($"[SMOKE] 存档往返：loaded={loaded}，恢复任务 {core.WorldSave.LastRestoredTaskCount} 条");
			Check("存档：读档成功", loaded);
			Check("存档：读档恢复了周期任务", core.WorldSave.LastRestoredTaskCount > 0);

			// ⑥ 表现层端到端（`WP-5.2` 的核心回归）：把**真实主场景** `map_view.tscn` 实例化并渲染整张地图。
			// 这正是改造前必 NRE 的路径（`MapView._mapQuery` 从未赋值）+ 场景路径大小写（`AutoLoad` vs `Autoload`）。
			CheckSceneRendering(core, cells);

			Finish();
		}

		/// <summary>实例化 `map_view.tscn` → 注入服务 → 渲染全部格子（真引擎下的表现层回归）。</summary>
		private void CheckSceneRendering(CoreServices core, int expectedCells)
		{
			var scene = GD.Load<PackedScene>("res://Scene/Map/map_view.tscn");
			if (scene == null)
			{
				Check("表现层：map_view.tscn 可加载", false);
				return;
			}

			var view = scene.Instantiate<MapView>();
			AddChild(view);                      // 触发 _Ready：取服务、找 MapCells 容器
			Check("表现层：MapView 取到 MapAppService（旧版必 NRE）", view.IsWired);
			Check("表现层：场景内相机带 CameraController", view.GetNodeOrNull<CameraController>("Camera2D") != null);

			view.MapId = MapId;
			ulong startedMs = Time.GetTicksMsec();
			view.UpdateAllCells();
			ulong renderMs = Time.GetTicksMsec() - startedMs;
			_timings["render"] = renderMs;

			GD.Print($"[SMOKE] 表现层：渲染 {view.RenderedCellCount} 格耗时 {renderMs} ms（缺贴图时只警告、不崩）");
			Check($"表现层：渲染格数 = 地图格数（{expectedCells}）", view.RenderedCellCount == expectedCells);
			Check($"表现层：渲染耗时 < {RenderBudgetMs} ms（当前 {renderMs}）", renderMs < RenderBudgetMs);

			view.QueueFree();
		}

		/// <summary>
		/// （v0.6.0 / WP-5.3）**配置 → 服务 → 表现层**的链路核对：直接读 <c>res://Config/Terrains.json</c> 的原始文本，
		/// 与 <see cref="CoreServices.Appearance"/> 的值比对。
		/// <para>为什么值得单独查一次：这条链路断了的表现是"地图还能画出来，但用的是代码里的旧常量" ——
		/// 换美术时改了配置却不生效，很难查。"读文件比对"是唯一不依赖同一条代码路径的自证方式。</para>
		/// </summary>
		private static bool AppearanceMatchesConfigFile(CoreServices core)
		{
			using FileAccess file = FileAccess.Open("res://Config/Terrains.json", FileAccess.ModeFlags.Read);
			if (file == null)
			{
				GD.Print("[SMOKE] 无法读取 res://Config/Terrains.json");
				return false;
			}

			var json = Newtonsoft.Json.Linq.JObject.Parse(file.GetAsText());
			float expectedX = json.Value<float?>("CellXStep") ?? TerrainAppearance.DefaultCellXStep;
			float expectedY = json.Value<float?>("CellYStep") ?? TerrainAppearance.DefaultCellYStep;
			string expectedDir = json.Value<string>("TerrainSpriteDir") ?? TerrainAppearance.DefaultSpriteDir;

			GD.Print($"[SMOKE] 外观比对：表 CellXStep={expectedX} CellYStep={expectedY} 目录={expectedDir}；" +
					 $"服务 {core.Appearance.CellXStep}/{core.Appearance.CellYStep}/{core.Appearance.TerrainSpriteDir}");

			return core.Appearance.CellXStep == expectedX
				&& core.Appearance.CellYStep == expectedY
				&& core.Appearance.TerrainSpriteDir == expectedDir;
		}

		/// <summary>
		/// （v0.6.0 / WP-5.8）**耗时护栏**：宽松到只抓"数量级退化"（引擎换版本/生成器改坏/渲染逐格建纹理这类）。
		/// <para>为什么不做严格时间断言：CI 机器性能差异会让严格阈值变成"假红"，而假红会让人开始无视它。
		/// 精确基线数字记在 <c>Document/PerfBaseline.md</c>，由人对比。</para>
		/// </summary>
		private const ulong GenerateBudgetMs = 5000;

		private const ulong RenderBudgetMs = 15000;

		/// <summary>命令行用户参数（`--` 之后）：<c>--size=WxH</c> / <c>--seed=N</c>。</summary>
		private void ReadArguments()
		{
			foreach (string arg in OS.GetCmdlineUserArgs())
			{
				if (arg == null) continue;

				if (arg.StartsWith("--size=", System.StringComparison.Ordinal))
				{
					string[] parts = arg.Substring("--size=".Length).Split('x', 'X');
					if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0)
					{
						_mapWidth = w;
						_mapHeight = h;
					}
				}
				else if (arg.StartsWith("--seed=", System.StringComparison.Ordinal)
						 && int.TryParse(arg.Substring("--seed=".Length), out int seed))
				{
					_seed = seed;
				}
			}
		}

		/// <summary>把本次跑出来的耗时打成一行（便于贴进 <c>Document/PerfBaseline.md</c> 做对比）。</summary>
		private void PrintBaselineLine()
		{
			ulong memory = OS.GetStaticMemoryUsage();
			long saveBytes = 0;

			using (FileAccess save = FileAccess.Open("user://save/smoke.json", FileAccess.ModeFlags.Read))
			{
				if (save != null) saveBytes = (long)save.GetLength();
			}

			long mapBytes = 0;
			using (FileAccess mapFile = FileAccess.Open($"user://maps/{MapId}.json", FileAccess.ModeFlags.Read))
			{
				if (mapFile != null) mapBytes = (long)mapFile.GetLength();
			}

			GD.Print($"[SMOKE][BASE] size={_mapWidth}x{_mapHeight}={_mapWidth * _mapHeight} cells seed={_seed} " +
					 $"generate={Get("generate")}ms start={Get("start")}ms advance90={Get("advance90")}ms " +
					 $"save={Get("save")}ms load={Get("load")}ms render={Get("render")}ms " +
					 $"worldSave={saveBytes / 1024.0:0.0}KB mapFile={mapBytes / 1024.0:0.0}KB " +
					 $"memStatic={memory / 1024.0 / 1024.0:0.0}MB memPeak={OS.GetStaticMemoryPeakUsage() / 1024.0 / 1024.0:0.0}MB");
		}

		private ulong Get(string key) => _timings.TryGetValue(key, out ulong value) ? value : 0ul;

		private static bool ServicesReady(CoreServices core)
			=> core.Map != null && core.Resources != null && core.Tech != null && core.Construction != null
				&& core.Units != null && core.Events != null && core.Fog != null && core.Settlement != null
				&& core.WorldSave != null && core.Orchestrator != null && core.Tasks != null;

		private void Check(string name, bool condition)
		{
			if (condition)
			{
				_passed++;
				GD.Print($"[SMOKE][PASS] {name}");
				return;
			}

			_failures.Add(name);
			GD.PrintErr($"[SMOKE][FAIL] {name}");
		}

		private void Finish()
		{
			PrintBaselineLine();

			GD.Print($"[SMOKE] 汇总：通过 {_passed} / 失败 {_failures.Count}");
			foreach (string failure in _failures) GD.Print($"[SMOKE]   失败：{failure}");

			GetTree().Quit(_failures.Count == 0 ? 0 : 1);
		}
	}
}
