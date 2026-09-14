using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Events.Domain
{
	public interface IEventConfig
	{
		string EventId { get; }
		string Name { get; }
		string Description { get; }

		/// <summary>
		/// （v0.3 / WP-2.8）**每日触发概率**（原 <c>TriggerChance</c>）：口径 = design/events.md 的「%/日」，
		/// 即 0.2%/日 填 <c>0.002</c>。字段改名是为了让"逐日独立判定"写进字段名本身 ——
		/// 旧表若仍写 `TriggerChance`，该字段会静默为 0（事件永不触发），由启动期校验器报警提示。
		/// </summary>
		float TriggerChancePerDay { get; }

		/// <summary>持续天数（**游戏日**；0 = 永久）。</summary>
		int Duration { get; }
		List<Modifier> Modifiers { get; }
		Dictionary<string, float> ResourcePrerequisites { get; }
		Dictionary<string, List<string>> TechPrerequisites { get; }
	}
}