using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Units.Domain
{
	public class UnitsConfigDto : IUnitsConfig
	{
		/// <summary>
		/// （v0.3 / WP-1.4）必须显式声明 JSON 根键：属性名 <c>UnitsData</c> 与 JSON 键 <c>Units</c> 不匹配，
		/// 缺了本特性会让整张单位表**静默解析成 0 条**（由启动期校验的「表为空」error 抓出）。
		/// </summary>
		[JsonProperty("Units")]
		public Dictionary<string, UnitConfigDto> UnitsData { get; set; }

		Dictionary<string, IUnitConfig> IUnitsConfig.Units
			=> UnitsData?.ToDictionary(kvp => kvp.Key, kvp => (IUnitConfig)kvp.Value)
			   ?? new Dictionary<string, IUnitConfig>();
	}
}
