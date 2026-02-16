using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Domain
{
	public interface IMapRepository
	{
		public void SaveMap(Map map);
		public Map LoadMap(string path);
		public void DeleteMap(Map map);
	}
}
