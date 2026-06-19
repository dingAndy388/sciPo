using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Domain
{
	public class MapCell(HexCubePosition position)
	{
		public HexCubePosition Position { get; set; } = position;
		public ITerrainData Terrain { get; private set; }
		public List<IMapOccupant> Occupants { get; private set; } = new List<IMapOccupant>();
		public IMapOccupant Building { get; private set; }

		public void SetTerrain(ITerrainData terrain)
		{
			this.Terrain = terrain;
		}

		public void AddOccupant(IMapOccupant occupant)
		{
			Occupants.Add(occupant);
		}

		public void SetBuilding(IMapOccupant building)
		{
				Building = building;
		}

		public void RemoveOccupant(IMapOccupant occupant)
		{
			if (Occupants.Contains(occupant)) { Occupants.Remove(occupant); }
		}

		public void RemoveBuilding()
		{
			Building = null;
		}

        internal MapOccupantInfo? GetBuildingInfo()
        {
			if(Building!=null)
				return Building.GetInfo();
			return null;
        }
    }
}
