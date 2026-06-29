using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.Resources.Domain;

namespace SciencePotato.Scripts.Resources.Infrastructure
{
	public class ResourcesConfigRepository : GenericConfigRepository<IResourcesPoolConfig, ResourcesPoolConfigDto>, IResourcesConfigRepository
	{
		private IResourcesPoolConfig _config;

		public ResourcesConfigRepository(string json) : base(json)
		{
			base.Load();
			_config = base.Data;
		}

		public IResourcesPoolConfig GetResourcesPoolConfig()
		{
			return _config;
		}
	}
}