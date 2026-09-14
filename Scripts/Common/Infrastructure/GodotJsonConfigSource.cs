using SciencePotato.Scripts.Common.Domain;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-0.3）配置表读取入口：
	/// 优先 <c>user://Config/{name}.json</c>（可热更 / 可被 Mod 覆写），否则回退 <c>res://Config/{name}.json</c>。
	/// </summary>
	public class GodotJsonConfigSource(IFileSystem fileSystem) : IConfigSource
	{
		private const string UserConfigDir = "user://Config/";
		private const string ResConfigDir = "res://Config/";

		private readonly IFileSystem _fileSystem = fileSystem;

		public string LoadText(string configName)
		{
			if (string.IsNullOrWhiteSpace(configName)) return null;

			string userPath = $"{UserConfigDir}{configName}.json";
			if (_fileSystem.Exists(userPath))
			{
				string userText = _fileSystem.ReadAllText(userPath);
				if (!string.IsNullOrWhiteSpace(userText)) return userText;
			}

			return _fileSystem.ReadAllText($"{ResConfigDir}{configName}.json");
		}
	}
}
