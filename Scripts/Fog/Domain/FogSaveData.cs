using System.Collections.Generic;

namespace SciencePotato.Scripts.Fog.Domain
{
	public class FogSaveData
	{
		public int OwnerId { get; set; }
		public Dictionary<string, byte> MatrixData { get; set; } = new();
	}
}