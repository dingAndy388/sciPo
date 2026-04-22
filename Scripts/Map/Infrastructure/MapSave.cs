using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	public partial class MapSave
	{
		public string ID { get; set; }
		public int width { get; set; }
		public int height { get; set; }
		public int seed { get; set; }
		public List<HexCubeCellSave> cells { get; set; } = new List<HexCubeCellSave>();
	}
}
