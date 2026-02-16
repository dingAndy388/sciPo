using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	public interface IConfigLoader
	{
		T Load<T>(string path) where T: class;
		IEnumerable<T> LoadAll<T>(string path) where T : class;
	}
}
