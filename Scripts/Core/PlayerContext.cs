using System;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.6.0 / WP-4.18）**玩家上下文**：一个 owner（势力）的身份与类型 —— 多玩家地基的最小单元。
	/// <para>背景（`DEP-03` / `ROOT-2`）：改造前\"谁是玩家\"没有任何显式表示，所有服务都默认 owner=1，
	/// 于是\"一局里 AI 与人类共用同一份资源池/迷雾/科技\"是**隐式**成立的 —— 这在单人原型里看不出问题，
	/// 一旦加入 AI 对手就会立刻串味（AI 花玩家的钱、玩家的迷雾被 AI 揭开）。</para>
	/// <para>ownerId 的取值约定（与 `EnemySpawner.HostileOwnerId` 一致）：</para>
	/// <list type="bullet">
	/// <item><c>&gt;= 1</c>：**势力**（人类玩家或 AI 对手），拥有资源池/科技树/迷雾/建筑/单位；</item>
	/// <item><c>0</c>：中立（无主地块/中立建筑）；</item>
	/// <item><c>&lt; 0</c>：野怪（敌方封锁单位，不参与胜负判定，见 `WP-3.8`）。</item>
	/// </list>
	/// </summary>
	public sealed class PlayerContext
	{
		/// <summary>势力的最小 ownerId（0 与负数保留给中立 / 野怪）。</summary>
		public const int FirstOwnerId = 1;

		/// <summary>势力 Id（地图/资源/科技/迷雾的各种键都带着它）。</summary>
		public int OwnerId { get; }

		/// <summary>是否人类玩家。**只有人类玩家**会走事件引擎与决策暂停（`G8` / `WP-4.12` 口径）。</summary>
		public bool IsHuman { get; }

		/// <summary>显示名（界面用；缺省按类型给 \"玩家N\" / \"AI N\"）。</summary>
		public string DisplayName { get; }

		public PlayerContext(int ownerId, bool isHuman, string displayName = null)
		{
			if (ownerId < FirstOwnerId)
				throw new ArgumentOutOfRangeException(nameof(ownerId), ownerId,
					$"势力 ownerId 必须 >= {FirstOwnerId}（0 保留给中立，负数保留给野怪）");

			OwnerId = ownerId;
			IsHuman = isHuman;
			DisplayName = string.IsNullOrWhiteSpace(displayName)
				? (isHuman ? $"玩家{ownerId}" : $"AI {ownerId}")
				: displayName;
		}

		public static PlayerContext Human(int ownerId = FirstOwnerId, string displayName = null)
			=> new(ownerId, true, displayName);

		public static PlayerContext Ai(int ownerId, string displayName = null)
			=> new(ownerId, false, displayName);

		public override string ToString() => $"{DisplayName}(owner={OwnerId}, {(IsHuman ? "人类" : "AI")})";
	}
}
