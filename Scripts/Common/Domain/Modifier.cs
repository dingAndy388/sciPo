using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	//DTO of modifers from config
	public struct Modifier
	{
		public string Target { get; set; }
		public string Type { get; set; }
		public float Value { get; set; }
	}
}
