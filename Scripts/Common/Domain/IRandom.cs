using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	public interface IRandom
	{
		int Next(int max, int min);
		int Next();
		float NextGaussian(float mean, float std);
		float NextFloat();
		bool ProbCodition(float p);
		T WeightedPick<T>(IEnumerable<T> values, IEnumerable<float> weights);
	}
}
