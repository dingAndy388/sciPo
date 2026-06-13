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
        List<TaskSnapshot> GetCurrentTasks();
        void AddTask(TaskSnapshot task);
        void RemoveTask(TaskSnapshot task);
        
    }
}
