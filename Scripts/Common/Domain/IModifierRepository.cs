using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Domain
{
	public interface IModifierRepository
	{
		Dictionary<string, List<ModifierValue>> LoadModifiers(string mapId, int ownerId);
		void SaveModifier(string mapId, int ownerId, Dictionary<string, List<ModifierValue>> modifiers);
	}
}