using SciencePotato.Scripts.Common.Domain;
using System;

namespace SciencePotato.Scripts.Units.Domain
{
	public class UnitFactory(IUnitsRepository repo)
	{
		private readonly IUnitsRepository _repo = repo;

		/// <summary>
		/// 生成单位实例。
		/// <para>（v0.3 / WP-2.5）<paramref name="uid"/> 可显式指定：训练队列在**入队时**就锁定单位 uid
		/// （任务键与落位都靠它），完成训练时用同一个 uid 建对象。省略则生成新 Guid（敌方刷新 / 调试用）。</para>
		/// </summary>
		public Unit CreateUnit(string unitId, HexCubePosition position, int ownerId, string uid = null)
		{
			var config = _repo.GetUnitConfig(unitId);
			if (config == null) return null;

			return new Unit(position, config.UnitId, string.IsNullOrWhiteSpace(uid) ? Guid.NewGuid().ToString() : uid, ownerId, config.UnitId,
				config.HP, config.Movement, config.Attack, config.Movement, true,
				config.AttackRadius, config.AttackDamage, config.IsHostile);
		}
	}
}