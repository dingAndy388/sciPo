using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace SciencePotato.Scripts.TechTree.Domain
{
	/// <summary>
	/// （v0.3 / WP-2.1）<see cref="TechPrerequisite"/> 的 JSON **兼容层**：同时接受三种写法
	/// <list type="bullet">
	/// <item><c>"mathematics"</c> —— 旧表写法，解释为「本树节点」（§13.z / D23 兼容层要求，旧表不破）</item>
	/// <item><c>"math:counting"</c> —— 紧凑跨树写法（跨树前置的最小手写形态）</item>
	/// <item><c>{ "TreeId": "science", "NodeId": "counting" }</c> —— 结构体写法（§15 字段规格）</item>
	/// </list>
	/// <para>非法形态**不在这里抛异常**：空串 / 数字 / 缺字段一律照原样带进领域对象，由
	/// <c>ConfigValidator</c> 逐条报 error —— 否则填表者只会看到"整表解析失败"，无法定位。</para>
	/// </summary>
	public sealed class TechPrerequisiteJsonConverter : JsonConverter
	{
		private const char TreeSeparator = ':';

		public override bool CanConvert(Type objectType) => objectType == typeof(TechPrerequisite);

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null) return null;

			if (reader.TokenType == JsonToken.String)
			{
				string raw = (string)reader.Value;
				if (string.IsNullOrWhiteSpace(raw)) return TechPrerequisite.InTree(raw);

				int separator = raw.IndexOf(TreeSeparator);
				return separator < 0
					? TechPrerequisite.InTree(raw.Trim())
					: TechPrerequisite.Cross(raw.Substring(0, separator).Trim(), raw.Substring(separator + 1).Trim());
			}

			if (reader.TokenType == JsonToken.StartObject)
			{
				JObject json = JObject.Load(reader);
				return TechPrerequisite.Cross(
					(string)json["TreeId"] ?? (string)json["treeId"] ?? (string)json["Tree"],
					(string)json["NodeId"] ?? (string)json["nodeId"] ?? (string)json["Node"]);
			}

			// 其它形态（数字/数组…）：保留原文交给校验器报错
			return TechPrerequisite.InTree(reader.Value?.ToString());
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			if (value is TechPrerequisite prerequisite) writer.WriteValue(prerequisite.ToString());
			else writer.WriteNull();
		}
	}
}
