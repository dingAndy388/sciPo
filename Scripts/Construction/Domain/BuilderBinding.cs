using SciencePotato.Scripts.Common.Domain;
using System;

namespace SciencePotato.Scripts.Construction.Domain
{
	/// <summary>
	/// （v0.3 / WP-2.4）**建造者绑定**：把在建建筑与它的建造者单位绑在一起（设计缺口 `D6`
	/// 「建筑必须由建造者单位建造；每建筑同时仅 1 建造者」）。
	/// <para>用途：① 「每建筑同时仅 1 建造者」的判定依据（非空即已占用）；
	/// ② 完工/拆除时**释放建造者**（把单位从"忙"恢复为"空闲"）—— 旧实现把 <c>IsIdle=false</c> 一置到底，
	/// 单位会永远卡死；③ 读档恢复施工时知道该重新占用哪个单位（`WP-3.2`）。</para>
	/// <para><see cref="OnRelease"/> 是**运行时**回调（由 Units 层提供，落在单位对象上）：
	/// 这样 Construction 模块不必引用 <c>Units.Domain.Unit</c>（避免构造模块 ↔ 单位模块的类型环）。
	/// 它不参与存档（`WP-3.2` 读档后由恢复流程重新绑定）。</para>
	/// </summary>
	public sealed class BuilderBinding
	{
		/// <summary>建造者单位 uid。</summary>
		public string BuilderUId;

		/// <summary>开工时建造者所在格（记录用；也是"相邻格"校验的现场证据）。</summary>
		public HexCubePosition BuilderPosition;

		/// <summary>建筑将要落地的格。</summary>
		public HexCubePosition TargetPosition;

		/// <summary>释放动作（运行时）：把建造者恢复为空闲。</summary>
		public Action OnRelease;

		/// <summary>触发释放（幂等：触发后清空回调，避免重复释放）。</summary>
		public void Release()
		{
			Action release = OnRelease;
			OnRelease = null;
			release?.Invoke();
		}
	}
}