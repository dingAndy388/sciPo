using Newtonsoft.Json.Linq;
using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.6.5 / WP-5.10）**多语言（i18n）**：UI 文案外置成键 → 文本，代码里只出现键。
	/// <para>为什么排进 M1（而不是"等 UI 做完再说"，`R4`）：字符串一旦散进 UI 代码，回头抽取等于重写 UI。
	/// 本 WP 只建**框架 + 缺键可见 + 双语键集一致**这三件地基，正式文案规模归 `WP-8.3`。</para>
	/// </summary>
	public interface II18nService
	{
		/// <summary>当前语言（如 <c>zh</c> / <c>en</c>）。</summary>
		string Locale { get; }

		/// <summary>已装载的语言（升序）。</summary>
		IReadOnlyList<string> AvailableLocales { get; }

		/// <summary>
		/// 取文案：当前语言 → 缺则回退到默认语言 → 再缺则记入 <see cref="MissingKeys"/> 并返回**可见的缺键标记**。
		/// <para>缺键必须"看得见"（`⟦key⟧`），否则玩家看到空字符串、开发看到一片空白，两边都不知道少了什么。</para>
		/// </summary>
		string T(string key, params object[] args);

		/// <summary>键是否存在（任意已装载语言里）。</summary>
		bool HasKey(string key);

		/// <summary>运行时实际缺失过的键（自检 / 用例断言 / 8.3 的文案清单都用它）。</summary>
		IReadOnlyCollection<string> MissingKeys { get; }

		/// <summary>切换语言；语言未装载返回 <c>false</c>（不抛异常：UI 切语言不该让游戏崩）。</summary>
		bool SetLocale(string locale);
	}

	/// <summary>（v0.6.5 / WP-5.10）缺键标记：**刻意用罕见字符**，避免和正常文案混淆。</summary>
	public static class I18nMarkers
	{
		public const string MissingPrefix = "⟦";
		public const string MissingSuffix = "⟧";

		public static string Missing(string key) => $"{MissingPrefix}{key}{MissingSuffix}";
	}

	/// <summary>
	/// （v0.6.5 / WP-5.10）`II18nService` 的实现：不可变字典 + 回退链 + 缺键记账。
	/// <para>纯 C#（不依赖 Godot）：无头用例可以直接验"键集一致 / 缺键可见 / 回退正确"。</para>
	/// </summary>
	public sealed class I18nService : II18nService
	{
		private readonly Dictionary<string, Dictionary<string, string>> _tables;
		private readonly List<string> _missing = new();

		public I18nService(string defaultLocale, Dictionary<string, Dictionary<string, string>> tables)
		{
			_tables = tables ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
			DefaultLocale = string.IsNullOrWhiteSpace(defaultLocale) ? _tables.Keys.FirstOrDefault() ?? "zh" : defaultLocale.Trim();
			Locale = _tables.ContainsKey(DefaultLocale) ? DefaultLocale : _tables.Keys.FirstOrDefault() ?? DefaultLocale;
		}

		/// <summary>默认语言（回退终点）。</summary>
		public string DefaultLocale { get; }

		public string Locale { get; private set; }

		public IReadOnlyList<string> AvailableLocales
			=> _tables.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

		public IReadOnlyCollection<string> MissingKeys => _missing.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();

		public bool SetLocale(string locale)
		{
			if (string.IsNullOrWhiteSpace(locale)) return false;
			if (!_tables.ContainsKey(locale.Trim())) return false;

			Locale = locale.Trim();
			return true;
		}

		public bool HasKey(string key)
			=> !string.IsNullOrWhiteSpace(key) && _tables.Values.Any(table => table.ContainsKey(key));

		public string T(string key, params object[] args)
		{
			if (string.IsNullOrWhiteSpace(key)) return string.Empty;

			string text = Lookup(Locale, key) ?? Lookup(DefaultLocale, key);
			if (text == null)
			{
				if (!_missing.Contains(key)) _missing.Add(key);
				return I18nMarkers.Missing(key);
			}

			// 参数化：只有给了参数才做格式化（避免文案里的 {} 被当占位符炸掉）
			if (args == null || args.Length == 0) return text;

			try { return string.Format(text, args); }
			catch (FormatException) { return text; }
		}

		private string Lookup(string locale, string key)
			=> _tables.TryGetValue(locale, out Dictionary<string, string> table) && table.TryGetValue(key, out string text) ? text : null;

		/// <summary>
		/// （v0.6.5 / WP-5.10）从配置来源装载若干语言：`Config/Strings.{locale}.json` →
		/// <c>{ "Culture": "zh", "Strings": { "ui.generate": "生成地图", ... } }</c>。
		/// <para>装载失败的语言**跳过并记 warning**（不阻断启动）：少一种语言不该让游戏开不了。</para>
		/// </summary>
		public static I18nService Load(IConfigSource source, IEnumerable<string> locales, string defaultLocale, Action<string> warn = null)
		{
			var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
			if (source == null) return new I18nService(defaultLocale, tables);

			foreach (string locale in locales ?? Enumerable.Empty<string>())
			{
				if (string.IsNullOrWhiteSpace(locale)) continue;

				string json = source.LoadText($"Strings.{locale.Trim()}");
				if (string.IsNullOrWhiteSpace(json))
				{
					warn?.Invoke($"[I18n] 缺少 Config/Strings.{locale}.json（该语言不可用）");
					continue;
				}

				try
				{
					var root = JObject.Parse(json);
					var strings = root["Strings"] as JObject;
					if (strings == null)
					{
						warn?.Invoke($"[I18n] Config/Strings.{locale}.json 缺少 `Strings` 对象");
						continue;
					}

					tables[locale.Trim()] = strings.Properties()
						.ToDictionary(p => p.Name, p => p.Value?.ToString() ?? string.Empty, StringComparer.Ordinal);
				}
				catch (Exception ex)
				{
					warn?.Invoke($"[I18n] Config/Strings.{locale}.json 解析失败：{ex.Message}");
				}
			}

			return new I18nService(defaultLocale, tables);
		}
	}
}
