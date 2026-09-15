using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Resources.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Resources.Application
{
	/// <summary>
	/// （v0.3 / WP-3.7 / `E20`）<see cref="ILootSink"/> 的实现：把战利品写进「图 + 所有者」的资源池。
	/// <para>与 Map 侧实现 `IPopulationSink`（`D62`）同一个思路：契约在 `Common/Domain`，
	/// 实现落在**持有该状态**的模块里（人口归 Map、资源池归 Resources）。
	/// 这里没有新的玩法规则 —— 只是一次 `ResourcesPool.AddValue` + 落盘。</para>
	/// </summary>
	public partial class ResourcesAppService : ILootSink
	{
		/// <inheritdoc/>
		/// <remarks>
		/// **上限口径**：沿用 <see cref="ResourcesPool.AddValue"/> 的夹取（掉落不破仓储上限）。
		/// 因此返回值是逐项**前值/后值之差**（实际入池量），而不是传入的掉落表 ——
		/// 池满时调用方看到的是 0 而不是"以为拿到了"。
		/// </remarks>
		public Dictionary<string, float> GrantLoot(string mapId, int ownerId, IReadOnlyDictionary<string, float> rewards)
		{
			var granted = new Dictionary<string, float>();

			// 空表/空引用：连池子都不必要创建（敌方阵营万一被误传也不会凭空多出一个池）
			if (rewards == null || rewards.Count == 0) return granted;

			ResourcesPool pool = GetOrCreatePool(mapId, ownerId);

			foreach (KeyValuePair<string, float> reward in rewards)
			{
				if (reward.Value <= 0f) continue; // 0/负数量是配置噪声，不是掉落

				float before = pool.GetValue(reward.Key);
				pool.AddValue(reward.Key, reward.Value);
				granted[reward.Key] = pool.GetValue(reward.Key) - before;
			}

			if (granted.Count > 0) _repo.SaveResources(mapId, ownerId, pool);
			return granted;
		}
	}
}
