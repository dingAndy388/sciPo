using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core.Config;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Application;
using SciencePotato.Scripts.Map.Domain;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.3）组合根的产物：**已装配好的服务集合**（无状态）。
	/// 与 <see cref="GameSession"/>（只持状态）分离，避免"服务与状态混在同一个对象里"导致测试无法替换实现。
	/// <para>v0.3 / WP-1.4 起 <see cref="Tables"/> 持有 7 张配置表，<see cref="ConfigReport"/> 持有启动期校验结论
	/// （error 才会阻断启动，warning 仅提示）。装配是增量的：`WP-2.x` 会依次把 Construction / Units /
	/// TechTrees / Events 的应用服务接到这些表上。</para>
	/// </summary>
	public sealed class CoreServices
	{
		public GameSession Session { get; init; }

		/// <summary>
		/// （v0.3 / WP-1.5）**游戏日节拍总线**：所有周期任务（建造 / 训练 / 人口 / 事件 / 资源月结）
		/// 都注册到这里，由 <see cref="GameSession.Clock"/> 逐日派发（`OnTick(1 日)`）。
		/// <para>调用方只需推进时钟（`Session.Advance(realSeconds)` 或 `ITimeDriver.Advance`），
		/// 不必关心"秒"。</para>
		/// </summary>
		public GameTimeService Time { get; init; }

		public IConfigSource ConfigSource { get; init; }

		/// <summary>7 张配置表（Terrains / Resources / Buildings / Units / TechTrees / Events / Generator）。</summary>
		public ConfigTables Tables { get; init; }

		/// <summary>启动期配置校验报告（真实配置要求 0 error）。</summary>
		public ConfigReport ConfigReport { get; init; }

		public IMapGenerator MapGenerator { get; init; }

		public MapAppService Map { get; init; }

		/// <summary>
		/// （v0.3 / WP-2.10）**领域事件总线**：全进程一份（`ROOT-4` / `UNIT-08` / `TECH-05` / `EVT-04`）。
		/// <para>应用服务在构造时接收它并发布事件；表现层/统计/联动模块订阅它，
		/// 从而不必轮询 `IsReady`/`IsResearched`/`GetActiveEvents`。</para>
		/// </summary>
		public IDomainEventBus DomainEvents { get; init; }
	}
}

