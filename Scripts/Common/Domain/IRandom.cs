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
		double NextGaussian(double mean, double std);
		double NextDouble();
		bool ProbCodition(double p);
		T WeightedPick<T>(IEnumerable<T> values, IEnumerable<double> weights);
	}
}
