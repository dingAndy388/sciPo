using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	public struct ModifierValue(ModifierType type, float value, string sourceId)
	{
		public ModifierType Type { get; } = type;
		public float Value { get; } = value;
		public string SourceId { get; } = sourceId;
	}
}
