using Godot;
using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Infrastructure
{
    public partial class GodotTimeService : Node, ITimeService
    {
        public float Scale { get; set; } = 1f;

        private List<ITickable> _subscribers = new();

        public void Register(ITickable tickable) => _subscribers.Add(tickable);

        public void Unregister(ITickable tickable) => _subscribers.Remove(tickable);

        public override void _Process(double delta)
        {
            float scaledDelta = (float)delta * Scale;

            for (int i = 0; i < _subscribers.Count; i++)
            {
                _subscribers[i].OnTick(scaledDelta);
            }
        }
    }
}
