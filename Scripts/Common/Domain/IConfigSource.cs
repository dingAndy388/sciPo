namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / WP-0.3）配置表文本来源：按表名（如 "Buildings"）返回 JSON 文本。
	/// 这是"配置表 → DTO"管道的入口段，补齐了 WIRE-03 缺失的加载入口。
	/// </summary>
	public interface IConfigSource
	{
		/// <summary>返回配置表 JSON 文本；找不到时返回 null（不抛异常）。</summary>
		string LoadText(string configName);
	}
}
