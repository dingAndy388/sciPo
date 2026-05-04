using SciencePotato.Scripts.Common.Domain;

namespace SciencePotato.Scripts.Map.Domain
{
	public class MapCell(HexCubePosition position)
	{
		public HexCubePosition Position { get; set; } = position;
		public ITerrainData Terrain { get; private set; }
		public IMapOccupant Occupant { get; private set; }


		public void SetTerrain(ITerrainData terrain)
		{
			this.Terrain = terrain;
		}

		public void SetOccupant(IMapOccupant occupant)
		{
			Occupant = occupant;
		}
	}
}
