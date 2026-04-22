using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	public class SystemRandom(int seed) : IRandom
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

		public float NextFloat()
		{
			return random.NextSingle();
		}

		public float NextGaussian(float mean, float std)
		{
			float u1 = 1f - random.NextSingle();
			float u2 = 1f - random.NextSingle();

			float z = (float)Math.Sqrt(-2 * Math.Log(u1) * Math.Cos(2 * Math.PI * u2));

			return mean + std * z;
		}

		public bool ProbCodition(float p)
		{
			return NextFloat() < p;
		}

		public T WeightedPick<T>(IEnumerable<T> values, IEnumerable<float> weights)
		{
			float sum = 0;
			foreach (float i in weights)
			{
				sum += i;
			}
			float rd = NextFloat() * sum;
			float c = 0;
			for (int i = 0; i < weights.Count(); i++)
			{
				c += weights.ElementAt(i);
				if (rd < c) return values.ElementAt(i);
			}
			return values.Last();
		}
	}
}
