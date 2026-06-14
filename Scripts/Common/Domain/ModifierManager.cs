using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	public class ModifierManager(Dictionary<string, List<ModifierValue>> modifiers)
	{
		private Dictionary<string, List<ModifierValue>> _modifiers = modifiers;

		public void AddModifier(string target, ModifierValue value)
		{
			if (!_modifiers.ContainsKey(target))
			{
				_modifiers[target] = new List<ModifierValue>();
			}
			_modifiers[target].Add(value);
		}

		public void RemoveModifierByTarget(string target, ModifierValue value)
		{
			if (_modifiers.ContainsKey(target))
			{
				_modifiers[target].Remove(value);
				if (_modifiers[target].Count == 0)
				{
					_modifiers.Remove(target);
				}
			}
		}

		public float GetValue(string[] targets, float baseValue)
		{
			float result = baseValue;
			foreach (string target in targets)
			{
				if (_modifiers.ContainsKey(target))
				{
					float abs = 0;
					float per = 0;
					foreach (var modifer in _modifiers[target])
					{
						if (modifer.Type == ModifierType.Absolute)
						{
							abs += modifer.Value;
						}
						else if (modifer.Type == ModifierType.Percentage)
						{
							per += modifer.Value;
						}
					}
					return (result + abs) * (1 + per);
				}
			}
			return result;
		}

		public void RemoveModifiersBySourceId(string sourceId)
		{
			foreach (var target in _modifiers.Keys.ToList())
			{
				_modifiers[target].RemoveAll(m => m.SourceId == sourceId);
				if (_modifiers[target].Count == 0)
				{
					_modifiers.Remove(target);
				}
			}
		}

		public Dictionary<string, List<ModifierValue>> GetAllModifiers()
		{
			return _modifiers;
		}
	}
}
