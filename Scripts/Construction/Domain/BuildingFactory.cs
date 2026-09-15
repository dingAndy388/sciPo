using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Domain
{
	public class BuildingFactory(IBuildingConfigRepository repo)
	{
		private IBuildingConfigRepository _repo = repo;

		/// <summary>
		/// 创建建筑。
		/// <para>（v0.3 / WP-3.2）<paramref name="uid"/> 与 <paramref name="isReady"/> 可显式指定：读档必须**保留原 uid**
		/// （修正器/迷雾/任务/建造者绑定都以它为锚）并恢复"是否已完工"。</para>
		/// </summary>
		public Building CreateBuilding(string buildingId, HexCubePosition position, int ownerId, string uid = null, bool isReady = false)
		{
			var config = _repo.GetBuildingConfig(buildingId);
			if (config == null) return null;

			// v0.3 / WP-2.5：训练队列上限随配置进建筑实例（默认 5，见 BuildingConfigDto）
			// v0.7.0 / WP-4.8：HP 模型随配置进建筑实例（`HasHP=false` ⇒ hp 传 0 = 免疫伤害）
			float hp = config.HasHP && config.HP > 0f ? config.HP : 0f;
			return new Building(position, config.BuildingId,
				string.IsNullOrWhiteSpace(uid) ? Guid.NewGuid().ToString() : uid,
				ownerId, config.Name, config.TrainingQueueLimit, hp) { IsReady = isReady };
		}
	}
}
