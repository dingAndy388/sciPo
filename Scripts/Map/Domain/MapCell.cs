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

		/// <summary>
		/// （v0.3 / WP-3.6 / `E9`）**同格交战的进攻方**：被挑战方留在 <see cref="Occupant"/>，
		/// 攻进该格的单位落在这里 ⇒ "一格内一对正在交战的单位"（design/unit.md）。
		/// <para>写入/清除的唯一入口是 <c>Map.BeginEngagement</c> / <c>Map.EndEngagement</c>
		/// （`WP-3.4` 的统一入口口径）；被挑战方阵亡时由 <c>Map.RemoveOccupant</c> 顶替上位。</para>
		/// </summary>
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

        /// <summary>（v0.8.8 / WP-4.9）本格上的**附属建筑**（宿主是 <see cref="Building"/>）。</summary>
		public List<IMapOccupant> Attachments { get; } = new List<IMapOccupant>();

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
