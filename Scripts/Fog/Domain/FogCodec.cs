using SciencePotato.Scripts.Common.Domain;
using System;
using System.Collections.Generic;

namespace SciencePotato.Scripts.Fog.Domain
{
	/// <summary>
	/// （v0.9.7 / `WP-5.5`）**迷雾矩阵的紧凑编码**（`FOG-03`/`FOG-04` 的存档侧）。
	/// <para>旧格式是 `{"3,4": 2, ...}`：每格键 ~6 字节文本 + 冒号空格 + 值 ⇒ 10~14 字节文本/格，
	/// 10k 格的地图在"全图探索"后能到 ~120 KB 且**键是文本**（解析要 split + int.TryParse 每格一次）。</para>
	/// <para>新格式：`q/r` 走 **zigzag varint**（小坐标 1 字节）+ 值 1 字节 ⇒ 3 字节/格，再整体 Base64
	/// （4/3 膨胀）⇒ 文本约为旧格式的 1/3；解码是纯字节游标（没有字符串分配）。</para>
	/// <para>兼容：`FogSaveData.Compact` 为空时仍按旧的 `MatrixData` 读（老存档不破）。</para>
	/// </summary>
	public static class FogCodec
	{
		/// <summary>编码版本标记（写进 `FogSaveData.Encoding`，便于将来再压缩时区分）。</summary>
		public const string Version = "v2-compact";

		/// <summary>把矩阵（只应有"已探索"的格子）编码成 Base64 文本。</summary>
		public static string Encode(IEnumerable<KeyValuePair<HexCubePosition, byte>> cells)
		{
			var bytes = new List<byte>(64);
			int count = 0;
			var positionBuffer = new List<byte>(3);

			foreach (KeyValuePair<HexCubePosition, byte> cell in cells)
			{
				positionBuffer.Clear();
				WriteVarint(positionBuffer, ZigZag(cell.Key.q));
				WriteVarint(positionBuffer, ZigZag(cell.Key.r));
				bytes.AddRange(positionBuffer);
				bytes.Add(cell.Value);
				count++;
			}

			// 头：格子数（varint）—— 解码时可预分配，也便于自校验
			var head = new List<byte>(4);
			WriteVarint(head, (uint)count);
			head.AddRange(bytes);
			return Convert.ToBase64String(head.ToArray());
		}

		/// <summary>把 Base64 文本解码回矩阵格子。</summary>
		public static List<KeyValuePair<HexCubePosition, byte>> Decode(string text)
		{
			var result = new List<KeyValuePair<HexCubePosition, byte>>();
			if (string.IsNullOrEmpty(text)) return result;

			byte[] bytes = Convert.FromBase64String(text);
			int index = 0;
			uint count = ReadVarint(bytes, ref index);

			for (uint i = 0; i < count && index < bytes.Length; i++)
			{
				int q = UnZigZag(ReadVarint(bytes, ref index));
				int r = UnZigZag(ReadVarint(bytes, ref index));
				if (index >= bytes.Length) break;
				byte value = bytes[index++];
				result.Add(new KeyValuePair<HexCubePosition, byte>(new HexCubePosition(q, r), value));
			}

			return result;
		}

		/// <summary>编码后的文本字节数（用例比较"紧凑 vs 旧 JSON"用）。</summary>
		public static int MeasureBytes(string text) => string.IsNullOrEmpty(text) ? 0 : text.Length;

		// ────────────────────────── varint / zigzag ──────────────────────────

		private static uint ZigZag(int value) => (uint)((value << 1) ^ (value >> 31));

		private static int UnZigZag(uint value) => (int)((value >> 1) ^ (uint)(-(int)(value & 1)));

		private static void WriteVarint(List<byte> target, uint value)
		{
			while (value >= 0x80)
			{
				target.Add((byte)(value | 0x80));
				value >>= 7;
			}
			target.Add((byte)value);
		}

		private static uint ReadVarint(byte[] source, ref int index)
		{
			uint result = 0;
			int shift = 0;
			while (index < source.Length)
			{
				byte current = source[index++];
				result |= (uint)(current & 0x7F) << shift;
				if ((current & 0x80) == 0) break;
				shift += 7;
				if (shift > 28) break;
			}
			return result;
		}
	}
}
