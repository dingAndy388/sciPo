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
        public float Progress;
        public float Target;
        public string Id;
        public string Type;
        public long UId;
		public bool IsCompleted;
    }
}
