using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	internal interface IModifiable
	{
		void AddModifier(IModifier modifier);
		void RemoveModifier(IModifier modifier);
	}
}
