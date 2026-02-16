using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	public class SystemRandom(int seed):IRandom
	{

		 Random random = new(seed);

		public int Next(int min, int max)
		{
			return random.Next(min, max);
		}

		public int Next()
		{
			return random.Next();
		}

		public double NextDouble()
		{
			return random.NextDouble();
		}

		public double NextGaussian(double mean, double std)
		{
			double u1 = 1d - random.NextDouble();
			double u2 = 1d - random.NextDouble();

			double z = Math.Sqrt(-2 * Math.Log(u1) * Math.Cos(2 * Math.PI * u2));

			return mean + std * z;
		}

		public bool ProbCodition(double p)
		{
			return NextDouble()<p;
		}

		public T WeightedPick<T>(IEnumerable<T> values, IEnumerable<double> weights)
		{
			double sum = 0;
			foreach (double i in weights)
			{
				sum += i;
			}
			double rd = NextDouble()*sum;
			double c = 0;
			for (int i = 0; i<weights.Count();i++)
			{
				c += weights.ElementAt(i);
				if (rd < c) return values.ElementAt(i); 
			}
			return values.Last();
		}
	}
}
