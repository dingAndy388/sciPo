using Godot;
using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-0.3）基于 Godot FileAccess / DirAccess 的 IFileSystem 实现。
	/// 支持 res://（只读打包资源）与 user://（可写用户目录）。
	/// </summary>
	public class GodotFileSystem : IFileSystem
	{
		public bool Exists(string path)
		{
			return FileAccess.FileExists(path);
		}

		public string ReadAllText(string path)
		{
			if (!FileAccess.FileExists(path)) return null;
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			return file?.GetAsText();
		}

		public void WriteAllText(string path, string text)
		{
			EnsureDirectory(DirectoryOf(path));
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
			file?.StoreString(text ?? string.Empty);
		}

		/// <summary>
		/// 取路径的目录部分（**必须是合法的 Godot 路径**）。
		/// <para>为什么要单独做：`System.IO.Path.GetDirectoryName("user://a/b.json")` 在 Windows 上得到
		/// `user:\a` —— 再交给 Godot 的目录 API 就变成了不存在的目录（`res://`/`user://` 的根目录也不该创建）。
		/// v0.3 / WP-3.3 起存档单元要靠它保证\"写入前目录存在\"。</para>
		/// </summary>
		private static string DirectoryOf(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) return null;

			string normalized = path.Replace('\\', '/');
			int slash = normalized.LastIndexOf('/');
			if (slash <= 0) return null;

			string directory = normalized.Substring(0, slash);
			// `user://` / `res://` / `user:` 这种根前缀：无目录可建（创建它只会得到引擎报错）
			return directory.EndsWith("://", System.StringComparison.Ordinal) || directory.EndsWith(":", System.StringComparison.Ordinal)
				? null
				: directory;
		}

		public void EnsureDirectory(string directory)
		{
			if (string.IsNullOrWhiteSpace(directory)) return;
			if (!DirAccess.DirExistsAbsolute(directory))
				DirAccess.MakeDirRecursiveAbsolute(directory);
		}

		public IEnumerable<string> ListFiles(string directory)
		{
			if (!DirAccess.DirExistsAbsolute(directory)) yield break;
			using var dir = DirAccess.Open(directory);
			if (dir == null) yield break;
			foreach (string name in dir.GetFiles())
				yield return name;
		}

		public void Delete(string path)
		{
			if (!FileAccess.FileExists(path)) return;
			DirAccess.RemoveAbsolute(path);
		}

		/// <summary>
		/// （v0.3 / WP-3.3）移动/替换：Godot 的 <c>DirAccess.RenameAbsolute</c> 在目标存在时**会覆盖**
		/// （同一个卷内是文件系统级替换），因此它满足存档原子写的要求。
		/// </summary>
		public void Move(string source, string destination, bool overwrite)
		{
			if (!FileAccess.FileExists(source)) return;

			if (!overwrite && FileAccess.FileExists(destination)) return;

			EnsureDirectory(DirectoryOf(destination));
			DirAccess.RenameAbsolute(source, destination);
		}
	}
}
