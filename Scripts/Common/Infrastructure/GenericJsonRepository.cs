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

		/// <summary>
		/// （v0.3 / WP-2.2）**真正删除**一个键（`TIME-04`）：旧实现用 <c>AddOrUpdate(key, null)</c> 代替删除，
		/// 结果文件里堆积 <c>"key": null</c>，回读后列表含 null 项（任何不做 null 判断的遍历都会 NRE）。
		/// </summary>
		/// <returns>是否命中并删除。</returns>
		public bool Remove(string id, string filePath)
		{
			if (!_data.Remove(id)) return false;
			Save(filePath);
			return true;
		}

		/// <summary>（v0.3 / WP-2.2）按谓词批量删除（范围注销 / 清理"实体已消失的任务"，`TIME-02`）。返回删除条数。</summary>
		public int RemoveWhere(Func<T, bool> predicate, string filePath)
		{
			if (predicate == null) return 0;

			List<string> doomed = _data.Where(pair => pair.Value != null && predicate(pair.Value))
									   .Select(pair => pair.Key)
									   .ToList();
			if (doomed.Count == 0) return 0;

			foreach (string key in doomed) _data.Remove(key);
			Save(filePath);
			return doomed.Count;
		}

		/// <summary>
		/// （v0.3 / WP-2.2）**按业务字段重新编键**：把磁盘上以旧口径（例如"只用 Id 作键"）写入的条目迁移到
		/// <paramref name="keySelector"/> 给出的键上，并顺带丢弃历史 null 项。
		/// <para>为何不直接忽略旧键：旧文件与新键并存会让"新增"和"删除"打在两个不同的键上（同一任务出现两条）。
		/// 迁移只在建键结果与现状不一致时写盘一次。</para>
		/// </summary>
		/// <returns>是否发生了迁移（= 是否写盘）。</returns>
		public bool Reindex(Func<T, string> keySelector, string filePath)
		{
			if (keySelector == null) return false;

			var reindexed = new Dictionary<string, T>();
			foreach (T item in _data.Values)
			{
				if (item == null) continue; // `TIME-04`：历史 null 项直接丢弃
				reindexed[keySelector(item)] = item;
			}

			bool changed = reindexed.Count != _data.Count;
			if (!changed)
				foreach (string key in _data.Keys)
					if (!reindexed.ContainsKey(key)) { changed = true; break; }

			if (!changed) return false;

			_data = reindexed;
			Save(filePath);
			return true;
		}
	}
}
