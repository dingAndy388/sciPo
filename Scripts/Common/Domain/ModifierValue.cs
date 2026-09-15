using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / `WP-3.3`；v0.8.3 / `WP-4.1` 加 <see cref="Stage"/>）运行期的一条修正。
	/// <para><see cref="SourceId"/> = 谁挂的（建筑 uid / 科技节点 Id / 事件 Id）：`WP-4.1` 起它同时承担
	/// **宿主作用域**查询（"只算来自这栋建筑的修正"）与**同宿主去重**（同一宿主对同一目标只算一次）。</para>
	/// </summary>
	public struct ModifierValue(ModifierType type, float value, string sourceId, ModifierStage stage = ModifierStage.Building)
	{
		public ModifierType Type { get; } = type;
		public float Value { get; } = value;
		public string SourceId { get; } = sourceId;

		/// <summary>阶段（缺省 = 建筑；存档缺字段的历史数据同样落到这里）。</summary>
		public ModifierStage Stage { get; } = stage;
	}
}
