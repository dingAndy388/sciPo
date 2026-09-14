namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.8 / `UNIT-14`）**地图生成后处理器**：生成器（<see cref="IMapGenerator"/>）只产地形，
	/// 依赖"地形已定、实体尚未落位"这一窗口的内容（如按地形概率放置敌方单位）实现本接口。
	/// <para>职责边界：处理器只拿到**聚合根**（<see cref="Map"/>），不接触仓储/会话 —— 生成仍是纯函数式的
	/// "种子 → 地图"，落盘与缓存由 <c>MapAppService.GenerateMap</c> 负责。</para>
	/// <para>实现必须**确定性**：同一 seed 的地图要得到同一结果（因此处理器内部的随机源要从 <c>Map.seed</c> 派生，
	/// 而不是复用一个进程级随机源）。</para>
	/// </summary>
	public interface IMapPostProcessor
	{
		/// <summary>处理器名字（装配自检/日志用）。</summary>
		string Name { get; }

		/// <summary>就地加工刚生成好的地图（可放置占据物，但不应增删格子）。</summary>
		void Process(Map map);
	}
}
