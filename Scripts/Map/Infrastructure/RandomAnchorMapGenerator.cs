using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	public class RandomAnchorMapGenerator : IMapGenerator
	{
		private IRandom _random;
		private IConfigLoader _config;
		private IEnumerable<ITerrainData> _terrainConfig;
		private IMapGeneratorConfig _mapGeneratorConfig;
		private string terrainConfigDir, generatorConfigPath;

		public RandomAnchorMapGenerator(string terrainConfigDir, string generatorConfigPath, IConfigLoader config)
		{
			this._config = config;
			this.terrainConfigDir = terrainConfigDir;
			this.generatorConfigPath = generatorConfigPath;
		}

		public Domain.Map Generate(int width, int height, int seed, string Id)
		{
			// reload config
			_terrainConfig = _config.LoadAll<ITerrainData>(terrainConfigDir);
			_mapGeneratorConfig = _config.Load<IMapGeneratorConfig>($"{generatorConfigPath}/Generator.tres");

			// generate blank map
			Domain.Map map = GetBlankMap(width, height, seed, Id);

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
			int nAnchor = Math.Clamp((int)_random.NextGaussian(_mapGeneratorConfig.Density / 100 * area, _mapGeneratorConfig.Density / 100 * 0.2f * area), area / 100, area / 10);

			HashSet<HexCubePosition> validCells = new(x * y);
			for (int i = 0; i < x; i++)
				for (int j = 0; j < y; j++)
				{
					validCells.Add(new HexCubePosition(i, j));
				}

			HashSet<HexCubePosition> anchors = new HashSet<HexCubePosition>(nAnchor);
			// set anchor Terrain
			while (anchors.Count < nAnchor)
			{
				// pick random CellPosition
				int rx = _random.Next(0, x);
				int ry = _random.Next(0, y);
				HexCubePosition pos = new HexCubePosition(rx, ry);
				foreach (HexCubePosition p in anchors)
				{
					if (pos.DistenceTo(p) < 2)
						continue;
				}

				// get weights
				Dictionary<ITerrainData, float> terrainDis = _terrainConfig.ToDictionary(t => t, t => t.Weight);

				// pick Terrain
				ITerrainData pickedTerrain = _random.WeightedPick<ITerrainData>(terrainDis.Keys, terrainDis.Values);

				// set Terrain
				map.SetTerrain(pos, pickedTerrain);
				anchors.Add(pos);
			}

			// Terrain spread
			Queue<HexCubePosition> queue = new Queue<HexCubePosition>(anchors);
			int filled = nAnchor;
			while (queue.Count > 0)
			{
				HexCubePosition current = queue.Dequeue();
				ITerrainData terrainn = map.GetTerrain(current);
				foreach (HexCubePosition neighbor in current.GetNeighbor())
				{
					if (validCells.Contains(neighbor) && map.GetTerrain(neighbor) == null)
					{
						map.SetTerrain(neighbor, terrainn);
						filled++;
						queue.Enqueue(neighbor);
					}
				}
			}

			return map;
		}

		private Domain.Map GetBlankMap(int width, int height, int seed, string Id)
		{
			Domain.Map map = new Domain.Map(seed, width, height, Id);

			for (int i = 0; i < width; i++)
			{
				for (int j = 0; j < height; j++)
				{
					HexCubePosition pos = new HexCubePosition(i, j);
					map.SetCell(pos, new MapCell(pos));
				}
			}

			return map;
		}
	}
}

