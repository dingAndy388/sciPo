using SciencePotato.Scripts.Common.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	public class ConfigManager
	{
		public Dictionary<string, string> ConfigCache = new();

		public void Inject(string name, string text)
		{
			ConfigCache[name] = text;
		}

		public string GetConfig(string name)
		{
			if (ConfigCache.TryGetValue(name, out var text))
			{
				return text;
			}	
			return null;
		}
	}
}
