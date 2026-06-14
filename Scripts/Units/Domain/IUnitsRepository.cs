using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Units.Domain
{
	public interface IUnitsRepository
	{
		IUnitConfig GetUnitConfig(string unitId);
	}
}
