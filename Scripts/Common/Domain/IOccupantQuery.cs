using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.8.5 / `WP-4.5`）**占据物只读查询**：让"只想知道图上有哪些建筑/单位"的服务（月结产出浮动、
	/// 将来的产出归因 `WP-4.13`）不必依赖整个 <c>MapAppService</c>。
	/// <para>为什么单独抽一个接口：`MonthlySettlementService` 在 Resources 模块，而 `MapAppService` 在 Map 模块；
	/// 直接依赖具体类会把两个模块的方向绑死。`MapAppService` 实现本接口即可（它已有 `GetOccupants`）。</para>
	/// </summary>
	public interface IOccupantQuery
	{
		IEnumerable<IMapOccupant> GetOccupants(string mapId);
	}
}
