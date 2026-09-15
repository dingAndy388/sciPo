using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Core.Time;
using SciencePotato.Scripts.Map.Domain;
using SciencePotato.Scripts.Map.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

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

		/// <summary>（v0.3 / WP-3.3）移动/替换：内存实现同样是\"一次操作换掉整个文件\"（不留下半写状态）。</summary>
		public void Move(string source, string destination, bool overwrite)
		{
			if (!_files.TryGetValue(source, out string text)) return;
			if (!overwrite && _files.ContainsKey(destination)) return;

			_files.Remove(source);
			_files[destination] = text;
		}
	}

	/// <summary>（v0.3 / WP-0.2）内存配置来源替身：预置若干 JSON 文本。</summary>
	internal sealed class InMemoryConfigSource : IConfigSource
	{
		private readonly Dictionary<string, string> _configs = new(StringComparer.OrdinalIgnoreCase);

		public void Inject(string configName, string json) => _configs[configName] = json;

		public string LoadText(string configName) => _configs.TryGetValue(configName, out string json) ? json : null;
	}

	/// <summary>（v0.3 / WP-0.2 / WP-1.5）手动时间驱动：测试里精确控制"推进多少真实秒"。</summary>
	internal sealed class ManualTimeDriver(GameClock clock) : ITimeDriver
	{
		public GameClock Clock { get; } = clock;

		public int Advance(double realDeltaSeconds) => Clock.Advance(realDeltaSeconds);
	}

	/// <summary>
	/// （v0.3 / WP-0.2 / WP-3.1 / WP-3.2）内存地图仓库替身。
	/// <para>**与真实存档保持同一套映射逻辑**：内部存 `MapSave`（由 `SaveMapper.ToSave` 生成，含地形/人口/占据物），
	/// 读档时用 `SaveRebuilder` 按 uid 重建实体 —— 这样"存档等价性"用例验的才是真实语义，
	/// 而不是替身自己的简化行为（`WP-3.2` 起旧替身只存地形，会把实体持久化的 bug 掩盖掉）。</para>
	/// </summary>
	internal sealed class InMemoryMapRepository : IMapRepository
	{
		private readonly Dictionary<string, MapSave> _store = new(StringComparer.OrdinalIgnoreCase);

		public int LoadCount { get; private set; }
		public int SaveCount { get; private set; }

		/// <summary>读档时的实体重建器（与真实仓库一致：缺省为 null = 只恢复地形）。</summary>
		public SaveRebuilder Rebuilder { get; set; }

		public void SaveMap(Map map)
		{
			SaveCount++;
			_store[map.Id] = SaveMapper.ToSave(map); // 深拷贝：之后对活动地图的改动不会渗进"存档"
		}

		public Map LoadMap(string id)
		{
			LoadCount++;
			if (id == null || !_store.TryGetValue(id, out MapSave save)) return null;

			// 与真实仓库一致：更高版本的存档**拒绝加载**（v0.3 / WP-3.2）
			if (save.SaveVersion > MapSave.CurrentVersion) return null;

			var map = new Map(save.seed, save.width, save.height, save.Id);

			foreach (HexCubeCellSave cellSave in save.cells)
			{
				var cell = new MapCell(cellSave.position);
				cell.SetTerrain(cellSave.terrain == null ? null : TerrainOf(cellSave.terrain));
				cell.SetPopulation(cellSave.Population);
				map.SetCell(cellSave.position, cell);

				if (cellSave.Building != null)
				{
					IMapOccupant building = Rebuilder?.RebuildBuilding(cellSave.Building, cellSave.position);
					if (building != null) map.PlaceBuilding(building, cellSave.position); // WP-3.4：建筑落位一致
				}
				else if (cellSave.Unit != null)
				{
					IMapOccupant unit = Rebuilder?.RebuildUnit(cellSave.Unit, cellSave.position);
					if (unit != null) map.AddOccupant(unit, cellSave.position);
				}
			}

			return map;
		}

		public void DeleteMap(Map map) => _store.Remove(map.Id);

		/// <summary>（v0.3 / WP-3.2）测试用：篡改存档里的格式版本（模拟更高版本的游戏写出的档）。</summary>
		public void BumpSaveVersion(string id, int version)
		{
			if (_store.TryGetValue(id, out MapSave save)) save.SaveVersion = version;
		}

		public IEnumerable<Map> ListMaps()
		{
			foreach (string id in new List<string>(_store.Keys))
			{
				Map map = LoadMap(id);
				if (map != null) yield return map;
			}
		}

		/// <summary>地形解析：与真实仓库同样按 Id 查地形表；替身只保留 Id → 最小地形对象（无表时返回 null）。</summary>
		public Func<string, ITerrainData> TerrainResolver { get; set; }

		private ITerrainData TerrainOf(string terrainId)
			=> TerrainResolver != null ? TerrainResolver(terrainId) : new TerrainStub(terrainId);

		/// <summary>（v0.3 / WP-3.2）无地形表时的占位地形（仅测试替身使用）。</summary>
		private sealed class TerrainStub(string id) : ITerrainData
		{
			public string Id { get; set; } = id;
			public string Name { get; set; } = id;
			public float Weight { get; set; } = 1f;
			public float MoveCost { get; set; } = 1f;
			public bool Passable { get; set; } = true;
			public string UnlockTech { get; set; } = null;
		}
	}

	/// <summary>
	/// （v0.3 / WP-3.8）**固定结果的随机源替身**：`ProbCodition` 恒为 <see cref="Result"/>。
	/// <para>让"按概率刷新"的处理器在用例里得到**受控布局**：`true` = 每个适配地块都刷出敌人
	/// （用于断言封锁/占用语义），`false` = 一个都不刷（负向对照）。</para>
	/// </summary>
	internal sealed class FixedRandom(bool result) : IRandom
	{
		public bool Result { get; } = result;

		public int Next(int min, int max) => min;

		public int Next() => 0;

		public float NextFloat() => Result ? 0f : 1f;

		public float NextGaussian(float mean, float std) => mean;

		public bool ProbCodition(float p) => Result;

		public T WeightedPick<T>(IEnumerable<T> values, IEnumerable<float> weights) => values.First();
	}

	/// <summary>
	/// （v0.3 / WP-3.10）**循环索引随机源替身**：`Next(min, max)` 依次返回给定索引（用完后从头循环）。
	/// <para>为什么需要它：`FixedRandom` 的 `Next` 恒返回 <c>min</c>，验证不了"按随机索引挑格"这类**索引敏感**的行为
	/// （减员要证明"人散着掉"而不是"永远从第一格扣"）。候选集合在减员过程中只会变小，越界索引按跨度取模 ——
	/// 替身不该在用例里抛异常。</para>
	/// </summary>
	internal sealed class CyclingRandom(params int[] picks) : IRandom
	{
		private readonly int[] _picks = picks is { Length: > 0 } ? picks : new[] { 0 };
		private int _cursor;

		public int Next(int min, int max)
		{
			int span = Math.Max(1, max - min);
			int offset = ((_picks[_cursor++ % _picks.Length] % span) + span) % span;
			return min + offset;
		}

		public int Next() => _picks[_cursor++ % _picks.Length];

		public float NextFloat() => 0f;

		public float NextGaussian(float mean, float std) => mean;

		public bool ProbCodition(float p) => true;

		public T WeightedPick<T>(IEnumerable<T> values, IEnumerable<float> weights) => values.First();
	}

	/// <summary>（v0.3 / WP-0.2）占据物替身：用于验证"占据物跨调用不丢失"。</summary>
	internal sealed class FakeOccupant(HexCubePosition position, string id, string uid, int ownerId, float hp = 10f) : IMapOccupant
	{
		public bool IsReady { get; set; } = true;

		public MapOccupantInfo GetInfo()
			=> new(position, id, uid, ownerId, id, IsReady, hp, OccupantType.Building);
	}
}
