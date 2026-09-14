using Newtonsoft.Json;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Construction.Infrastructure;
using SciencePotato.Scripts.Events.Infrastructure;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using SciencePotato.Scripts.Resources.Infrastructure;
using SciencePotato.Scripts.TechTree.Infrastructure;
using SciencePotato.Scripts.Units.Infrastructure;
using System;

namespace SciencePotato.Scripts.Core.Config
{
	/// <summary>
	/// （v0.3 / WP-1.4）配置表装载段：把 <see cref="IConfigSource"/> 的 7 份 JSON 文本转成仓库实例。
	/// <para>与 <see cref="ConfigValidator"/> 的分工：**本类只负责「能不能解析出来」**（文件缺失 / JSON 非法 / 根对象形状不符），
	/// 后者负责「解析出来的内容对不对」。两者的结论都写进同一个 <see cref="ConfigReport"/>，使报告成为单一事实来源。</para>
	/// <para>装载失败**不抛异常**：交给 <see cref="CoreBootstrap"/> 按报告统一决策是否快速失败，
	/// 这样测试与工具可以在报告里看到全部问题（而不是只看到第一条）。</para>
	/// </summary>
	internal static class ConfigTableLoader
	{
		public static ConfigTables Load(IConfigSource configSource, ConfigReport report)
		{
			if (configSource == null) throw new ArgumentNullException(nameof(configSource));
			if (report == null) throw new ArgumentNullException(nameof(report));

			return new ConfigTables
			{
				Terrains = LoadTable(configSource, "Terrains", report, json => new TerrainsConfigRepository(json)),
				Resources = LoadTable(configSource, "Resources", report, json => new ResourcesConfigRepository(json)),
				Buildings = LoadTable(configSource, "Buildings", report, json => new BuildingsConfigRepository(json)),
				Units = LoadTable(configSource, "Units", report, json => new UnitsConfigRepository(json)),
				TechTrees = LoadTable(configSource, "TechTrees", report, json => new TechTreesConfigRepository(json)),
				Events = LoadTable(configSource, "Events", report, json => new EventConfigRepository(json)),
				Generator = LoadTable(configSource, "Generator", report, json => JsonConvert.DeserializeObject<GeneratorConfigDto>(json)),
			};
		}

		private static T LoadTable<T>(IConfigSource configSource, string tableName, ConfigReport report, Func<string, T> factory)
			where T : class
		{
			string json;
			try
			{
				json = configSource.LoadText(tableName);
			}
			catch (Exception ex)
			{
				report.Error(tableName, null, $"读取配置表失败：{ex.GetType().Name}：{ex.Message}");
				return null;
			}

			if (string.IsNullOrWhiteSpace(json))
			{
				report.Error(tableName, null,
					$"缺少配置表 Config/{tableName}.json（v0.3 / WP-1.4 起 7 张表统一由 JSON 提供；" +
					$"user://Config/{tableName}.json 可覆写）");
				return null;
			}

			try
			{
				T table = factory(json);
				if (table == null)
				{
					report.Error(tableName, null, "解析结果为空（根对象形状可能与 DTO 不符）");
					return null;
				}
				return table;
			}
			catch (JsonException ex)
			{
				report.Error(tableName, null, $"JSON 解析失败：{ex.Message}");
				return null;
			}
			catch (Exception ex)
			{
				report.Error(tableName, null, $"装载失败：{ex.GetType().Name}：{ex.Message}");
				return null;
			}
		}
	}
}
