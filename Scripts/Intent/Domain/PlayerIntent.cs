using SciencePotato.Scripts.Common.Domain;

namespace SciencePotato.Scripts.Intent.Domain
{
	/// <summary>
	/// （v0.9.4 / `WP-5.7`）**一条玩家意图**：表现层与规则层之间唯一的输入结构（`I2`）。
	/// <list type="bullet">
	/// <item>字段按 <see cref="Kind"/> 解释，越界的字段填空即可（不引入继承层次 —— 意图是"数据"不是"对象树"）；</item>
	/// <item><see cref="HasTarget"/> 是**显式标志**：<see cref="HexCubePosition"/> 是 struct，
	/// 默认值 <c>(0,0)</c> 恰好是合法坐标，没有这个标志就无法区分"没给目标"和"目标是原点"；</item>
	/// <item>用工厂方法构造（`Build`/`Upgrade`/…）而不是到处 `new PlayerIntent { ... }`：字段口径只有一处。</item>
	/// </list>
	/// </summary>
	public sealed class PlayerIntent
	{
		public IntentKind Kind { get; init; }

		/// <summary>发起者（人类势力的 owner）。**必须**能通过 `GameSession.IsHuman` —— UI 不能指挥 AI。</summary>
		public int OwnerId { get; init; }

		public string MapId { get; init; }

		/// <summary>目标实体的 Id：建筑 Id（`Build`）/ 单位 Id（`Train`）/ 科技节点 Id（`Research`）/ 事件 Id（`ResolveEvent`）。</summary>
		public string Id { get; init; }

		/// <summary>仅 `Research`：科技树 Id（`math`/`physics`/`chemistry`）。</summary>
		public string TreeId { get; init; }

		/// <summary>作用对象：要升级的建筑 uid（`Upgrade`）/ 要下单的建筑 uid（`Train`）/ 要移动的单位 uid（`MoveUnit`）。</summary>
		public string SourceUid { get; init; }

		/// <summary>格位目标：建造位置（`Build`）/ 移动目的地（`MoveUnit`）。</summary>
		public HexCubePosition Target { get; init; }

		/// <summary><see cref="Target"/> 是否有效（见类型注释：struct 默认值也是合法坐标）。</summary>
		public bool HasTarget { get; init; }

		public static PlayerIntent Build(string mapId, int ownerId, string buildingId, HexCubePosition position) => new PlayerIntent
		{
			Kind = IntentKind.Build, MapId = mapId, OwnerId = ownerId, Id = buildingId, Target = position, HasTarget = true,
		};

		public static PlayerIntent Upgrade(string mapId, int ownerId, string buildingUid) => new PlayerIntent
		{
			Kind = IntentKind.Upgrade, MapId = mapId, OwnerId = ownerId, SourceUid = buildingUid,
		};

		public static PlayerIntent Train(string mapId, int ownerId, string buildingUid, string unitId) => new PlayerIntent
		{
			Kind = IntentKind.Train, MapId = mapId, OwnerId = ownerId, SourceUid = buildingUid, Id = unitId,
		};

		public static PlayerIntent Research(string mapId, int ownerId, string treeId, string nodeId) => new PlayerIntent
		{
			Kind = IntentKind.Research, MapId = mapId, OwnerId = ownerId, TreeId = treeId, Id = nodeId,
		};

		public static PlayerIntent Move(string mapId, int ownerId, string unitUid, HexCubePosition destination) => new PlayerIntent
		{
			Kind = IntentKind.MoveUnit, MapId = mapId, OwnerId = ownerId, SourceUid = unitUid, Target = destination, HasTarget = true,
		};

		public static PlayerIntent DecideEvent(string mapId, int ownerId, string eventId) => new PlayerIntent
		{
			Kind = IntentKind.ResolveEvent, MapId = mapId, OwnerId = ownerId, Id = eventId,
		};

		public override string ToString() => $"{Kind}(owner={OwnerId}, map={MapId}, id={Id}, tree={TreeId}, src={SourceUid}, target={(HasTarget ? Target.ToString() : "-")})";
	}
}
