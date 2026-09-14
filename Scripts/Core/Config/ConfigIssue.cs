namespace SciencePotato.Scripts.Core.Config
{
	/// <summary>（v0.3 / WP-1.4）配置校验问题的等级：error 阻断启动，warning 仅提示（见 §13.z「校验分级」）。</summary>
	public enum ConfigIssueLevel
	{
		Warning = 0,
		Error = 1,
	}

	/// <summary>
	/// （v0.3 / WP-1.4）单条配置校验问题。定位信息刻意做成「表 + 行 Id」的形式，
	/// 便于填表者直接用 <see cref="ToString"/> 的输出跳到出错的表项。
	/// </summary>
	public sealed class ConfigIssue
	{
		public ConfigIssue(ConfigIssueLevel level, string table, string id, string message)
		{
			Level = level;
			Table = table ?? string.Empty;
			Id = id ?? string.Empty;
			Message = message ?? string.Empty;
		}

		public ConfigIssueLevel Level { get; }

		/// <summary>表名（与 <c>Config/{name}.json</c> 的文件名一致，如 "Buildings"）。</summary>
		public string Table { get; }

		/// <summary>表内条目 Id（表级问题时为空）。</summary>
		public string Id { get; }

		public string Message { get; }

		public override string ToString()
		{
			string level = Level == ConfigIssueLevel.Error ? "ERROR" : "WARN ";
			string location = string.IsNullOrEmpty(Id) ? Table : $"{Table}/{Id}";
			return $"[{level}] {location}: {Message}";
		}
	}
}
