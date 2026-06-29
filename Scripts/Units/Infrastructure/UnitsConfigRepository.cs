using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Units.Domain;

namespace SciencePotato.Scripts.Units.Infrastructure
{
	public class UnitsConfigRepository : GenericConfigRepository<IUnitsConfig, UnitsConfigDto>, IUnitsRepository
	{
		private IUnitsConfig _config;

		public UnitsConfigRepository(string json) : base(json)
		{
			base.Load();
			_config = base.Data;
		}

		public IUnitConfig GetUnitConfig(string unitId)
		{
			if (_config?.Units == null) return null;
			_config.Units.TryGetValue(unitId, out var unitConfig);
			return unitConfig;
		}
	}
}