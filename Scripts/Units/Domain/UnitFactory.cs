using SciencePotato.Scripts.Common.Domain;
using System;

namespace SciencePotato.Scripts.Units.Domain
{
	public class UnitFactory(IUnitsRepository repo)
	{
		private readonly IUnitsRepository _repo = repo;

		public Unit CreateUnit(string unitId, HexCubePosition position, int ownerId)
		{
			var config = _repo.GetUnitConfig(unitId);
			if (config == null) return null;

			return new Unit(position, config.UnitId, Guid.NewGuid().ToString(), ownerId, config.UnitId,
				config.HP, config.Movement, config.Attack, config.Movement, true,
				config.MoveRechargePerTick, config.AttackRadius, config.AttackDamage);
		}
	}
}