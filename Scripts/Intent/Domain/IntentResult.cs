namespace SciencePotato.Scripts.Intent.Domain
{
	/// <summary>
	/// （v0.9.4 / `WP-5.7`）**意图的处理结果**：成/败 + 失败原因的 **i18n 键**。
	/// <para>为什么只给键不给文案：表现层只允许出现键（`R4` / `D90`），文案由 `Config/Strings.*.json` 提供；
	/// 于是"规则层的拒绝理由"也能双语，且核心程序集里不出现任何中文文案。</para>
	/// </summary>
	public sealed class IntentResult
	{
		private IntentResult(bool ok, string messageKey)
		{
			Ok = ok;
			MessageKey = messageKey;
		}

		/// <summary>意图是否被接受（已开工/已下单/已移动/已决策）。</summary>
		public bool Ok { get; }

		/// <summary>失败原因键（成功时为 <c>null</c>）；取值见 <c>IntentKeys</c>。</summary>
		public string MessageKey { get; }

		public static IntentResult Success() => new IntentResult(true, null);

		public static IntentResult Reject(string messageKey) => new IntentResult(false, messageKey);

		public override string ToString() => Ok ? "ok" : $"rejected({MessageKey})";
	}
}
