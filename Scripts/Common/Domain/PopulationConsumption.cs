using SciencePotato.Scripts.Map.Domain;

namespace SciencePotato.Scripts.Common.Domain
{
	public class PopulationConsumption(MapCell cell, int amount) : IConsumable
	{
		private readonly MapCell _cell = cell;
		private readonly int _amount = amount;

		public bool IsConsumable()
		{
			return _cell.Population >= _amount;
		}

		public void Consume()
		{
			_cell.SetPopulation(_cell.Population - _amount);
		}
	}
}