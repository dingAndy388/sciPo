using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SciencePotato.Scripts.Common.Infrastructure
{
	/// <summary>
	/// （v0.3 / WP-3.3）<see cref="ISaveStore"/> 的**单文件 JSON 实现**（`I3`、`DEP-06`、`TIME-08`）。
	/// <para>读：首次访问读一次盘（含**版本迁移**：文件版本落后就按 <see cref="SaveMigrations"/> 升级并标脏，
	/// 下次 <see cref="Commit"/> 写回升级后的内容）；写：全部改动先落在内存文档上，
	/// <see cref="Commit"/> 时\"先写临时文件再替换\"（原子）。</para>
	/// <para>为什么读要缓存：存档点可能连续写很多分区（时钟/任务/迷雾/资源/…），
	/// 每次都\"读文件 + 改 + 写回去\"会让 IO 数量翻倍 —— 这是 `WP-1.5` 起就在排查的写放大问题（`TIME-08`）。</para>
	/// <para>宿主无 Godot 时用 <see cref="SystemFileSystem"/>；Godot 里用 `GodotFileSystem`（`user://`），
	/// 因此本类不直接依赖 <c>System.IO</c>。</para>
	/// </summary>
	public sealed class JsonSaveStore : ISaveStore
	{
		/// <summary>临时文件后缀：先写它、再替换正式文件（崩溃时留下的半个存档只可能是这个文件）。</summary>
		public const string TempSuffix = ".tmp";

		private readonly IFileSystem _fileSystem;
		private readonly string _filePath;
		private readonly List<ISaveMigration> _migrations;

		private SaveFile _file;
		private bool _loaded;
		private bool _dirty;

		public JsonSaveStore(IFileSystem fileSystem, string filePath, IEnumerable<ISaveMigration> migrations = null)
		{
			_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
			_filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
			_migrations = (migrations ?? SaveMigrations.Default).OrderBy(m => m.FromVersion).ToList();
		}

		public string FilePath => _filePath;

		/// <summary>显式读盘 + 版本迁移（幂等：只读一次）。</summary>
		public void Load() => EnsureLoaded();

		public int SaveVersion
		{
			get { EnsureLoaded(); return _file.SaveVersion; }
		}

		public double Day
		{
			get { EnsureLoaded(); return _file.Day; }
		}

		public void SetDay(double day)
		{
			EnsureLoaded();
			if (Math.Abs(_file.Day - day) < double.Epsilon) return;
			_file.Day = day;
			_dirty = true;
		}

		/// <summary>**累计落盘次数**（验收/排查用：一次存档点应当只写一次）。</summary>
		public int CommitCount { get; private set; }

		/// <summary>**通过\"替换\"完成的落盘次数**（小于 <see cref="CommitCount"/> 的那一次是首次创建文件）。</summary>
		public int ReplaceCount { get; private set; }

		/// <summary>本次加载实际应用的迁移名（升序）。</summary>
		public IReadOnlyList<string> AppliedMigrations { get; private set; } = new List<string>();

		/// <summary>当前是否有未落盘的改动（排查/用例断言用）。</summary>
		public bool IsDirty { get { EnsureLoaded(); return _dirty; } }

		public bool HasSection(string key)
		{
			if (string.IsNullOrWhiteSpace(key)) return false;
			EnsureLoaded();
			return _file.Sections.ContainsKey(key);
		}

		public string ReadSection(string key)
		{
			if (string.IsNullOrWhiteSpace(key)) return null;
			EnsureLoaded();
			return _file.Sections.TryGetValue(key, out string json) ? json : null;
		}

		public void WriteSection(string key, string json)
		{
			if (string.IsNullOrWhiteSpace(key)) return;
			EnsureLoaded();

			if (json == null) _file.Sections.Remove(key);
			else _file.Sections[key] = json;

			_dirty = true;
		}

		public bool DeleteSection(string key)
		{
			if (string.IsNullOrWhiteSpace(key)) return false;
			EnsureLoaded();

			if (!_file.Sections.Remove(key)) return false;
			_dirty = true;
			return true;
		}

		public IReadOnlyList<string> ListSections()
		{
			EnsureLoaded();
			return _file.Sections.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
		}

		/// <summary>
		/// **原子落盘**：`<存档>.tmp` 写入 → 替换 `<存档>`。替换失败时正式文件保持旧内容
		/// （因此\"断电\"最坏的结果是丢掉最后一次改动，而不是得到一份读不出的存档）。
		/// </summary>
		/// <returns>是否真的写了盘（没有改动时返回 <c>false</c>）。</returns>
		public bool Commit()
		{
			EnsureLoaded();
			if (!_dirty) return false;

			string tempPath = _filePath + TempSuffix;
			string json = JsonConvert.SerializeObject(_file, Formatting.Indented);

			_fileSystem.WriteAllText(tempPath, json);

			bool replaced = _fileSystem.Exists(_filePath);
			_fileSystem.Move(tempPath, _filePath, overwrite: true);

			CommitCount++;
			if (replaced) ReplaceCount++;
			_dirty = false;
			return true;
		}

		/// <summary>
		/// 读盘（只读一次）。**版本高于本程序** → 抛 <see cref="SaveVersionTooNewException"/>（拒绝加载）；
		/// **版本落后** → 依次应用迁移并标脏（下次存档点写回升级后的内容）。
		/// </summary>
		private void EnsureLoaded()
		{
			if (_loaded) return;
			_loaded = true;

			string json = _fileSystem.ReadAllText(_filePath);
			_file = ParseOrEmpty(json);
			ApplyMigrationsIfNeeded();
		}

		private static SaveFile ParseOrEmpty(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new SaveFile { SaveVersion = SaveFile.CurrentVersion };

			try
			{
				JObject document = JObject.Parse(json);

				// 版本头**是否存在**必须与\"值是不是 0/默认值\"分开判断：Newtonsoft 会先跑构造器里的初始值，
				// 因此\"文件里没有 SaveVersion\"与\"文件里写着 3\"反序列化后长得一样 —— 直接看 JSON 才分得清
				// （v1 档没有版本头，必须被识别成\"最老的档\"才会走迁移）。
				bool hasVersion = document.TryGetValue(nameof(SaveFile.SaveVersion), StringComparison.OrdinalIgnoreCase, out _);

				var file = document.ToObject<SaveFile>();
				if (file == null) return new SaveFile { SaveVersion = SaveFile.CurrentVersion };

				file.Sections ??= new Dictionary<string, string>();
				file.Migrations ??= new List<string>();

				if (!hasVersion || file.SaveVersion <= 0) file.SaveVersion = 1; // 无版本头 = v1（最老的单文件档）
				return file;
			}
			catch (JsonException)
			{
				// 损坏的存档：按\"空档\"处理（上层据 SaveVersion/Day=0 判断），不静默改数据、也不让加载崩
				return new SaveFile { SaveVersion = SaveFile.CurrentVersion };
			}
		}

		private void ApplyMigrationsIfNeeded()
		{
			if (_file.SaveVersion > SaveFile.CurrentVersion)
				throw new SaveVersionTooNewException(_file.SaveVersion, SaveFile.CurrentVersion);

			var applied = new List<string>();

			while (_file.SaveVersion < SaveFile.CurrentVersion)
			{
				ISaveMigration migration = _migrations.FirstOrDefault(m => m.FromVersion == _file.SaveVersion);
				if (migration == null) break; // 缺一步迁移：停在当前版本（宁可少迁也不猜）

				migration.Apply(_file);
				_file.Migrations.Add(migration.Name);
				applied.Add(migration.Name);
				_file.SaveVersion = migration.ToVersion;
				_dirty = true;
			}

			AppliedMigrations = applied;
		}
	}
}
