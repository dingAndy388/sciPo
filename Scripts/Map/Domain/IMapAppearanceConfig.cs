namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.6.0 / WP-5.3）**地图外观参数**：格子步长与贴图目录 —— 让"换美术"只改配置表，不改代码。
	/// <para>背景（`R5`）：改造前 `MapCellView` 把六边形尺寸写死（`height=366`/`width=423`）、
	/// 贴图路径按 <c>res://Texture/Terrain/{Id}.png</c> 拼字符串且**缺图会抛异常** ——
	/// 换一套美术要改代码、改常量、猜文件名；缺一张图会让整张地图渲染中断。</para>
	/// </summary>
	public interface IMapAppearanceConfig
	{
		/// <summary>列步长（相邻 **q** 之间的横向像素）。</summary>
		float CellXStep { get; }

		/// <summary>行步长（相邻 **r** 之间的纵向像素）。</summary>
		float CellYStep { get; }

		/// <summary>地形贴图目录（Godot 资源路径，含结尾斜杠）。</summary>
		string TerrainSpriteDir { get; }
	}
}
