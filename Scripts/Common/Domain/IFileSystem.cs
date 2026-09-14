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

		/// <summary>
		/// （v0.3 / WP-3.3）**移动/替换文件**：存档的原子写靠它（先写 `*.tmp` 再替换正式文件）。
		/// <para>要求实现是**文件系统级**的移动：要么完整替换、要么什么都不发生，
		/// 不允许\"先删再拷\"那种在半途留下空档的做法（否则\"原子性\"等于没有）。</para>
		/// </summary>
		void Move(string source, string destination, bool overwrite);

		void Delete(string path);
	}
}
