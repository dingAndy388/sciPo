using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Domain
{
	public class MapCell(HexCubePosition position)
	{
		public HexCubePosition Position { get; set; } = position;
		public ITerrainData Terrain { get; private set; }
		public IMapOccupant Occupant {  get; private set; }
		public IMapOccupant Building { get; private set; }

		public IMapOccupant Invader { get; private set; }
		public int Population { get; private set; }

		public void SetTerrain(ITerrainData terrain)
		{
			this.Terrain = terrain;
		}

		public void SetOccupant(IMapOccupant occupant)
		{
			Occupant = occupant;
		}

		public void RemoveOccupant()
		{
			Occupant = null;
		}

        public void SetBuilding(IMapOccupant building)
        {
            Building = building;
        }

        public void RemoveBuilding()
		{
			Building = null;
		}

		public void AddPopulation(int growth)
		{
			Population += growth;
		}

		public void SetPopulation(int population)
		{
			Population = population;
		}

		public void SetInvader(IMapOccupant invader) => Invader = invader;
		public void RemoveInvader() => Invader = null;
	}
}
