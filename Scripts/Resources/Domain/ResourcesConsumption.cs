using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Resources.Domain
{
	//implement of consumable contract in resource system
	public class ResourcesConsumption : IConsumable
	{
		private readonly ResourcesPool _pool;
		private readonly string _type;
		private readonly float _amount;

		public ResourcesConsumption(ResourcesPool pool, string type, float amount)
		{
			_pool = pool;
			_type = type;
			_amount = amount;
		}

		public bool IsConsumable()
		{
			return _pool.GetValue(_type)>=_amount;
		}

		public void Consume()
		{
			_pool.AddValue(_type, -_amount);
		}
	}
}
