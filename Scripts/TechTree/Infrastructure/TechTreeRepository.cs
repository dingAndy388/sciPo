using Godot.NativeInterop;
using SciencePotato.Scripts.Common.Infrastructure;
using SciencePotato.Scripts.TechTree.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.TechTree.Infrastructure
{
	public class TechTreeRepository : GenericJsonRepository<TechTree.Domain.TechTree>, ITechTreeRepository
	{
		private string _filePath;

		public TechTreeRepository(string filePath) 
		{
			_filePath = filePath;
		}

		public Domain.TechTree GetTreeById(string mapId, string id)
		{
			base.Load(_filePath+mapId);
			return base.GetById(id);
		}

		public void SaveTree(string mapId, string id, Domain.TechTree tree)
		{
			base.AddOrUpdate(id, tree, _filePath+mapId);
		}
	}
}
