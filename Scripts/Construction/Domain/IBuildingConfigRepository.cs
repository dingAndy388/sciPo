using System.Collections.Generic;

namespace SciencePotato.Scripts.Construction.Domain
{
	public interface IBuildingConfigRepository
	{
		IBuildingConfig GetBuildingConfig(string buildingId);

		/// <summary>（v0.3 / WP-1.4）枚举全部建筑配置：启动期校验（表非空 / Id 一致性 / 引用完整性）需要全表视图。</summary>
		IEnumerable<IBuildingConfig> GetAll();
	}
}

