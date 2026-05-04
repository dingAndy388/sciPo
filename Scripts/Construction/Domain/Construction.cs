using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Construction.Domain
{
	public class Construction
	{
		public Building Build(HexCubePosition coord,int ID, long uid, string name)
		{
			return new Building(coord,ID,uid,name);
		}
	}
}
