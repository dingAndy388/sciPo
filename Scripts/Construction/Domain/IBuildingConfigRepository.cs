using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Domain
{
	public interface IBuildingConfigRepository
	{
		IBuildingConfig GetBuildingConfig(string buildingId);
	}
}
