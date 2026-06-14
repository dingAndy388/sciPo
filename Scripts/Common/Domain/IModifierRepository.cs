using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	public interface IModifierRepository
	{
		Dictionary<string, List<ModifierValue>> LoadModifiers(string mapId);
		void SaveModifier(string mapId, Dictionary<string, List<ModifierValue>> modifiers);
	}
}
