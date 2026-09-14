namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.3 / WP-1.3）地图生成器配置的纯 C# 默认实现：
	/// 无头环境（测试 / 离线结算）没有 Godot 的 <c>.tres</c> 资源，用本类作为回退值，
	/// 使 <c>VoronoiMapGenerator</c> 不再硬依赖 <c>ResourceLoader</c>。
	/// </summary>
	public class GeneratorConfigDto : IMapGeneratorConfig
	{
		/// <summary>锚点密度（与 <c>Config/Generator/Generator.tres</c> 的默认值一致）。</summary>
		public float Density { get; set; } = 4f;
	}
}
