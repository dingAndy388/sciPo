using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	public class ModifierRepository : IModifierRepository
	{
		private readonly string _filePath;

		public ModifierRepository(string filePath)
		{
			_filePath = filePath;
		}

		private string BuildFilePath(string mapId, int ownerId)
		{
			return _filePath + mapId + "_" + ownerId;
		}

		public Dictionary<string, List<ModifierValue>> LoadModifiers(string mapId, int ownerId)
		{
			var repo = new GenericJsonRepository<Dictionary<string, List<ModifierValue>>>();
			repo.Load(BuildFilePath(mapId, ownerId));
			return repo.GetById("modifiers") ?? new Dictionary<string, List<ModifierValue>>();
		}

		public void SaveModifier(string mapId, int ownerId, Dictionary<string, List<ModifierValue>> modifiers)
		{
			var repo = new GenericJsonRepository<Dictionary<string, List<ModifierValue>>>();
			repo.AddOrUpdate("modifiers", modifiers, BuildFilePath(mapId, ownerId));
		}
	}
}