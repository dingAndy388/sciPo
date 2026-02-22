using SciencePotato.Scripts.Map.Domain;
using System;
using System.Collections.Generic;
using Godot;
using System.Text.Json;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Common.Infrastructure;

namespace SciencePotato.Scripts.Map.Infrastructure
{
    public class GodotMapRepository : IMapRepository
    {
        private readonly string _mapDir = "user://maps/";

        private readonly IConfigLoader _configLoader = new GodotConfigService();

        public void DeleteMap(Domain.Map map)
        {
            throw new NotImplementedException();
        }

        public Domain.Map LoadMap(string ID)
        {
            string path = $"{_mapDir}{ID}.json";

            if (!FileAccess.FileExists(path))
                return null;

            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            string json = file.GetAsText();

            MapSave mapSave = JsonSerializer.Deserialize<MapSave>(json);

            GD.Print("json:"+ mapSave.cells.Count);

            Domain.Map map = new Domain.Map(mapSave.seed, mapSave.width, mapSave.height, mapSave.ID);

            foreach (HexCubeCellSave cellSave in mapSave.cells)
            {
                GD.Print("Position: " + cellSave.position.q + " , " +cellSave.position.r);
                MapCell cell = new MapCell(cellSave.position);
                cell.SetTerrain(_configLoader.Load<ITerrainData>($"res://Config/Terrains/{cellSave.terrain}.tres"));

                map.SetCell(cellSave.position, cell);
            }

            return map;
        }

        public void SaveMap(Domain.Map map)
        {
            string path = $"{_mapDir}{map.ID}.json";

            GD.Print("saving");
            GD.Print(map.GetAllCells().Count());

            MapSave mapSave = new MapSave();
            mapSave.ID = map.ID;
            mapSave.seed = map.seed;
            mapSave.height = map.height;
            mapSave.width = map.width;
            foreach (var cell in map.GetAllCells())
            {
                HexCubeCellSave cellSave = new HexCubeCellSave();
                cellSave.position = (HexCubePosition)cell.position;
                cellSave.terrain = cell.terrain.Id;
                mapSave.cells.Add(cellSave);
            }
            if (!DirAccess.DirExistsAbsolute(_mapDir))
            {
                DirAccess.MakeDirAbsolute(_mapDir);
            }

            string json = JsonSerializer.Serialize(mapSave,new JsonSerializerOptions { WriteIndented = true });
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            file.StoreString(json);
        }

        public IEnumerable<Domain.Map> ListMaps()
        {
            throw new NotImplementedException();
        }
    }
}
