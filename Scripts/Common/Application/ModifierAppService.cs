using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Application
{
	public class ModifierAppService(IModifierRepository repo)
	{
		private IModifierRepository _repo = repo;

		public void AddModifier(string mapId, string sourceId, Modifier modifier)
		{
			ModifierManager manager = new(_repo.LoadModifiers(mapId));

			ModifierValue value = new(modifier.Type == "Percent" ? ModifierType.Percentage : ModifierType.Absolute, modifier.Value, sourceId);

			manager.AddModifier(modifier.Target, value);

			_repo.SaveModifier(mapId, manager.GetAllModifiers());
		}

		public void AddModifiers(string mapId, string sourceId, List<Modifier> modifiers)
		{
			ModifierManager manager = new(_repo.LoadModifiers(mapId));
			foreach (var modifier in modifiers)
			{
				ModifierValue value = new(modifier.Type == "Percent" ? ModifierType.Percentage : ModifierType.Absolute, modifier.Value, sourceId);
				manager.AddModifier(modifier.Target, value);
			}
			_repo.SaveModifier(mapId, manager.GetAllModifiers());
		}

		public void RemoveModifiersBySourceId(string mapId, string sourceId)
		{
			ModifierManager manager = new(_repo.LoadModifiers(mapId));
			manager.RemoveModifiersBySourceId(sourceId);
			_repo.SaveModifier(mapId, manager.GetAllModifiers());
		}
	}
}
