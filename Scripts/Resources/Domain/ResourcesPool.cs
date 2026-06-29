using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Resources.Domain
{
	public class ResourcesPool(int owner)
	{
		public readonly int OwnerId = owner;

		private Dictionary<string, float> _value = new Dictionary<string, float>();
		private Dictionary<string, float> _limit = new Dictionary<string, float>();

		//get value of resource
		public float GetValue(string key)
		{
			return _value.GetValueOrDefault(key, 0);
		}

		//change value of resource
		public void AddValue(string key, float value)
		{
			_value.Add(key, value);
		}

		//get limit of resource
		public float GetLimit(string key)
		{
			return _limit.GetValueOrDefault(key, 0);
		}

		//change limit of resource
		public void AddLimit(string key, float value)
		{
		}
	}
}
