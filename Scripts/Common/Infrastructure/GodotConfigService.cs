using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using SciencePotato.Scripts.Common.Domain;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	public class GodotConfigService : IConfigLoader
	{
		public T Load<T>(string path) where T : class
		{
			return ResourceLoader.Load<T>(path);
		}

		public IEnumerable<T> LoadAll<T>(string path) where T : class
		{
			Dictionary<string, T> dict = [];
			DirAccess dir = DirAccess.Open(path);
			foreach (string filename in dir.GetFiles())
			{
				if(filename.EndsWith(".tres")|| filename.EndsWith(".res"))
				{
					string fullDir = $"{path}/{filename}";
					T resource = ResourceLoader.Load<T>(fullDir);
					if (resource != null )
					{
						dict.Add(filename, resource);
					}
				}
			}
			return dict.Values.ToList();
		}
	}
}
