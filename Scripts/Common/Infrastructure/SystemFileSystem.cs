using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.3）**基于真实文件系统**的 <see cref="IFileSystem"/> 实现（`System.IO`）。
	/// <para>用途：无头验收/测试宿主（没有 Godot 的 `user://`）以及\"把存档放在可写的任意路径\"的场景；
	/// Godot 运行时仍用 <c>GodotFileSystem</c>。两个实现共用同一套存档语义 ——
	/// 这也是 `WP-3.3` 把\"原子替换\"放进 <see cref="IFileSystem.Move"/> 的原因：**换文件系统不能换掉原子性**。</para>
	/// </summary>
	public sealed class SystemFileSystem : IFileSystem
	{
		public bool Exists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

		public string ReadAllText(string path)
		{
			if (!Exists(path)) return null;
			try
			{
				return File.ReadAllText(path);
			}
			catch (IOException)
			{
				return null; // 读不到（被占用/权限）：按\"无存档\"处理，由上层决定是否提示
			}
		}

		public void WriteAllText(string path, string text)
		{
			EnsureDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, text ?? string.Empty);
		}

		public void EnsureDirectory(string directory)
		{
			if (string.IsNullOrWhiteSpace(directory)) return;
			if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
		}

		public IEnumerable<string> ListFiles(string directory)
		{
			if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return Array.Empty<string>();
			return Directory.GetFiles(directory).Select(Path.GetFileName);
		}

		public void Delete(string path)
		{
			if (Exists(path)) File.Delete(path);
		}

		/// <summary>移动/替换（**原子语义**：同卷内 `Move` 带覆盖 = 文件系统级的替换操作）。</summary>
		public void Move(string source, string destination, bool overwrite)
		{
			if (!Exists(source)) return;
			EnsureDirectory(Path.GetDirectoryName(destination));
			File.Move(source, destination, overwrite);
		}
	}
}
