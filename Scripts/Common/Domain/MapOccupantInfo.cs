namespace SciencePotato.Scripts.Common.Domain
{
	//DTO of IMapOccupant
	//Issue by IMapOccupant, Recieved by Map sys
	public struct MapOccupantInfo(HexCubePosition coord, string id, long uid, int owner, string name, bool isReady, float hp, OccupantType type)
	{
		//Position
		public readonly HexCubePosition Position = coord;
		
		//Id maps to specific entity (e.g. farm)
		public readonly string Id = id;
		
		//identifier on map
		public readonly long UId = uid;

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
	}
}