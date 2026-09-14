using Newtonsoft.Json;

namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / WP-2.2）任务快照：任务仓储的唯一持久化形态。
	/// <para><b>字段语义</b>（`TIME-03` 的修复前提 —— 三个字段此前没有明确分工，被各模块按各自方便填写）：</para>
	/// <list type="bullet">
	/// <item><see cref="Type"/> = **任务类型**（Construction / Training / Research / EventTick / ResourceGrowth / PopulationGrowth / UnitMove / UnitAttack）。</item>
	/// <item><see cref="UId"/> = **实例唯一 Id**（实体 uid；无实体归属的任务填 <c>"none"</c>）。</item>
	/// <item><see cref="Id"/> = **业务模板 / 业务键**（BuildingId、UnitId、科技节点 Id、事件 Id、资源名；少量历史任务直接用它承载实体 uid）。</item>
	/// </list>
	/// <para><b>存储键 = <c>Type:UId:Id</c></b>（见 <see cref="Key"/>）。旧实现只用 <c>Id</c> 作键，导致"同时造两座同名建筑"
	/// "同时训练两个同种单位"时后一个快照**静默覆盖**前一个（读档只能恢复一个）。</para>
	/// <para>键只由三个业务字段派生（不含 <see cref="Progress"/>），因此"重建同一个任务"得到同一个键，
	/// 而"同名但不同实例"（不同 uid）天然互不覆盖。</para>
	/// </summary>
	public class TaskSnapshot
	{
		public string MapId;
		public int OwnerId;
		public float Progress;
		public float Target;
		public string Id;
		public string Type;
		public string UId;
		public bool IsCompleted;

		/// <summary>
		/// 仓储键（<c>Type:UId:Id</c>）。**不参与序列化**：它由三个字段派生，写进文件只会造成"两处真相"。
		/// </summary>
		[JsonIgnore]
		public string Key => BuildKey(Type, UId, Id);

		/// <summary>构造仓储键（`TIME-03`）；空字段归一化为 <c>"none"</c>，保证键永远非空且可读。</summary>
		public static string BuildKey(string type, string uid, string id)
			=> $"{Normalize(type)}:{Normalize(uid)}:{Normalize(id)}";

		private static string Normalize(string value)
			=> string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
	}
}