using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	public struct Consumption (string type, float amount)
	{
		public readonly string Type = type;
		public readonly float Amount = amount;
	}
}
