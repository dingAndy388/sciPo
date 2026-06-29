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
	}
}