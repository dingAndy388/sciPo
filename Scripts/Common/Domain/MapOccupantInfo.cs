namespace SciencePotato.Scripts.Common.Domain
{
	//DTO of IMapOccupant
	//Issue by IMapOccupant, Recieved by Map sys
	public struct MapOccupantInfo(HexCubePosition coord, string id, string uid, int owner, string name, bool isReady, float hp, OccupantType type, bool isHostile = false)
	{
		//Position
		public readonly HexCubePosition Position = coord;
		
		//Id maps to specific entity (e.g. farm)
		public readonly string Id = id;
		
		//identifier on map
		public readonly string UId = uid;

		//player id
		public readonly int OwnerId = owner;

		//displayed name to players
		public readonly string Name = name;

		//whether the occupant is ready
		public readonly bool IsReady = isReady;

		//type (unit/building)
		public readonly OccupantType Type =type;

		//health points, positive for units, -1 for buildings
		public readonly float HP= hp;

		/// <summary>
		/// （v0.3 / WP-3.8 / `UNIT-14`）**是否敌方单位**（建筑恒为 `false`）。
		/// <para>放在这一层而不是让 `Map` 去 `is Unit`：地图模块只认识占据物**信息**，
		/// 封锁判定（`Map.IsHostileAt`）与"不可建造/不可进入"就不必依赖 Units 模块的类型。</para>
		/// </summary>
		public readonly bool IsHostile = isHostile;
	}
}