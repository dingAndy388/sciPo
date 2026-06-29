using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Application
{
	public class ModifierAppService
	{
		private readonly IModifierRepository _repo;

		public ModifierAppService(IModifierRepository repo)
		{
			_repo = repo;
		}

		public void AddModifier(string mapId, int ownerId, string sourceId, Modifier modifier)
		{
			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));

			var value = new ModifierValue(
				modifier.Type == "Percent" ? ModifierType.Percentage : ModifierType.Absolute,
				modifier.Value, sourceId);

			manager.AddModifier(modifier.Target, value);

			_repo.SaveModifier(mapId, ownerId, manager.GetAllModifiers());
		}

		public void AddModifiers(string mapId, int ownerId, string sourceId, List<Modifier> modifiers)
		{
			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));
			foreach (var modifier in modifiers)
			{
				var value = new ModifierValue(
					modifier.Type == "Percent" ? ModifierType.Percentage : ModifierType.Absolute,
					modifier.Value, sourceId);
				manager.AddModifier(modifier.Target, value);
			}
			_repo.SaveModifier(mapId, ownerId, manager.GetAllModifiers());
		}

		public void RemoveModifiersBySourceId(string mapId, int ownerId, string sourceId)
		{
			var manager = new ModifierManager(_repo.LoadModifiers(mapId, ownerId));
			manager.RemoveModifiersBySourceId(sourceId);
			_repo.SaveModifier(mapId, ownerId, manager.GetAllModifiers());
		}
	}
}