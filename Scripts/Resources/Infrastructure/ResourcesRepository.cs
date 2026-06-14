using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Resources.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Resources.Infrastructure
{
	public class ResourcesRepository : GenericJsonRepository<ResourcesPool>, IResourcesRepository
	{
		private string _filePath;

		private const string id = "pool";

		public ResourcesRepository(string filePath)
		{
			this._filePath = filePath;
		}

		public ResourcesPool LoadResourcesPool(string mapId)
		{
			base.Load(_filePath+mapId);
			return base.GetById(id);
		}

		public void SaveResources(string mapId, ResourcesPool pool)
		{
			base.AddOrUpdate(id, pool, _filePath+mapId);
		}
	}
}
