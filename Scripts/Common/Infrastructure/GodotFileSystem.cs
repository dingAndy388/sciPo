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
			EnsureDirectory(System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'));
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
			file?.StoreString(text ?? string.Empty);
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
	}
}
