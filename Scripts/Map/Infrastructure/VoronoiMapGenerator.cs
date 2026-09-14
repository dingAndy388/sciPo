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
		private readonly IConfigLoader _resourceConfigLoader;
		private IEnumerable<ITerrainData> _terrainConfig;
		private IMapGeneratorConfig _mapGeneratorConfig;
		private readonly ITerrainConfigRepository _terrainRepo;
		private readonly IMapGeneratorConfig _jsonGeneratorConfig;
		private readonly string _generatorConfigPath;

		/// <param name="terrainRepository">地形配置表（<c>Config/Terrains.json</c>）。</param>
		/// <param name="generatorConfig">生成器配置表（<c>Config/Generator.json</c>，v0.3 / WP-1.4 起通电）。</param>
		/// <param name="resourceConfigLoader">可选的 Godot 资源加载器：仅当它非空且 <paramref name="generatorConfigPath"/> 有效时，
		/// 才尝试用 <c>Config/Generator/Generator.tres</c> 覆写配置表（方便在编辑器里调参）；无头环境传 null。</param>
		/// <param name="generatorConfigPath">.tres 覆写目录（如 <c>res://Config/Generator</c>）。</param>
		public VoronoiMapGenerator(
			ITerrainConfigRepository terrainRepository,
			IMapGeneratorConfig generatorConfig,
			IConfigLoader resourceConfigLoader = null,
			string generatorConfigPath = null)
		{
			this._terrainRepo = terrainRepository;
			this._jsonGeneratorConfig = generatorConfig;
			this._resourceConfigLoader = resourceConfigLoader;
			this._generatorConfigPath = generatorConfigPath;
		}

		public Domain.Map Generate(int width, int height, int seed, string Id)
		{
			// reload config
			_terrainConfig = _terrainRepo.GetAll();

			// v0.3 / WP-1.4：生成器配置的权威来源是配置表（JSON），.tres 仅作编辑器覆写
			_mapGeneratorConfig = ResolveGeneratorConfig();

			// generate blank map
			Domain.Map map = GetBlankMap(width, height, seed, Id);

			// distribute terrains
			map = DistributeTerrain(map, seed);
			return map;
		}

		/// <summary>
		/// 取值顺序：Godot <c>.tres</c>（存在资源加载器时）→ JSON 配置表 → 代码默认值。
		/// 无头环境（测试 / 离线结算）没有资源加载器，走 JSON 表，与其余 6 张表一致（`DEP-07`）。
		/// </summary>
		private IMapGeneratorConfig ResolveGeneratorConfig()
		{
			if (_resourceConfigLoader != null && !string.IsNullOrWhiteSpace(_generatorConfigPath))
			{
				IMapGeneratorConfig fromResource = _resourceConfigLoader.Load<IMapGeneratorConfig>($"{_generatorConfigPath}/Generator.tres");
				if (fromResource != null) return fromResource;
			}

			return _jsonGeneratorConfig ?? new GeneratorConfigDto();
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

			// get weights
			Dictionary<ITerrainData, float> terrainDis = _terrainConfig.ToDictionary(t => t, t => t.Weight);

			HashSet<HexCubePosition> anchors = new HashSet<HexCubePosition>(nAnchor);
			// set anchor Terrain
			while (anchors.Count < nAnchor)
			{
				bool flag = false;
				// pick random CellPosition
				int rx = _random.Next(0, x);
				int ry = _random.Next(0, y);
				HexCubePosition pos = new HexCubePosition(rx, ry);
				foreach (HexCubePosition p in anchors)
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
			foreach (HexCubePosition cell in validCells)
			{
				int bestDist = int.MaxValue;
				ITerrainData bestTerrain = null;
				foreach (HexCubePosition p in anchors)
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
					HexCubePosition pos = new HexCubePosition(i, j);
					map.SetCell(pos, new MapCell(pos));
				}
			}

			return map;
		}
	}
}
