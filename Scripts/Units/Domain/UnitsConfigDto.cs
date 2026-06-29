using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Units.Domain
{
	public class UnitsConfigDto : IUnitsConfig
	{
		public Dictionary<string, UnitConfigDto> UnitsData { get; set; }

		Dictionary<string, IUnitConfig> IUnitsConfig.Units
			=> UnitsData?.ToDictionary(kvp => kvp.Key, kvp => (IUnitConfig)kvp.Value)
			   ?? new Dictionary<string, IUnitConfig>();
	}
}