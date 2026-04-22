using System.Collections.Generic;

namespace SciencePotato.Scripts.Common.Domain
{
	public interface IConfigLoader
	{
		T Load<T>(string path) where T : class;
		IEnumerable<T> LoadAll<T>(string path) where T : class;
	}
}
