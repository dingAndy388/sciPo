using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	public interface IMapOccupant
	{
		bool IsReady { get; set; }
		MapOccupantInfo GetInfo();
	}
}
