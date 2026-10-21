using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace Reed.Scripts.Patches;

public interface IFullCardSubscriber
{
}

// [HarmonyPatch(typeof(NCard),"UpdatePortrait")]
// public static class EHUpdatePortraitPatch
// {
//     [HarmonyPrefix]
//     public static bool Prefix(NCard __instance)
//     {
//         if(__instance.Model is IFullCardSubscriber)
//         {
//             Texture2D portrait = __instance.Model.Portrait;
//             string AncientTextBgPath = ImageHelper.GetImagePath("atlases/compressed_atlas.sprites/ancient_text_bg_" + CardType.Skill.ToString().ToLowerInvariant() + ".png.tres");
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_ancientBorder").GetValue(__instance)).Texture = __instance.Model.AncientBorder;
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_ancientTextBg").GetValue(__instance)).Texture = ResourceLoader.Load<Texture2D>(AncientTextBgPath, null, ResourceLoader.CacheMode.Reuse);
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_ancientPortrait").GetValue(__instance)).Texture = portrait;

//             Material _canvasGroupMaskMaterial = (Material)AccessTools.Field(typeof(NCard),"_canvasGroupMaskMaterial").GetValue(__instance);
//             if (_canvasGroupMaskMaterial == null)
//             {
//                 _canvasGroupMaskMaterial = PreloadManager.Cache.GetMaterial("res://scenes/cards/card_canvas_group_mask_material.tres");
//                 AccessTools.Field(typeof(NCard),"_canvasGroupMaskMaterial").SetValue(__instance,_canvasGroupMaskMaterial);
//             }
//             ((CanvasGroup)AccessTools.Field(typeof(NCard),"_portraitCanvasGroup").GetValue(__instance)).Material = _canvasGroupMaskMaterial;
//             return false;
//         }
//         return true;
//     }
// }

// [HarmonyPatch(typeof(NCard),"ReloadOverlay")]
// public static class EHReloadOverlayPatch
// {
//     private static readonly string EHBannerPath = "res://Reed/images/bg/reed_ancient_border2.png";

//     [HarmonyPrefix]
//     public static void Postfix(NCard __instance)
//     {
//         if(__instance.Model is IFullCardSubscriber)
//         {
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_portraitBorder").GetValue(__instance)).Visible = false;
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_portrait").GetValue(__instance)).Visible = false;
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_frame").GetValue(__instance)).Visible = false;
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_banner").GetValue(__instance)).Texture = ResourceLoader.Load<Texture2D>(EHBannerPath, null, ResourceLoader.CacheMode.Reuse);
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_ancientBorderGlassOverlay").GetValue(__instance)).Visible = true;
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_ancientBorder").GetValue(__instance)).Visible = true;
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_ancientTextBg").GetValue(__instance)).Visible = true;
//             ((TextureRect)AccessTools.Field(typeof(NCard),"_ancientPortrait").GetValue(__instance)).Visible = true;
//             //((Control)AccessTools.Field(typeof(NCard),"_ancientBanner").GetValue(__instance)).Visible = true;
//         }
//     }
// }