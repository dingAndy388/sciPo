using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	public class VoronoiMapGenerator : IMapGenerator
	{
		private IRandom _random;
		private IConfigLoader _config;
		private IEnumerable<ITerrainData> _terrainConfig;
		private IMapGeneratorConfig _mapGeneratorConfig;
		private string terrainConfigDir, generatorConfigPath;

		public VoronoiMapGenerator(string terrainConfigDir, string generatorConfigPath, IConfigLoader config)
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

			HashSet<IPosition> validCells = new(x * y);
			for (int i = 0; i < x; i++)
				for (int j = 0; j < y; j++)
				{
					validCells.Add(new HexCubePosition(i, j));
				}

			// get weights
			Dictionary<ITerrainData, float> terrainDis = _terrainConfig.ToDictionary(t => t, t => t.Weight);

			HashSet<IPosition> anchors = new HashSet<IPosition>(nAnchor);
			// set anchor Terrain
			while (anchors.Count < nAnchor)
			{
				bool flag = false;
				// pick random CellPosition
				int rx = _random.Next(0, x);
				int ry = _random.Next(0, y);
				IPosition pos = new HexCubePosition(rx, ry);
				foreach (IPosition p in anchors)
				{
					if (pos.DistenceTo(p) < 2)
						flag = true;
				}
				if (flag) continue;

				// pick Terrain
				ITerrainData pickedTerrain = _random.WeightedPick<ITerrainData>(terrainDis.Keys, terrainDis.Values);

				// set Terrain
				map.SetTerrain(pos, pickedTerrain);
				anchors.Add(pos);
			}

			// Terrain spread
			foreach (IPosition cell in validCells)
			{
				int bestDist = int.MaxValue;
				ITerrainData bestTerrain = null;
				foreach (IPosition p in anchors)
				{
					if (cell.DistenceTo(p) < bestDist)
					{
						bestDist = cell.DistenceTo(p);
						bestTerrain = map.GetTerrain(p);
					}
				}
				map.SetTerrain(cell, bestTerrain);
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
					IPosition pos = new HexCubePosition(i, j);
					map.SetCell(pos, new MapCell(pos));
				}
			}

			return map;
		}
	}
}
