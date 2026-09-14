using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>（v0.3 / WP-0.2）极简断言与运行框架：无外部依赖，返回码即验收结果。</summary>
	internal static class Check
	{
		private static readonly List<string> Failures = new();
		private static int _passed;

		public static void Run(string name, Action body)
		{
			try
			{
				body();
				_passed++;
				Console.WriteLine($"[PASS] {name}");
			}
			catch (Exception ex)
			{
				Failures.Add(name);
				Console.WriteLine($"[FAIL] {name}");
				Console.WriteLine($"       {ex.Message}");

				// 定位用：只打第一帧（harness 要能指出"哪一行抛的"，否则用例只能靠猜）
				string frame = ex.StackTrace?.Split('\n').FirstOrDefault();
				if (!string.IsNullOrWhiteSpace(frame)) Console.WriteLine($"       @{frame.Trim()}");
			}
		}

		public static void Assert(bool condition, string message)
		{
			if (!condition) throw new Exception(message);
		}

		public static void AssertEqual<T>(T expected, T actual, string message)
		{
			if (!EqualityComparer<T>.Default.Equals(expected, actual))
				throw new Exception($"{message}：期望 <{expected}>，实际 <{actual}>");
		}

		public static int Summary()
		{
			Console.WriteLine($"\n结果：通过 {_passed} / 失败 {Failures.Count}");
			if (Failures.Count > 0)
			{
				Console.WriteLine("失败项：");
				foreach (string f in Failures) Console.WriteLine($"  - {f}");
			}
			return Failures.Count == 0 ? 0 : 1;
		}

		/// <summary>从运行目录向上查找仓库根（以主工程文件为标记）。</summary>
		public static string FindRepoRoot()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Science Potato.csproj")))
				dir = dir.Parent;
			if (dir == null) throw new Exception("未找到仓库根目录（缺少 Science Potato.csproj）");
			return dir.FullName;
		}
	}
}
