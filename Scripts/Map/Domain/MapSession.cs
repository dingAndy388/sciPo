using System.Collections.Generic;

namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.3 / WP-3.1）Map 的运行时缓存 —— 让 Map 真正成为**常驻内存的聚合根**，
	/// 而不是"每次操作都从磁盘反序列化一份新副本"。
	/// <para>背景（`MAP-01` / `MAP-02`）：改造前 <c>MapAppService</c> 的每个方法都 LoadMap→改→SaveMap，
	/// 而存档只保存地形 ⇒ 占据物（建筑/单位）与人口在两次调用之间丢失，
	/// 导致建造/训练完成回调按 uid 取占据物时抛 <c>KeyNotFoundException</c>、人口永远为 0
	/// （M0-2 验收 ③④ 不可能通过）。</para>
	/// <para>职责边界：本类只负责"缓存 + 脏标记"；真正的落盘仍由 <see cref="IMapRepository"/>
	/// 在**存档点**（<see cref="Flush"/>）执行；实体级持久化由 `WP-3.2` 补齐。</para>
	/// </summary>
	public sealed class MapSession(IMapRepository repository)
	{
		private readonly IMapRepository _repository = repository;
		private readonly Dictionary<string, Map> _maps = new();
		private readonly HashSet<string> _dirty = new();

		/// <summary>累计读盘次数（用于断言"同一地图只读一次"）。</summary>
		public int LoadCount { get; private set; }

		/// <summary>累计落盘次数（用于断言"只在存档点写盘"）。</summary>
		public int SaveCount { get; private set; }

		public IReadOnlyCollection<string> LoadedMapIds => _maps.Keys;

		public bool IsLoaded(string mapId) => mapId != null && _maps.ContainsKey(mapId);

		public bool IsDirty(string mapId) => mapId != null && _dirty.Contains(mapId);

		/// <summary>取地图：首次访问才读盘，之后一律返回同一个内存实例。</summary>
		public Map Get(string mapId)
		{
			if (mapId == null) return null;
			if (_maps.TryGetValue(mapId, out Map cached)) return cached;

			Map loaded = _repository.LoadMap(mapId);
			LoadCount++;
			if (loaded != null) _maps[mapId] = loaded;
			return loaded;
		}

		/// <summary>把地图放入/替换内存缓存（如刚生成的新地图）。</summary>
		public void Set(string mapId, Map map)
		{
			if (mapId == null || map == null) return;
			_maps[mapId] = map;
		}

		/// <summary>标记为脏：内容已改，等待下一个存档点写盘。</summary>
		public void MarkDirty(string mapId)
		{
			if (mapId == null) return;
			_dirty.Add(mapId);
		}

		/// <summary>存档点：仅把标记为脏的地图写盘。</summary>
		public void Flush(string mapId)
		{
			if (mapId == null) return;
			if (!_dirty.Contains(mapId)) return;
			if (!_maps.TryGetValue(mapId, out Map map)) return;

			_repository.SaveMap(map);
			SaveCount++;
			_dirty.Remove(mapId);
		}

		/// <summary>存档点：把所有脏地图写盘（存档/退出时调用）。</summary>
		public void FlushAll()
		{
			foreach (string mapId in new List<string>(_dirty))
				Flush(mapId);
		}

		/// <summary>从缓存移除（不落盘）：切换地图或会话结束时调用。</summary>
		public void Evict(string mapId)
		{
			if (mapId == null) return;
			_maps.Remove(mapId);
			_dirty.Remove(mapId);
		}
	}
}
