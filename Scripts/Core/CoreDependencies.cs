using SciencePotato.Scripts.Common.Domain;
using SciencePotato.Scripts.Map.Domain;
using System;

namespace SciencePotato.Scripts.Core
{
	/// <summary>
	/// （v0.3 / WP-1.3）组合根的入参：所有"与引擎相关"的实现都由宿主（Godot 适配层 / 测试）注入，
	/// 核心只依赖接口，从而可以脱离 Godot 运行（`WIRE-01` / `DEP-07`）。
	/// </summary>
	public sealed class CoreDependencies
	{
		/// <summary>文件系统（存档、user:// 覆写配置）。</summary>
		public IFileSystem FileSystem { get; init; }

		/// <summary>配置表文本来源（Config/*.json）。</summary>
		public IConfigSource ConfigSource { get; init; }

		/// <summary>
		/// 地图存档仓库工厂：由宿主提供（Godot 层用 <c>GodotMapRepository</c>，测试用内存实现）。
		/// <para>之所以用工厂而不是直接实例：地图仓库需要按地形 Id 解析地形，而地形配置由组合根本身产出，
		/// 直接注入会形成"仓库 ↔ 配置"的构造循环；工厂是打断该循环的最小手段（见 §13.z 的装配顺序）。</para>
		/// </summary>
		public Func<ITerrainConfigRepository, IMapRepository> MapRepositoryFactory { get; init; }

		/// <summary>随机源（可注入种子以保证可复现）。</summary>
		public IRandom Random { get; init; }

		/// <summary>
		/// 生成器配置（<c>Config/Generator/Generator.tres</c>）的资源加载器。
		/// 无头环境可留空 —— 此时生成器回退到 <see cref="Map.Domain.GeneratorConfigDto"/> 的默认值。
		/// </summary>
		public IConfigLoader ResourceConfigLoader { get; init; }

		/// <summary>生成器配置目录（Godot 资源路径）。</summary>
		public string GeneratorConfigPath { get; init; } = "res://Config/Generator";

		public string SessionId { get; init; } = "session";
	}
}
