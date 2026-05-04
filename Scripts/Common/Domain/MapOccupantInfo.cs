namespace SciencePotato.Scripts.Common.Domain
{
	//DTO of IMapOccupant
	//Issue by IMapOccupant, Recieved by Map sys
	public struct	MapOccupantInfo(HexCubePosition coord, int id, long uid, string name, OccupantType type)
	{
		//Position
		public readonly HexCubePosition Position = coord;
		
		//ID maps to specific entity (e.g. farm)
		public readonly int ID = id;
		
		//identifier on map
		public readonly long UID = uid;

		//displayed name to players
		public readonly string Name = name;

		//type (unit/building)
		public readonly OccupantType Type =type;
	}
}