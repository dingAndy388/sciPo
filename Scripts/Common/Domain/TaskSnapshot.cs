using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
    public class TaskSnapshot
    {
        public float Progess;
        public string Id;
        public bool IsCompleted;
    }
}
