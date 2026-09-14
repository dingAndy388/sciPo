using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / WP-0.3）文件系统抽象：让 Domain / Application 不直接依赖 Godot 的 FileAccess，
	/// 从而可以在无引擎环境下（xUnit）用内存实现替换。
	/// </summary>
	public interface IFileSystem
	{
		bool Exists(string path);

		/// <summary>读取文本；文件不存在时返回 null（不抛异常）。</summary>
		string ReadAllText(string path);

		void WriteAllText(string path, string text);

		/// <summary>确保目录存在（不存在则递归创建）。</summary>
		void EnsureDirectory(string directory);

		/// <summary>列出目录下的文件名（非完整路径）；目录不存在时返回空集合。</summary>
		IEnumerable<string> ListFiles(string directory);

		void Delete(string path);
	}
}
