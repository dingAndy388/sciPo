using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Resources.Domain
{
	public class ResourcesPool
	{
		[JsonProperty]
		public int OwnerId { get; private set; }

		[JsonProperty]
		private Dictionary<string, float> _value = new();
		[JsonProperty]
		private Dictionary<string, float> _limit = new();

		[JsonConstructor]
		private ResourcesPool() { }

		public ResourcesPool(int ownerId)
		{
			OwnerId = ownerId;
		}

		public void InitializeFromConfig(IResourcesPoolConfig config)
		{
			if (config?.Resources == null) return;

			foreach (var resource in config.Resources)
			{
				if (!_value.ContainsKey(resource.Name))
					_value[resource.Name] = resource.BaseValue;
				if (!_limit.ContainsKey(resource.Name))
					_limit[resource.Name] = resource.BaseLimit;
			}
		}

		public float GetValue(string key)
		{
			return _value.GetValueOrDefault(key, 0);
		}

		public void AddValue(string key, float value)
		{
			if (_value.ContainsKey(key))
				_value[key] = Math.Clamp(_value[key] + value, 0, GetLimit(key));
			else
				_value[key] = value;
		}

		public float GetLimit(string key)
		{
			return _limit.GetValueOrDefault(key, 0);
		}

		public void AddLimit(string key, float value)
		{
			if (_limit.ContainsKey(key))
				_limit[key] += value;
			else
				_limit[key] = value;
		}

		/// <summary>
		/// （v0.6.3 / WP-7.2a）**把上限设为精确值**：仓库（`ResourceLimit` 修饰器）的消费点用它 ——
		/// 上限必须按"配置基值 + 当前修正器"**重算**（幂等），而不是每建一座仓库就 `AddLimit` 累加一次
		/// （那样拆掉仓库上限也不会回落，读档几次还会翻倍）。
		/// </summary>
		public void SetLimit(string key, float value)
		{
			_limit[key] = Math.Max(0f, value);

			// 上限下调时把当前存量夹到新上限内（否则会出现"库存 2000 / 上限 1500"的非法状态）
			if (_value.TryGetValue(key, out float current) && current > _limit[key])
				_value[key] = _limit[key];
		}
	}
}