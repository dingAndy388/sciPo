using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	public class TaskRepository :  GenericJsonRepository<TaskSnapshot>, ITaskRepository
	{
		private string _filePath;

		public TaskRepository(string filePath)
		{
			this._filePath = filePath;
		}

		public void AddTask(string mapId, TaskSnapshot task)
		{
			base.AddOrUpdate(task.Id, task, _filePath+mapId);
			Save(_filePath + mapId);
		}

		public List<TaskSnapshot> GetCurrentTasks(string mapId)
		{
			Load(_filePath + mapId);
			return base.GetAll();
		}

		public void RemoveTask(string mapId, TaskSnapshot task)
		{
			base.AddOrUpdate(task.Id, null, _filePath+mapId);
			Save(_filePath + mapId);
		}
	}
}
