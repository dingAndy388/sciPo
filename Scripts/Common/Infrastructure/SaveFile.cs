using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.3）统一存档文件的**磁盘结构**：文件头（版本 / 会话 / 游戏日 / 已应用迁移）+ 若干具名分区。
	/// <para>分区内容是**原始 JSON 文本**而不是强类型对象 —— 这样存档单元不需要认识任何子系统的模型
	/// （各仓储自己序列化），也便于迁移器只改动它关心的那一段。</para>
	/// <para>版本历史（**只增不改**，口径与 <c>MapSave.SaveVersion</c> 一致）：
	/// <list type="number">
	/// <item><b>v1</b>：无版本头的单文件（早期把各文件手工折进一个文件时产生）；</item>
	/// <item><b>v2</b>：有版本头，但任务分区键仍是旧口径（只用 `Id`），时钟仍是一个独立分区；</item>
	/// <item><b>v3</b>：任务分区键 = `Type:UId:Id`（`TIME-03`），**时钟并入文件头**，事件状态开始落盘。</item>
	/// </list></para>
	/// </summary>
	public sealed class SaveFile
	{
		/// <summary>当前程序写出的版本。</summary>
		public const int CurrentVersion = 3;

		/// <summary>文件头版本（缺失 = v1）。</summary>
		public int SaveVersion { get; set; } = CurrentVersion;

		/// <summary>会话 id（多存档/多会话区分）。</summary>
		public string SessionId { get; set; }

		/// <summary>存档点的游戏日。</summary>
		public double Day { get; set; }

		/// <summary>分区：键 = `<子系统>:<业务 id>`，值 = 该子系统的原始 JSON。</summary>
		public Dictionary<string, string> Sections { get; set; } = new Dictionary<string, string>();

		/// <summary>已应用的迁移名（**留痕**：排查老档时能看出它被怎样升过级）。</summary>
		public List<string> Migrations { get; set; } = new List<string>();
	}
}
