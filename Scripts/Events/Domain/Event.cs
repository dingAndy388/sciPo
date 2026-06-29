using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Events.Domain
{
    public class Event
    {
        public int Id { get; set; }
        public List<Modifier> modifiers { get; set; }
    }
}
