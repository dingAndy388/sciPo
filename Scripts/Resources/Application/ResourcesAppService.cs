using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Resources.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Resources.Application
{
	public class ResourcesAppService(ResourcesPool pool)
	{
		private ResourcesPool _pool = pool;

		public IConsumable CreateResourceConsumption(Consumption consumption)
		{
			return new ResourcesConsumption(_pool,consumption.Type,consumption.Amount);
		}

		public void AddResource(string type, float amount)
		{
			_pool.AddValue(type, amount);
		}	
	}
}
