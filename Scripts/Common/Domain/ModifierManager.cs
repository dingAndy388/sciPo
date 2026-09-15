using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
	public class ModifierManager(Dictionary<string, List<ModifierValue>> modifiers)
	{
		private Dictionary<string, List<ModifierValue>> _modifiers = modifiers;

		public void AddModifier(string target, ModifierValue value)
		{
			if (!_modifiers.ContainsKey(target))
			{
				_modifiers[target] = new List<ModifierValue>();
			}
			_modifiers[target].Add(value);
		}

		public void RemoveModifierByTarget(string target, ModifierValue value)
		{
			if (_modifiers.ContainsKey(target))
			{
				_modifiers[target].Remove(value);
				if (_modifiers[target].Count == 0)
				{
					_modifiers.Remove(target);
				}
			}
		}

		/// <summary>
		/// （v0.8.3 / `WP-4.1`）**阶段管道 + 多 target 累加 + 同宿主去重**后的取值。
		/// <para>口径：`result = base`；按 <see cref="ModifierStage"/> **升序**逐阶段结算
		/// `result = (result + ΣAbsolute) × (1 + ΣPercent)`。单阶段时与旧公式逐位一致（旧档/旧调用零影响）。</para>
		/// <para>两点与旧实现不同（`WP-4.1` 有意为之）：① **多 target 全部累加**（旧实现只取第一个命中的 target）
		/// —— 否则"通用 `ResourceLimit`"会被"逐资源 `XxxLimit`"顶掉，科技的上限加成静默失效；
		/// ② **同一宿主（<see cref="ModifierValue.SourceId"/>）对同一 target 的同类修正只算一次**
		/// —— 同一栋建筑的两条相同修正不自我叠加。</para>
		/// </summary>
		public float GetValue(string[] targets, float baseValue) => Aggregate(null, targets, baseValue);

		/// <summary>
		/// （v0.8.3 / `WP-4.1`）**宿主作用域查询**：只结算 `SourceId == hostSourceId` 的修正。
		/// <para>用途：建筑/单位只关心"自身带来的修正"（例如"这栋农田自身 +12 食物/月"），
		/// 而范围效果（`WP-4.2`）与全局科技加成不参与该查询。</para>
		/// </summary>
		public float GetValueForHost(string hostSourceId, string[] targets, float baseValue)
			=> Aggregate(hostSourceId, targets, baseValue);

		private float Aggregate(string hostFilter, string[] targets, float baseValue)
		{
			if (targets == null || targets.Length == 0) return baseValue;

			// 阶段 → (ΣAbsolute, ΣPercent)；`SortedDictionary` 保证阶段升序结算
			var byStage = new SortedDictionary<int, (float Abs, float Per)>();
			var seen = new HashSet<string>();

			foreach (string target in targets)
			{
				if (string.IsNullOrWhiteSpace(target)) continue;
				if (!_modifiers.TryGetValue(target, out List<ModifierValue> list)) continue;

				foreach (ModifierValue modifier in list)
				{
					if (hostFilter != null && modifier.SourceId != hostFilter) continue;

					// 同宿主 + 同目标 + 同类型 ⇒ 只算一次（宿主不自我叠加）
					if (!seen.Add($"{target}|{modifier.Type}|{modifier.SourceId}")) continue;

					byStage.TryGetValue((int)modifier.Stage, out (float Abs, float Per) acc);
					byStage[(int)modifier.Stage] = modifier.Type == ModifierType.Absolute
						? (acc.Abs + modifier.Value, acc.Per)
						: (acc.Abs, acc.Per + modifier.Value);
				}
			}

			float result = baseValue;
			foreach (KeyValuePair<int, (float Abs, float Per)> stage in byStage)
				result = (result + stage.Value.Abs) * (1f + stage.Value.Per);

			return result;
		}

		public void RemoveModifiersBySourceId(string sourceId)
		{
			foreach (var target in _modifiers.Keys.ToList())
			{
				_modifiers[target].RemoveAll(m => m.SourceId == sourceId);
				if (_modifiers[target].Count == 0)
				{
					_modifiers.Remove(target);
				}
			}
		}

		public Dictionary<string, List<ModifierValue>> GetAllModifiers()
		{
			return _modifiers;
		}
	}
}
