using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Application
{
    public interface ITimeService
    {
        void Register(ITickable tickable);
        void Unregister(ITickable tickable);
        float Scale { get; set;}
    }
}
