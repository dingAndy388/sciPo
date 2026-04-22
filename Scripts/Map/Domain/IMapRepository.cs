using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Domain
{
	public interface IMapRepository
	{
		public void SaveMap(Map map);
		public Map LoadMap(string path);
		public void DeleteMap(Map map);
		public IEnumerable<Map> ListMaps();

	}
}
