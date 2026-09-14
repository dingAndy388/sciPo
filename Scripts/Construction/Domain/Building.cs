using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Domain
{
	/// <summary>
	/// （v0.3 / WP-2.5）建筑聚合：占据物信息 + **训练队列**。
	/// <para>队列口径（设计稿）：上限 <see cref="TrainingQueueLimit"/>（默认 5，来自配置）、**同时只训练 1 个**（队列头）。
	/// 排队不生成单位对象，只锁定单位 uid（见 <see cref="TrainingOrder"/>）；完成时由应用服务落位。</para>
	/// </summary>
	public class Building(HexCubePosition coord, string id, string uid, int ownerId, string name, int trainingQueueLimit = BuildingConfigDto.DefaultTrainingQueueLimit) : IMapOccupant
	{
		private MapOccupantInfo info;

		private readonly HexCubePosition _pos = coord;
		private readonly string _uid = uid;
		private readonly int _ownerId = ownerId;
		private readonly List<TrainingOrder> _trainingQueue = new();
		private string _id = id;
		private string _name = name;

		public bool IsReady { get; set; } = false;

		/// <summary>训练队列上限（配置 `TrainingQueueLimit`）。</summary>
		public int TrainingQueueLimit { get; } = trainingQueueLimit > 0 ? trainingQueueLimit : BuildingConfigDto.DefaultTrainingQueueLimit;

		/// <summary>当前队列（只读视图；顺序 = 训练顺序）。</summary>
		public IReadOnlyList<TrainingOrder> TrainingQueue => _trainingQueue;

		public int TrainingQueueCount => _trainingQueue.Count;

		/// <summary>是否已有订单在训练中（同时只训练 1 个）。</summary>
		public bool HasActiveTraining => _trainingQueue.Any(order => order.IsActive);

		/// <summary>（v0.3 / WP-2.4）当前绑定的建造者（null = 无建造者 / 已释放）。</summary>
		public BuilderBinding BuilderBinding { get; private set; }

		/// <summary>
		/// （v0.3 / WP-2.4）绑定建造者：**每建筑同时仅 1 个**（`D6`）。
		/// <para>传 <c>null</c> 表示"无建造者开工"（脚本/测试/未来"无需工人的建筑"），不算占用冲突。</para>
		/// </summary>
		/// <returns>是否绑定成功（已被占用则 false）。</returns>
		public bool TryBindBuilder(BuilderBinding binding)
		{
			if (binding == null) return true;
			if (BuilderBinding != null) return false;

			BuilderBinding = binding;
			return true;
		}

		/// <summary>（v0.3 / WP-2.4）释放建造者（完工或拆除时调用）：把单位从"忙"恢复为空闲。</summary>
		public void ReleaseBuilder()
		{
			BuilderBinding?.Release();
			BuilderBinding = null;
		}

		/// <summary>
		/// （v0.3 / WP-2.6）**升级**：把建筑换成目标等级的配置。
		/// <para>**uid 不变** —— 修正器/迷雾/任务/易主（`WP-4.8`）都以 uid 为锚，换 uid 会让这些引用全部失联；
		/// 升级只改 Id/Name，等级参数（人口三件套、产出、视野、可训练名单）由新配置在读侧生效。</para>
		/// </summary>
		public void ApplyUpgrade(string buildingId, string name)
		{
			_id = buildingId;
			_name = name;
			info = new MapOccupantInfo(_pos, _id, _uid, _ownerId, _name, IsReady, -1f, OccupantType.Building);
		}

		public MapOccupantInfo GetInfo()
		{
			info = new MapOccupantInfo(_pos, _id,_uid,_ownerId,_name, IsReady, -1f, OccupantType.Building);
			return info;
		}

		/// <summary>入队（队列满则拒绝）。返回是否入队成功。</summary>
		public bool TryEnqueueTraining(TrainingOrder order)
		{
			if (order == null || _trainingQueue.Count >= TrainingQueueLimit) return false;

			_trainingQueue.Add(order);
			return true;
		}

		/// <summary>取队列头（下一个该处理/在训的订单）。</summary>
		public TrainingOrder PeekTraining() => _trainingQueue.Count > 0 ? _trainingQueue[0] : null;

		/// <summary>移除队列头（订单完成或作废）。</summary>
		public bool DequeueTraining()
		{
			if (_trainingQueue.Count == 0) return false;

			_trainingQueue.RemoveAt(0);
			return true;
		}
	}
}
