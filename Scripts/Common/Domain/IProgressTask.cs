using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
    public interface IProgressTask : ITickable
    {
        float Progress { get; set; }
        float Target { get; }
        string Id {  get; }
        bool IsCompleted { get; set; }
        string Type { get; }
        string UId { get; }

        TaskSnapshot GetSnapshot();
		event Action OnCompleted;
    }
}
