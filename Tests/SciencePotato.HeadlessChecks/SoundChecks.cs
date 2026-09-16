using SciencePotato.Scripts.Audio.Domain;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SciencePotato.HeadlessChecks
{
	/// <summary>
	/// （v0.9.11 / `WP-8.4` 前置）**音效契约**的验收检查：id 形态 / 路径口径 / **触发点覆盖** / 清单闭合 / 缺失静默。
	/// <para>为什么这组要在"还没有播放器"的时候就存在：音频由**另一个人/另一个文档**负责，
	/// id 与触发点一旦漂移，等播放器上线时就会变成"文件都有、就是不响"。这组把对接面钉死在代码里。</para>
	/// </summary>
	internal static class SoundChecks
	{
		public static void RunAll()
		{
			Check.Run("WP-8.4 音效契约：id 唯一、非空，且都是小写+下划线（= 文件名）", IdsAreWellFormed);
			Check.Run("WP-8.4 路径口径：`res://Audio/SFX/{id}.wav` 与 `res://Audio/BGM/{id}.ogg`", PathsFollowTheRule);
			Check.Run("WP-8.4 触发点覆盖：8 类领域事件都有音效，未知事件显式不出声", DomainEventsAreCovered);
			Check.Run("WP-8.4 清单闭合：`SoundManifest.csv` 的 id 集合 == 代码里的 id 集合", ManifestIsInSyncWithCatalog);
			Check.Run("WP-8.4 缺失口径：空/未知 id 不抛异常，缺文件只是没声音（可边做边交）", MissingAudioIsSilent);
		}

		private static void IdsAreWellFormed()
		{
			Check.AssertEqual(36, SoundCatalog.SfxIds.Count, "音效条数（规格见 Document/SoundList.md）");
			Check.AssertEqual(1, SoundCatalog.BgmIds.Count, "音乐条数（当前 1 首主曲）");

			foreach (string id in SoundCatalog.SfxIds.Concat(SoundCatalog.BgmIds))
			{
				Check.Assert(!string.IsNullOrWhiteSpace(id), "id 不应为空");
				Check.Assert(Regex.IsMatch(id, "^[a-z0-9_]+$"), $"id `{id}` 应是小写+下划线（= 文件名，不加前后缀）");
			}

			Check.AssertEqual(SoundCatalog.SfxIds.Count, SoundCatalog.SfxIds.Distinct().Count(), "音效 id 不应重复");
		}

		private static void PathsFollowTheRule()
		{
			foreach (string id in SoundCatalog.SfxIds)
				Check.AssertEqual(SoundCatalog.SfxDir + id + SoundCatalog.SfxExtension, SoundCatalog.SfxPath(id), $"音效路径：{id}");

			Check.AssertEqual("res://Audio/SFX/ui_click.wav", SoundCatalog.SfxPath("ui_click"), "音效路径样例");
			Check.AssertEqual("res://Audio/BGM/main.ogg", SoundCatalog.BgmPath("main"), "音乐路径样例");
		}

		private static void DomainEventsAreCovered()
		{
			// 与 Common/Domain/DomainEvents.cs 一一对应：新增事件必须同时决定"响哪个"或"不出声"
			string[] events =
			{
				"BuildingCompletedEvent", "BuildingUpgradedEvent", "UnitTrainedEvent", "UnitDiedEvent",
				"ResearchCompletedEvent", "GameEventTriggeredEvent", "BuildingCapturedEvent", "BuildingRemovedEvent",
			};

			foreach (string name in events)
			{
				string id = SoundCatalog.SfxForDomainEvent(name);
				Check.Assert(!string.IsNullOrWhiteSpace(id), $"领域事件 `{name}` 应有对应音效（或显式登记为不出声）");
				Check.Assert(SoundCatalog.SfxIds.Contains(id), $"`{name}` 映射到的 `{id}` 必须在音效表里");
			}

			Check.Assert(SoundCatalog.SfxForDomainEvent("NoSuchEvent") == null, "未知事件应显式不出声（返回 null 而不是猜一个）");
			Check.Assert(SoundCatalog.SfxForDomainEvent(null) == null, "null 事件名不应抛异常");

			Check.AssertEqual(SoundCatalog.UiClick, SoundCatalog.SfxForIntent(true), "意图被接受 → 点击音");
			Check.AssertEqual(SoundCatalog.UiReject, SoundCatalog.SfxForIntent(false), "意图被拒 → 拒绝音");
		}

		private static void ManifestIsInSyncWithCatalog()
		{
			string path = Path.Combine(Check.FindRepoRoot(), "Document", "SoundManifest.csv");
			Check.Assert(File.Exists(path), $"音频清单应存在：{path}（由 Tools/gen_sound_list.ps1 生成）");

			var manifestIds = File.ReadAllLines(path)
				.Skip(1)
				.Where(line => !string.IsNullOrWhiteSpace(line))
				.Select(line => line.Replace("\"", string.Empty).Split(',')[1])
				.OrderBy(x => x, StringComparer.Ordinal)
				.ToArray();

			var catalogIds = SoundCatalog.SfxIds.Concat(SoundCatalog.BgmIds).OrderBy(x => x, StringComparer.Ordinal).ToArray();
			Check.Assert(manifestIds.SequenceEqual(catalogIds),
				$"清单与代码 id 应完全一致（清单 {manifestIds.Length} 条 / 代码 {catalogIds.Length} 条）—— 音频 id 与代码同源，别手改清单");
		}

		private static void MissingAudioIsSilent()
		{
			Check.Assert(SoundCatalog.SfxPath(null) == null && SoundCatalog.SfxPath("") == null, "空音效 id 应返回 null（调用方据此跳过播放）");
			Check.Assert(SoundCatalog.BgmPath(null) == null && SoundCatalog.BgmPath("  ") == null, "空音乐 id 应返回 null");

			// 路径只是"约定"：文件不存在是常态（音频未交付），调用方按 null/缺文件静默
			string missing = SoundCatalog.SfxPath("definitely_not_delivered_yet");
			Check.Assert(missing != null, "未知 id 仍应给出路径（便于日志/核对），是否播放由文件存在性决定");
			Check.Assert(!missing.Contains(".."), "路径不应包含相对跳转（防目录穿越）");
		}
	}
}