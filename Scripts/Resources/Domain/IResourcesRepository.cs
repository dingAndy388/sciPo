using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SciencePotato.Scripts.Resources.Domain
{
	internal interface IResourcesRepository
	{
		void SaveResources(ResourcesPool pool);
		ResourcesPool LoadResourcesPool(string path);
	}
}
