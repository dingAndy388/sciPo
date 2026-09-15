using System.Collections.Generic;

namespace SciencePotato.Scripts.Fog.Domain
{
	public class FogSaveData
	{
		public int OwnerId { get; set; }

		/// <summary>旧口径（v1）：键 = `"q,r"` 的文本矩阵。**读档仍然支持**（老存档不破），新档不再写它。</summary>
		public Dictionary<string, byte> MatrixData { get; set; } = new();

		/// <summary>
		/// （v0.9.7 / `WP-5.5`）**紧凑矩阵**：`FogCodec.Encode` 产出的 Base64 文本（~3 字节/格）。
		/// <para>非空时优先于 <see cref="MatrixData"/>（后者留空 ⇒ 存档体积约为旧格式的 1/3）。</para>
		/// </summary>
		public string Compact { get; set; }

		/// <summary>（v0.9.7 / `WP-5.5`）编码版本（见 `FogCodec.Version`）；空 = 旧格式。</summary>
		public string Encoding { get; set; }
	}
}