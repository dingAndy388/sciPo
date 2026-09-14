using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core.Time;
using System;
using System.Collections.Generic;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>（v0.3 / WP-0.2）内存文件系统替身：让 Domain/Application 脱离 Godot 的 FileAccess 运行。</summary>
	internal sealed class InMemoryFileSystem : IFileSystem
	{
		private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

		public bool Exists(string path) => _files.ContainsKey(path);

		public string ReadAllText(string path) => _files.TryGetValue(path, out string text) ? text : null;

		public void WriteAllText(string path, string text) => _files[path] = text;

		public void EnsureDirectory(string directory) { /* 内存实现无需目录 */ }

		public IEnumerable<string> ListFiles(string directory)
		{
			foreach (string key in _files.Keys)
				if (key.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
					yield return key;
		}

		public void Delete(string path) => _files.Remove(path);
	}

	/// <summary>（v0.3 / WP-0.2）内存配置来源替身：预置若干 JSON 文本。</summary>
	internal sealed class InMemoryConfigSource : IConfigSource
	{
		private readonly Dictionary<string, string> _configs = new(StringComparer.OrdinalIgnoreCase);

		public void Inject(string configName, string json) => _configs[configName] = json;

		public string LoadText(string configName) => _configs.TryGetValue(configName, out string json) ? json : null;
	}

	/// <summary>（v0.3 / WP-0.2 / WP-1.1）手动时间驱动：测试里精确控制"推进多少真实秒"。</summary>
	internal sealed class ManualTimeDriver(GameClock clock) : ITimeDriver
	{
		public GameClock Clock { get; } = clock;

		public void Advance(double realDeltaSeconds) => Clock.Advance(realDeltaSeconds);
	}
}
