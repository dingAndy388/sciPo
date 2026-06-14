using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
    public interface ITaskRepository
    {
        List<TaskSnapshot> GetCurrentTasks(string mapId);
        void AddTask(string mapId, TaskSnapshot task);
        void RemoveTask(string mapId, TaskSnapshot task);
    }
}
