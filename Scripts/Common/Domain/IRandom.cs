using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Domain
{
	public interface IRandom
	{
		int Next(int min, int max);
		int Next();
		float NextGaussian(float mean, float std);
		float NextFloat();
		bool ProbCodition(float p);
		T WeightedPick<T>(IEnumerable<T> values, IEnumerable<float> weights);
	}
}
