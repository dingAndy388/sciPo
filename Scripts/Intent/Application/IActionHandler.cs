using SciencePotato.Scripts.Intent.Domain;

namespace SciencePotato.Scripts.Intent.Application
{
	/// <summary>
	/// （v0.9.4 / `WP-5.7`）**意图处理器契约**：表现层对它只有两个动作 ——
	/// <list type="number">
	/// <item><see cref="CanRequest"/>：能不能做（用于按钮灰化/提示，不改变世界）；</item>
	/// <item><see cref="Handle"/>：就做（唯一会改变世界的入口）。</item>
	/// </list>
	/// <para>于是"UI 直连应用服务"这件事在**类型层面**消失：表现层拿不到 `ConstructionAppService` 也不该拿
	/// （`CON-02`/`CON-08` 的收口）。AI 不走这里 —— 它有自己的 `IAiActionSink`（`D88`：同一套应用服务，
	/// 不同的"谁下指令"）。</para>
	/// </summary>
	public interface IActionHandler
	{
		/// <summary>该意图现在能否被执行（**只读**，不改世界）。<paramref name="reasonKey"/> = 不能做的原因键。</summary>
		bool CanRequest(PlayerIntent intent, out string reasonKey);

		/// <summary>执行意图。返回值 <see cref="IntentResult.Ok"/> 为假时 <see cref="IntentResult.MessageKey"/> 给出原因键。</summary>
		IntentResult Handle(PlayerIntent intent);
	}
}
