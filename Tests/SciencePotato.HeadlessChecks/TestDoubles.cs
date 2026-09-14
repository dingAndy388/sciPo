using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>（v0.3 / WP-0.2）内存文件系统替身：让 Domain/Application 脱离 Godot 的 FileAccess 运行。</summary>
	internal sealed class InMemoryFileSystem : IFileSystem
	{
		private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

		public bool Exists(string path) => _files.ContainsKey(path);

		public string ReadAllText(string path) => _files.TryGetValue(path, out string text) ? text : null;

		public void WriteAllText(string path, string text) => _files[path] = text;

		public void EnsureDirectory(string directory) { /* 内存实现无需目录 */ }

		public IEnumerable<string> ListFiles(string directory)
		{
			foreach (string key in _files.Keys)
				if (key.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
					yield return key;
		}

		public void Delete(string path) => _files.Remove(path);
	}

	/// <summary>（v0.3 / WP-0.2）内存配置来源替身：预置若干 JSON 文本。</summary>
	internal sealed class InMemoryConfigSource : IConfigSource
	{
		private readonly Dictionary<string, string> _configs = new(StringComparer.OrdinalIgnoreCase);

		public void Inject(string configName, string json) => _configs[configName] = json;

		public string LoadText(string configName) => _configs.TryGetValue(configName, out string json) ? json : null;
	}

	/// <summary>（v0.3 / WP-0.2 / WP-1.1）手动时间驱动：测试里精确控制"推进多少真实秒"。</summary>
	internal sealed class ManualTimeDriver(GameClock clock) : ITimeDriver
	{
		public GameClock Clock { get; } = clock;

		public void Advance(double realDeltaSeconds) => Clock.Advance(realDeltaSeconds);
	}

	/// <summary>
	/// （v0.3 / WP-0.2 / WP-3.1）内存地图仓库替身。与真实存档的行为保持一致：**只保存地形**，
	/// 因此读档后占据物与人口会丢失 —— 这正是 MapSession（WP-3.1）要解决的 M0-2 ③④ 阻塞点。
	/// </summary>
	internal sealed class InMemoryMapRepository : IMapRepository
	{
		private sealed class Snapshot
		{
			public int Seed;
			public int Width;
			public int Height;
			public string Id;
			public readonly Dictionary<HexCubePosition, ITerrainData> Terrain = new();
		}

		private readonly Dictionary<string, Snapshot> _store = new(StringComparer.OrdinalIgnoreCase);

		public int LoadCount { get; private set; }
		public int SaveCount { get; private set; }

		public void SaveMap(Map map)
		{
			SaveCount++;
			var snapshot = new Snapshot { Seed = map.seed, Width = map.width, Height = map.height, Id = map.Id };
			foreach (MapCell cell in map.GetAllCells())
				snapshot.Terrain[cell.Position] = cell.Terrain;
			_store[map.Id] = snapshot;
		}

		public Map LoadMap(string id)
		{
			LoadCount++;
			if (id == null || !_store.TryGetValue(id, out Snapshot snapshot)) return null;

			var map = new Map(snapshot.Seed, snapshot.Width, snapshot.Height, snapshot.Id);
			foreach (KeyValuePair<HexCubePosition, ITerrainData> pair in snapshot.Terrain)
			{
				var cell = new MapCell(pair.Key);
				cell.SetTerrain(pair.Value);
				map.SetCell(pair.Key, cell);
			}
			return map;
		}

		public void DeleteMap(Map map) => _store.Remove(map.Id);

		public IEnumerable<Map> ListMaps()
		{
			foreach (string id in new List<string>(_store.Keys))
			{
				Map map = LoadMap(id);
				if (map != null) yield return map;
			}
		}
	}

	/// <summary>（v0.3 / WP-0.2）占据物替身：用于验证"占据物跨调用不丢失"。</summary>
	internal sealed class FakeOccupant(HexCubePosition position, string id, string uid, int ownerId, float hp = 10f) : IMapOccupant
	{
		public bool IsReady { get; set; } = true;

		public MapOccupantInfo GetInfo()
			=> new(position, id, uid, ownerId, id, IsReady, hp, OccupantType.Building);
	}
}
