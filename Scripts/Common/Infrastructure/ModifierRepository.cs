using SciencePotato.Scripts.Common.Domain;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.3）修正器仓储：键 = `modifiers:{mapId}_{ownerId}`。
	/// <para>旧实现每次读/写都新建一个 <see cref="GenericJsonRepository{T}"/>（= 每次读一次盘、写一次整文件）；
	/// 接入存档单元后，写入只标脏，由存档点统一原子落盘。</para>
	/// </summary>
	public class ModifierRepository : IModifierRepository
	{
		private readonly string _filePath;
		private readonly ISaveStore _store;

		public ModifierRepository(string filePath, ISaveStore store = null)
		{
			_filePath = filePath;
			_store = store;
		}

		private string BuildFilePath(string mapId, int ownerId)
		{
			return _store != null ? $"modifiers:{mapId}_{ownerId}" : _filePath + mapId + "_" + ownerId;
		}

		public Dictionary<string, List<ModifierValue>> LoadModifiers(string mapId, int ownerId)
		{
			var repo = new GenericJsonRepository<Dictionary<string, List<ModifierValue>>>(_store);
			repo.Load(BuildFilePath(mapId, ownerId));
			return repo.GetById("modifiers") ?? new Dictionary<string, List<ModifierValue>>();
		}

		public void SaveModifier(string mapId, int ownerId, Dictionary<string, List<ModifierValue>> modifiers)
		{
			var repo = new GenericJsonRepository<Dictionary<string, List<ModifierValue>>>(_store);
			repo.AddOrUpdate("modifiers", modifiers, BuildFilePath(mapId, ownerId));
		}
	}
}