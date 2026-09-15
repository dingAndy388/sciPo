using System;

namespace SciencePotato.Scripts.Map.Domain
{
	/// <summary>
	/// （v0.6.0 / WP-5.3）**#RRGGBB / #RRGGBBAA 颜色值**。
	/// <para>为什么不用 `Godot.Color`：核心层（Domain）不得依赖引擎类型（§16 的引擎耦合点收敛），
	/// 而且无头用例要能直接验"颜色解析对不对" —— 自己实现一个 32 位颜色既可以脱离引擎测试，
	/// 也让"配置里写错颜色"能在启动校验里被抓住（而不是渲染时才变色）。</para>
	/// </summary>
	public readonly struct RgbColor(byte r, byte g, byte b, byte a)
	{
		public byte R { get; } = r;
		public byte G { get; } = g;
		public byte B { get; } = b;
		public byte A { get; } = a;

		/// <summary>是否完全不透明（存档/日志里可省掉 alpha）。</summary>
		public bool IsOpaque => A == 255;

		/// <summary>
		/// 解析 <c>#RGB</c> / <c>#RRGGBB</c> / <c>#RRGGBBAA</c>（也接受不带 <c>#</c> 的写法）；失败返回 <c>false</c> 并给出 <paramref name="error"/>。
		/// <para>刻意**不接受**颜色名（"red"）：配置里有两种写法就会出现两种口径，校验器也就抓不住拼写错误。</para>
		/// </summary>
		public static bool TryParse(string text, out RgbColor color, out string error)
		{
			color = default;
			error = null;

			if (string.IsNullOrWhiteSpace(text))
			{
				error = "为空";
				return false;
			}

			string hex = text.Trim();
			if (hex.StartsWith("#", StringComparison.Ordinal)) hex = hex.Substring(1);

			if (hex.Length == 3)
				hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";

			if (hex.Length != 6 && hex.Length != 8)
			{
				error = $"长度应为 3/6/8 位十六进制，实际 {hex.Length} 位（{text}）";
				return false;
			}

			if (!TryByte(hex, 0, out byte r) || !TryByte(hex, 2, out byte g) || !TryByte(hex, 4, out byte b))
			{
				error = $"含有非十六进制字符（{text}）";
				return false;
			}

			byte a = 255;
			if (hex.Length == 8 && !TryByte(hex, 6, out a))
			{
				error = $"alpha 段非法（{text}）";
				return false;
			}

			color = new RgbColor(r, g, b, a);
			return true;
		}

		private static bool TryByte(string hex, int offset, out byte value)
			=> byte.TryParse(hex.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber,
							 System.Globalization.CultureInfo.InvariantCulture, out value);

		/// <summary>是否适合写进 JSON（用于配置回写/日志）。</summary>
		public string ToHex() => IsOpaque ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

		public override string ToString() => ToHex();

		public bool Equals(RgbColor other) => R == other.R && G == other.G && B == other.B && A == other.A;

		public override bool Equals(object obj) => obj is RgbColor other && Equals(other);

		public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;

		public static bool operator ==(RgbColor left, RgbColor right) => left.Equals(right);

		public static bool operator !=(RgbColor left, RgbColor right) => !left.Equals(right);
	}
}
