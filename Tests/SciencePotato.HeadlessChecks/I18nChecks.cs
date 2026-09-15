using SciencePotato.Scripts.Core;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.6.5 / WP-5.10）**多语言（i18n）**的验收检查：键解析、缺键可见、回退、双语键集一致。
	/// <para>为什么要建在 M1（`R4`）：字符串一旦散进 UI 代码，回头抽取等于重写 UI；
	/// 而"两种语言的键集不一致"是双语项目最常见的静默缺陷 —— 它是可以自动验的，就一定要自动验。</para>
	/// </summary>
	internal static class I18nChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-5.10 i18n：真实配置装载中英两套文案", LoadsBothLocales);
			Check.Run("WP-5.10 i18n：键解析 / 参数化 / 缺键可见（⟦key⟧）", ResolvesAndMarksMissing);
			Check.Run("WP-5.10 i18n：缺失语言回退到默认语言", FallsBackToDefaultLocale);
			Check.Run("WP-5.10 i18n：中英两套的键集完全一致（防静默漏译）", KeySetsMatch);
			Check.Run("WP-5.10 i18n：切换语言；未装载语言被拒绝而不是崩溃", LocaleSwitching);
			Check.Run("WP-5.10 i18n：UI 只用键（面板里没有写死文案）", UiUsesKeysOnly);
		}

		private static void LoadsBothLocales()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			II18nService i18n = core.I18n;

			Check.Assert(i18n != null, "组合根应暴露 i18n 服务");
			Check.AssertEqual("en,zh", string.Join(",", i18n.AvailableLocales), "已装载语言（按 Ordinal 升序）");
			Check.AssertEqual("zh", i18n.Locale, "缺省语言");
			Check.AssertEqual(0, i18n.MissingKeys.Count, "装载后不应有缺键");
		}

		private static void ResolvesAndMarksMissing()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			II18nService i18n = core.I18n;

			Check.AssertEqual("生成地图", i18n.T("ui.generate"), "中文键解析");
			Check.Assert(i18n.HasKey("res.Food"), "HasKey 应能查到键");

			// 缺键：**必须看得见**（返回 ⟦key⟧ 并记账），而不是空字符串
			string missing = i18n.T("ui.doesNotExist");
			Check.AssertEqual("⟦ui.doesNotExist⟧", missing, "缺键应返回可见标记");
			Check.Assert(i18n.MissingKeys.Contains("ui.doesNotExist"), "缺键应被记账");
		}

		private static void FallsBackToDefaultLocale()
		{
			// 造一个"en 缺某键"的场景：回退到默认语言 zh
			var tables = new Dictionary<string, Dictionary<string, string>>
			{
				["zh"] = new() { ["a"] = "甲" },
				["en"] = new() { ["b"] = "B" },
			};
			var i18n = new I18nService("zh", tables);

			i18n.SetLocale("en");
			Check.AssertEqual("B", i18n.T("b"), "当前语言命中");
			Check.AssertEqual("甲", i18n.T("a"), "当前语言缺 → 回退默认语言");
			Check.AssertEqual("⟦c⟧", i18n.T("c"), "两边都缺 → 可见标记");
		}

		private static void KeySetsMatch()
		{
			I18nService i18n = I18nService.Load(ConfigFixtures.RealConfigSource(), new[] { "zh", "en" }, "zh");
			Dictionary<string, HashSet<string>> keys = i18n.AvailableLocales.ToDictionary(
				locale => locale,
				locale =>
				{
					// 用一个"只在某语言里存在"的探针不可行（没有枚举接口），因此用已知键集探针：
					// 这里改用配置文件直读的方式（目的就是**不依赖实现**地比对两套键）
					return KeysOf(locale);
				});

			Check.AssertEqual(keys["zh"].Count, keys["en"].Count, "中英键数应一致");
			foreach (string key in keys["zh"].Except(keys["en"]))
				Check.Assert(false, $"en 缺少键：{key}");
			foreach (string key in keys["en"].Except(keys["zh"]))
				Check.Assert(false, $"zh 缺少键：{key}");
		}

		/// <summary>直读配置文件取键集（与实现解耦：即使 i18n 实现换了，这条断言仍然有效）。</summary>
		private static HashSet<string> KeysOf(string locale)
		{
			string path = System.IO.Path.Combine(Check.FindRepoRoot(), "Config", $"Strings.{locale}.json");
			var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
			return new HashSet<string>(((Newtonsoft.Json.Linq.JObject)root["Strings"]).Properties().Select(p => p.Name));
		}

		private static void LocaleSwitching()
		{
			CoreServices core = ConfigFixtures.BuildRealCore();
			II18nService i18n = core.I18n;

			Check.Assert(i18n.SetLocale("en"), "切到 en 应成功");
			Check.AssertEqual("Generate", i18n.T("ui.generate"), "英文键解析");
			Check.AssertEqual("en", i18n.Locale, "当前语言");

			Check.Assert(!i18n.SetLocale("xx"), "未装载语言应被拒绝");
			Check.AssertEqual("en", i18n.Locale, "被拒绝后语言不变");
			Check.Assert(!i18n.SetLocale(null), "null 应被拒绝（不抛异常）");
		}

		/// <summary>
		/// （v0.6.5 / WP-5.10）**UI 只写键**：扫描表现层源码，确认面板文案都是 <c>T("...")</c> 的键，
		/// 而不是写死的中文/英文句子。
		/// <para>为什么用"扫源码"这种土办法：这是**纪律**问题，没有编译期能保证；一条朴素的源码断言
		/// （禁止在表现层出现中文字面量）比"以后注意"有效得多。</para>
		/// </summary>
		private static void UiUsesKeysOnly()
		{
			string scriptsRoot = System.IO.Path.Combine(Check.FindRepoRoot(), "Scripts");
			var uiFiles = System.IO.Directory.GetFiles(scriptsRoot, "*.cs", System.IO.SearchOption.AllDirectories)
				.Where(file => System.IO.Path.GetFileName(file).Contains("Ui"))   // ⚠️ 大小写敏感：`Building…` 里的 "ui" 不是 UI 文件
				.ToArray();

			Check.Assert(uiFiles.Length > 0, "应能找到 UI 源码文件");

			// 只看"面板文案的赋值语句"：`.Text = …` / `.Prefix = …` / `.PlaceholderText = …` 等。
			// 比"禁止一切中文字面量"精确得多 —— 日志与内部提示（如 `GD.Print($"出生点=…")`）不受影响。
			string[] uiTextProperties = { ".Text =", ".Prefix =", ".Suffix =", ".PlaceholderText =", ".TooltipText =", ".Title =" };
			int scanned = 0;

			foreach (string file in uiFiles)
			{
				string text = System.IO.File.ReadAllText(file);
				foreach (string rawLine in text.Split('\n'))
				{
					string line = rawLine;
					int comment = line.IndexOf("//", System.StringComparison.Ordinal);
					if (comment >= 0) line = line.Substring(0, comment);
					line = line.Trim();

					if (!uiTextProperties.Any(p => line.Contains(p))) continue;

					// 只看"赋了字符串字面量"的行：赋代码常量/变量（如 `_id.Text = DefaultMapId;`）不是写死文案
					string right = line.Substring(line.IndexOf('=') + 1);
					if (!right.Contains("\"")) continue;

					scanned++;
					Check.Assert(line.Contains("i18n.T("),
						$"{System.IO.Path.GetFileName(file)} 的面板文案必须走 i18n 键（`i18n.T(\"…\")`）：{line}");
				}
			}

			Check.Assert(scanned > 0, "应至少扫到一条面板文案赋值（否则这条纪律检查形同虚设）");
		}
	}
}
