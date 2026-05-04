using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Resources.Domain
{
	public class ResourcesPool
	{
		private Dictionary<string, float> _value = new Dictionary<string, float>();
		private Dictionary<string, float> _limit = new Dictionary<string, float>();

		public float GetValue(string key)
		{
			return _value.GetValueOrDefault(key, 0);
		}

		public void AddValue(string key, float value)
		{
			_value.Add(key, value);
		}

		public float GetLimit(string key)
		{
			return _limit.GetValueOrDefault(key, 0);
		}

		public void AddLimit(string key, float value)
		{
			_limit.Add(key, value);
		}
	}
}
