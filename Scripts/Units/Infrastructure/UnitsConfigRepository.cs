using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Units.Domain;
using System.Collections.Generic;
using System.Linq;

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

		/// <summary>（v0.3 / WP-1.4）全表视图：供启动期校验（表非空 / Id 一致性 / 引用完整性）使用。</summary>
		public IEnumerable<IUnitConfig> GetAll()
			=> _config?.Units?.Values ?? Enumerable.Empty<IUnitConfig>();
	}
}