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
		private readonly string _id = id;
		private readonly string _uid = uid;
		private readonly int _ownerId = ownerId;
		private readonly string _name = name;
		private readonly List<TrainingOrder> _trainingQueue = new();

		public bool IsReady { get; set; } = false;

		/// <summary>训练队列上限（配置 `TrainingQueueLimit`）。</summary>
		public int TrainingQueueLimit { get; } = trainingQueueLimit > 0 ? trainingQueueLimit : BuildingConfigDto.DefaultTrainingQueueLimit;

		/// <summary>当前队列（只读视图；顺序 = 训练顺序）。</summary>
		public IReadOnlyList<TrainingOrder> TrainingQueue => _trainingQueue;

		public int TrainingQueueCount => _trainingQueue.Count;

		/// <summary>是否已有订单在训练中（同时只训练 1 个）。</summary>
		public bool HasActiveTraining => _trainingQueue.Any(order => order.IsActive);

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
