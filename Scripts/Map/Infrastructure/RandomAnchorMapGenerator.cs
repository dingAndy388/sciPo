using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	public class RandomAnchorMapGenerator : IMapGenerator
	{
		private IRandom _random;
		private IConfigLoader _config;
		private IEnumerable<ITerrainData> _terrainConfig;
		private IMapGeneratorConfig _mapGeneratorConfig;
		private string terrainConfigDir, generatorConfigPath;

		public RandomAnchorMapGenerator(string terrainConfigDir, string generatorConfigPath)
		{
			this.terrainConfigDir = terrainConfigDir;
			this.generatorConfigPath = generatorConfigPath;
		}

		public Domain.Map Generate(int width, int height, int seed)
		{
			// reload config
			_terrainConfig = _config.LoadAll<ITerrainData>(terrainConfigDir);
			_mapGeneratorConfig = _config.Load<IMapGeneratorConfig>(generatorConfigPath);

			// generate blank map
			Domain.Map map = GetBlankMap(width, height, seed);
			// distribute terrains
			map = DistributeTerrain(map, seed);
			return map;
		}

		private Domain.Map DistributeTerrain(Domain.Map map, int seed)
		{
			// set random gen
			_random = new SystemRandom(seed);

			// get map props
			int x = map.width;
			int y = map.height;
			int area = x * y;

			// calculate the number of anchors
			int nAnchor = (int)_random.NextGaussian(_mapGeneratorConfig.Density / 100 * area, _mapGeneratorConfig.Density / 100 * 0.2 * area);

			List<IPosition> anchors = [];

			// set anchor terrain
			while (anchors.Count < nAnchor)
			{
				// pick random position
				int rx = _random.Next(-x / 2, x / 2);
				int ry = _random.Next(-y / 2, y / 2);
				IPosition pos = new HexCubePosition(rx, ry);

				// get weights
				Dictionary<ITerrainData, double> terrainDis = [];
				foreach (ITerrainData terrain in _terrainConfig)
				{
					terrainDis[terrain] = terrain.Weight;
				}

				// pick terrain
				ITerrainData pickedTerrain = _random.WeightedPick<ITerrainData>(terrainDis.Keys, terrainDis.Values);

				// set terrain
				map.SetTerrain(pos, pickedTerrain);	

				// add to list
				if (!anchors.Contains(pos))
				{
					anchors.Add(pos);
				}
			}

			// get mapcell dict
			Dictionary<IPosition, ITerrainData> cellList = [];
			for (int i = 0; i < x; i++)
			{
				for (int j = 0; j < y; j++)
				{
					IPosition pos = new HexCubePosition(i - x / 2, j - y / 2);
					cellList[pos] = map.GetTerrain(pos);
				}
			}

			// terrain spread
			bool spreaded = false;
			List<IPosition> spreadingcell = anchors;

			while (!spreaded)
			{
				foreach (IPosition cell in spreadingcell)
				{
					spreaded = true;
					// check if there is a terrain
					if (cellList[cell] != null)
					{
						// take all neighbor cells
						List<IPosition> neighbor = (List<IPosition>)cell.GetNeighbor();
						// for every neighbor cells, set terrain to the same as spreadingcell if there is no terrain yet
						foreach (IPosition neighborCell in neighbor)
						{
							// check boundaries
							if (cellList.ContainsKey(neighborCell))
							{
								// check if there was already a terrain
								if (cellList[neighborCell] == null)
								{
									// set to the same as srpeading cell
									cellList[neighborCell] = cellList[cell];
									// add the spreaded cell to spreading cell for next spread
									spreadingcell.Add(neighborCell);
									// remove the already finished cell
									spreadingcell.Remove(cell);
								}
							}
							else
								neighbor.Remove(neighborCell);
						}
					}
					else
						spreaded = false;
				}
			}

			// update terrain to map
			foreach (IPosition pos in cellList.Keys)
			{
				map.SetTerrain(pos, cellList[pos]);
			}

			return map;
		}
		private Domain.Map GetBlankMap(int width, int height, int seed)
		{
			Domain.Map map = new Domain.Map(seed, width, height);

			for (int i = 0; i < width; i++)
			{
				for (int j = 0; j < height; j++)
				{
					IPosition pos = new HexCubePosition(i - width / 2, j - height / 2);
					map.SetCell(pos, new MapCell(pos));
				}
			}

			return map;
		}
	}
}

