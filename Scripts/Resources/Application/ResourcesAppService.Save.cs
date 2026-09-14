using SciencePotato.Scripts.Common.Application;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Resources.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Resources.Application
{
	/// <summary>
	/// （v0.3 / WP-3.2）**资源月结任务的读档恢复**。
	/// <para>恢复口径与建筑/单位一致：**按配置重建周期任务 + 回填进度**（任务闭包不可序列化）。
	/// 进度键 = `TaskSnapshot.BuildKey("ResourceGrowth", "none", 资源名)`。</para>
	/// </summary>
	public partial class ResourcesAppService
	{
		public int RestoreGrowthTasks(string mapId, int ownerId, IReadOnlyDictionary<string, float> progressByResource)
		{
			var config = _configRepo.GetResourcesPoolConfig();
			if (config?.Resources == null) return 0;

			int restored = 0;
			foreach (IResourceConfig resource in config.Resources)
			{
				float intervalDays = resource.GrowInterval > 0 ? resource.GrowInterval : 0f;
				if (intervalDays <= 0f) continue;

				float progress = 0f;
				progressByResource?.TryGetValue(TaskSnapshot.BuildKey("ResourceGrowth", "none", resource.Name), out progress);

				StartGrowthTask(mapId, ownerId, resource, intervalDays, progress);
				restored++;
			}

			return restored;
		}
	}
}