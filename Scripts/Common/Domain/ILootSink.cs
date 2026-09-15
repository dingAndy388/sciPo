using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.7 / `E20`）**战利品入池口**：玩法侧算出「这次该给谁多少资源」，
	/// 由持有资源池的模块（Resources）真正写进池子。
	/// <para>为什么需要这条契约：掉落是**战斗结果**（Units 模块），落点却是**资源池**（Resources 模块）。
	/// 按 `D59` 的依赖方向 `Units → Resources` 虽然成立，但战斗引擎（`UnitCombatService`）刻意不认识
	/// 资源池（它只认识地图、配置、时间与事件）—— 于是照抄 `IPopulationSink`（`D62`）的手法：
	/// 接口放 `Common/Domain`，Resources 侧实现并注入（装配处 = `UnitsAppService`，它本来就持有资源服务）。</para>
	/// <para>实现方的义务（写进契约，避免两处口径漂移）：① 数量 ≤ 0 的条目不写入；② **不破池上限**
	/// （沿用 `ResourcesPool.AddValue` 的夹取口径 —— 掉落是收益，不该绕过仓储上限）；③ 有变化就落盘
	/// （`SaveResources`，掉落必须能被存档点记录）；④ 返回**实际入池量** `{ 资源: 实际增加 }` ——
	/// 掉落被上限吃掉时调用方才有据可查（`D25`），不要静默丢弃。</para>
	/// </summary>
	public interface ILootSink
	{
		/// <summary>
		/// 把一次掉落写进 <paramref name="ownerId"/> 的资源池。
		/// </summary>
		/// <param name="mapId">地图 Id（资源池按「图 + 所有者」分池）。</param>
		/// <param name="ownerId">受益者（= 击杀者所有者；敌方阵营不会有掉落进来）。</param>
		/// <param name="rewards">掉落表（资源名 → 数量，来源 = `IUnitConfig.DropReward`）。</param>
		/// <returns>每种资源**实际**增加的数量（被池上限裁剪后的净值；表为空 → 空表）。</returns>
		Dictionary<string, float> GrantLoot(string mapId, int ownerId, IReadOnlyDictionary<string, float> rewards);
	}
}
