using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;


namespace SciencePotato.Scripts.Map.Infrastructure
{
	[Obsolete("perlin noise has been shown incapable from empirical test, use other generator instead")]
	public class PerlinMapGenerator : IMapGenerator
	{
		private IRandom _random;
		private IConfigLoader _config;
		private IEnumerable<ITerrainData> _terrainConfig;
		private IMapGeneratorConfig _mapGeneratorConfig;
		private string terrainConfigDir, generatorConfigPath;
		private Dictionary<int, int> _hash;

		public PerlinMapGenerator(string terrainConfigDir, string generatorConfigPath, IConfigLoader config)
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

			_hash = RandomHash();

			// get map props
			int width = map.width;
			int height = map.height;
			int area = width * height;

			// calculate the n value from density
			int n = Math.Clamp((int)_random.NextGaussian(_mapGeneratorConfig.Density * 30, _mapGeneratorConfig.Density * 30 * 0.2f), 5, 50);

			//get terrain distribution from config
			var terrainDis = _terrainConfig.Select(t => new { Terrain = t, Weight = t.Weight }).OrderBy(t => t.Weight).ToList();
			float sum = 0;
			foreach (var i in terrainDis)
			{
				sum += i.Weight;
			}

			// Terrain set
			foreach (MapCell cell in map.GetAllCells())
			{
				int x, y;
				(x, y) = cell.position.ToCoordinate();
				float noise = PerlinNoise(x, y, n);

				float rd = noise * sum;
				float c = 0;
				ITerrainData selectedTerrain = null;
				for (int i = 0; i < terrainDis.Count(); i++)
				{
					c += terrainDis[i].Weight;
					if (rd < c)
					{
						selectedTerrain = (terrainDis[i].Terrain);
						break;
					}
				}
				if (selectedTerrain == null) selectedTerrain = terrainDis.Last().Terrain;
				cell.SetTerrain(selectedTerrain);
			}

			return map;
		}

		public Domain.Map GetBlankMap(int width, int height, int seed, string Id)
		{
			Domain.Map map = new Domain.Map(seed, width, height, Id);

			_terrainConfig = _config.LoadAll<ITerrainData>(terrainConfigDir);


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

		private float PerlinNoise(float x, float y, int n)
		{
			x = x / n;
			y = y / n;

			float x0 = (float)Math.Floor(x);
			float x1 = (float)Math.Ceiling(x);
			float y0 = (float)Math.Floor(y);
			float y1 = (float)Math.Ceiling(y);

			Vector2 P1 = new Vector2(x0, y0);
			Vector2 P2 = new Vector2(x1, y0);
			Vector2 P3 = new Vector2(x0, y1);
			Vector2 P4 = new Vector2(x1, y1);

			Vector2 Grad1 = RandomVec(P1);
			Vector2 Grad2 = RandomVec(P2);
			Vector2 Grad3 = RandomVec(P3);
			Vector2 Grad4 = RandomVec(P4);

			float V1 = Vector2.Dot(Grad1, new Vector2(x - x0, y - y0));
			float V2 = Vector2.Dot(Grad2, new Vector2(x - x1, y - y0));
			float V3 = Vector2.Dot(Grad3, new Vector2(x - x0, y - y1));
			float V4 = Vector2.Dot(Grad4, new Vector2(x - x1, y - y1));

			float rx = Blend(x - x0);
			float ry = Blend(y - y0);

			float noise = Lerp(ry, Lerp(rx, V1, V2), Lerp(rx, V3, V4));

			return (noise + 1) / 2;
		}

		private float Blend(float x)
		{
			return x * (x * (x * (10 + x * (-15 + 6 * x))));
		}

		private float Lerp(float r, float a, float b)
		{
			return a + r * (b - a);
		}

		private Vector2 RandomVec(Vector2 seed)
		{
			Vector2[] vecs =
			{
				new Vector2(-1,-1),
				new Vector2(-1,1),
				new Vector2(1,-1),
				new Vector2(1,1)
			};
			int rnd = _hash[(_hash[((int)seed.X % (_hash.Values.Count))] + (int)(seed.Y)) % (_hash.Values.Count)];
			return Vector2.Normalize(vecs[rnd % vecs.Length]);
		}

		private Dictionary<int, int> RandomHash()
		{
			Dictionary<int, int> hash = new Dictionary<int, int>();
			for (int i = 0; i < 256; i++)
			{
				while (!hash.ContainsKey(i))
				{
					int rand = _random.Next(0, 256);
					if (!hash.ContainsValue(rand))
					{
						hash.Add(i, rand);
					}
				}
			}
			return hash;
		}
	}
}

