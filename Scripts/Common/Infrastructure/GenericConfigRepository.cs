using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	public class GenericConfigRepository<TInterface, TDto> where TDto : TInterface
	{
		private readonly string _json;
		private TInterface _configData;

		public TInterface Data => _configData;

		public GenericConfigRepository(string json)
		{
			_json = json;
		}

		public void Load()
		{
			if (string.IsNullOrEmpty(_json))
			{
				return;
			}

			TDto dto = JsonConvert.DeserializeObject<TDto>(_json);

			_configData = dto;
		}
	}
}
