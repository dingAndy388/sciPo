using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
    public class IntervalTask(float progress, float interval, string id, string type, long uid): IProgressTask
    {
        public float Progress { get; set; } = progress;

        public float Target { get; set; } = interval;

        public string Id { get; set; } = id;

        public bool IsCompleted { get; set; } = false;

        public string Type { get; set; } = type;

        public long UId { get; set; } = uid;

        public event Action OnCompleted;

        public TaskSnapshot GetSnapshot()
        {
            return new TaskSnapshot
            {
                Progress = Progress,
                Target = Target,
                Id = Id,
                Type = Type,
                IsCompleted = IsCompleted
            };
        }

        public void OnTick(float delta)
        {
            if (!IsCompleted)
            { 
                Progress += delta;
                if(Progress>=Target)
                {
                    Progress -= Target;
                    OnCompleted?.Invoke();
                }
            }
        }
    }
}
