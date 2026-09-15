using Godot;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SciencePotato.Scripts.Map.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-0.4 + WP-3.2）Godot 侧地图存档：`user://maps/{mapId}.json`。
	/// <para>v0.3.15（`WP-3.2` / `MAP-02`）起**不再只存地形**：人口与占据物（建筑/单位，含 HP/MP/训练队列/建造者绑定）
	/// 都随 `SaveMapper` 一起落盘；读档按 uid 原样重建，因此修正器、迷雾、任务、易主等以 uid 为锚的引用不会失联。</para>
	/// </summary>
	public class GodotMapRepository : IMapRepository
	{
		private readonly string _mapDir = "user://maps/";

		private readonly ITerrainConfigRepository _terrainRepo;
		private SaveRebuilder _rebuilder;

		/// <param name="terrainRepository">地形解析（v0.3 / WP-0.4：地形来源为 JSON 配置表）。</param>
		/// <param name="rebuilder">
		/// 实体重建器（v0.3 / WP-3.2）。为 null 时读档只恢复地形（等价旧行为）—— 便于"没有建筑/单位工厂"的
		/// 极简场景；正式装配由组合根提供（见 `CoreBootstrap`）。
		/// </param>
		public GodotMapRepository(ITerrainConfigRepository terrainRepository, SaveRebuilder rebuilder = null)
		{
			_terrainRepo = terrainRepository;
			_rebuilder = rebuilder;
		}

		/// <summary>（v0.3 / WP-3.2）补挂实体重建器（组合根在拿到配置表后调用，见 `CoreBootstrap.AttachRebuilder`）。</summary>
		public void AttachRebuilder(SaveRebuilder rebuilder) => _rebuilder = rebuilder;

		/// <summary>（v0.9.7 / `WP-5.6`）上一次落盘的 DTO：增量合并的**比较基线**（也是"无变化则跳过写盘"的依据）。</summary>
		private readonly Dictionary<string, MapSave> _cache = new(StringComparer.OrdinalIgnoreCase);

		/// <inheritdoc />
		public int LastSavedDirtyCells { get; private set; }

		/// <inheritdoc />
		public int LastSavedTotalCells { get; private set; }

		/// <inheritdoc />
		public bool LastSaveSkippedWrite { get; private set; }

		/// <inheritdoc />
		public long LastSaveMilliseconds { get; private set; }

		public void DeleteMap(Domain.Map map)
		{
			if (map == null) return;
			_cache.Remove(map.Id); // （v0.9.7 / WP-5.6）缓存也要清：否则删档后下一次存档会拿旧基线做增量

			string path = $"{_mapDir}{map.Id}.json";
			if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(path);
		}

		public Domain.Map LoadMap(string Id)
		{
			string path = $"{_mapDir}{Id}.json";

			if (!FileAccess.FileExists(path))
				return null;

			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			string json = file.GetAsText();

			MapSave mapSave = JsonSerializer.Deserialize<MapSave>(json);
			if (mapSave == null) return null;

			// 更高版本的存档**拒绝加载**（猜未知字段语义只会静默丢数据；迁移由 WP-3.3 的存档单元负责）
			if (mapSave.SaveVersion > MapSave.CurrentVersion)
			{
				GD.PushError($"[GodotMapRepository] 存档 {Id} 的 SaveVersion={mapSave.SaveVersion} 高于当前支持的 {MapSave.CurrentVersion}，拒绝加载");
				return null;
			}

			Domain.Map map = new Domain.Map(mapSave.seed, mapSave.width, mapSave.height, mapSave.Id);

			foreach (HexCubeCellSave cellSave in mapSave.cells)
			{
				MapCell cell = new MapCell(cellSave.position);
				ITerrainData terrain = _terrainRepo?.GetById(cellSave.terrain);
				if (terrain == null)
					GD.PushWarning($"[GodotMapRepository] 未知地形 Id「{cellSave.terrain}」（v0.3 WP-0.4：地形已迁移为 JSON 配置表）");
				cell.SetTerrain(terrain);

				// 人口与占据物（v0.3 / WP-3.2）
				cell.SetPopulation(cellSave.Population);
				map.SetCell(cellSave.position, cell);

				if (cellSave.Building != null)
				{
					IMapOccupant building = _rebuilder?.RebuildBuilding(cellSave.Building, cellSave.position);
					if (building != null) map.PlaceBuilding(building, cellSave.position); // WP-3.4：建筑落位一致
					else GD.PushWarning($"[GodotMapRepository] 建筑「{cellSave.Building.Id}」(uid={cellSave.Building.UId}) 无法重建：配置缺失？");
				}
				else if (cellSave.Unit != null)
				{
					IMapOccupant unit = _rebuilder?.RebuildUnit(cellSave.Unit, cellSave.position);
					if (unit != null) map.AddOccupant(unit, cellSave.position);
					else GD.PushWarning($"[GodotMapRepository] 单位「{cellSave.Unit.Id}」(uid={cellSave.Unit.UId}) 无法重建：配置缺失？");
				}

				// 同格交战的进攻方（v0.3 / WP-3.6 / `E9`）：与被挑战方在同一格，单独一个槽位
				if (cellSave.Invader != null && !SaveMapper.RestoreEngagement(map, _rebuilder, cellSave))
					GD.PushWarning($"[GodotMapRepository] 交战进攻方「{cellSave.Invader.Id}」(uid={cellSave.Invader.UId}) 无法恢复：配置缺失或该格已有一对？");
			}

			_cache[Id] = mapSave; // （v0.9.7 / WP-5.6）读档即建立增量基线：读档后的首次存档也能按脏格写
			return map;
		}
		public void SaveMap(Domain.Map map)
		{
			if (map == null) return;

			var watch = System.Diagnostics.Stopwatch.StartNew();

			// 地形 + 人口 + 占据物统一走 SaveMapper（v0.3 / WP-3.2），再与上次落盘做**增量合并**（v0.9.7 / WP-5.6）
			MapSave current = SaveMapper.ToSave(map);
			_cache.TryGetValue(map.Id, out MapSave previous);
			(MapSave merged, int dirtyCells, int totalCells) = MapSaveMerger.Merge(previous, current);

			LastSavedDirtyCells = dirtyCells;
			LastSavedTotalCells = totalCells;
			_cache[map.Id] = merged;

			// 与上次落盘逐格等价 ⇒ 整次跳过写盘（空闲存档点不再产生 2.4 MB 的文本重写）
			if (previous != null && dirtyCells == 0)
			{
				LastSaveSkippedWrite = true;
				LastSaveMilliseconds = watch.ElapsedMilliseconds;
				return;
			}
			LastSaveSkippedWrite = false;

			string path = $"{_mapDir}{map.Id}.json";

			if (!DirAccess.DirExistsAbsolute(_mapDir))
			{
				DirAccess.MakeDirAbsolute(_mapDir);
			}

			string json = JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true });
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
			file.StoreString(json);

			LastSaveMilliseconds = watch.ElapsedMilliseconds;
		}

		public IEnumerable<Domain.Map> ListMaps()
		{
			if (!DirAccess.DirExistsAbsolute(_mapDir)) yield break;

			foreach (string fileName in DirAccess.GetFilesAt(_mapDir))
			{
				if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

				Domain.Map map = LoadMap(fileName.Substring(0, fileName.Length - ".json".Length));
				if (map != null) yield return map;
			}
		}
	}
}
