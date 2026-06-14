using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	public class GenericJsonRepository<T> where T : class
	{
		private Dictionary<string, T> _data = new();

		public void Load(string filePath)
		{ 
			_data.Clear();
			if (!File.Exists(filePath)) return;

			string json = File.ReadAllText(filePath);
			_data = JsonConvert.DeserializeObject<Dictionary<string, T>>(json) ?? new Dictionary<string, T>();
		}

		public void Save(string filePath)
		{
			string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
			File.WriteAllText(filePath, json);
		}

		public T GetById(string id) => _data.GetValueOrDefault(id);
		public void AddOrUpdate(string id, T item, string filePath		) 
		{
			_data[id] = item;
			Save(filePath);
		}
		public List<T> GetAll()
		{ 
			return _data.Values.ToList(); 
		}
	}
}
