using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SciencePotato.Scripts.Core.Config
{
	/// <summary>
	/// （v0.3 / WP-1.4）配置表校验报告：装配期收集，随 <see cref="CoreServices"/> 暴露给表现层与测试。
	/// <para>分级语义：<see cref="HasErrors"/> 为 true 时 <see cref="CoreBootstrap"/> 默认快速失败；
	/// 仅有 warning 时**照常启动**（原型数据必然存在与设计规模不符之处，不能因此阻断开发）。</para>
	/// </summary>
	public sealed class ConfigReport
	{
		private readonly List<ConfigIssue> _issues = new();

		public IReadOnlyList<ConfigIssue> Issues => _issues;

		public int ErrorCount => _issues.Count(i => i.Level == ConfigIssueLevel.Error);

		public int WarningCount => _issues.Count(i => i.Level == ConfigIssueLevel.Warning);

		public bool HasErrors => _issues.Any(i => i.Level == ConfigIssueLevel.Error);

		public void Error(string table, string id, string message) => _issues.Add(new ConfigIssue(ConfigIssueLevel.Error, table, id, message));

		public void Warn(string table, string id, string message) => _issues.Add(new ConfigIssue(ConfigIssueLevel.Warning, table, id, message));

		public IEnumerable<ConfigIssue> ForTable(string table) => _issues.Where(i => i.Table == table);

		/// <summary>一行摘要，用于日志与异常消息。</summary>
		public string Summary() => $"配置校验：{ErrorCount} 个 error / {WarningCount} 个 warning";

		/// <summary>全部问题逐行列出（缩进两格），用于异常消息与启动日志。</summary>
		public string ToLines()
		{
			if (_issues.Count == 0) return "  （无问题）";
			var builder = new StringBuilder();
			foreach (ConfigIssue issue in _issues) builder.Append("  ").Append(issue).Append('\n');
			return builder.ToString().TrimEnd('\n');
		}
	}
}
