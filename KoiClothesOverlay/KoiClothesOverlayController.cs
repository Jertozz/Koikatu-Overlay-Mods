﻿using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using ChaCustom;
using ExtensibleSaveFormat;
using Harmony;
using KoiSkinOverlayX;
using MakerAPI;
using MakerAPI.Chara;
using UnityEngine;
using Logger = BepInEx.Logger;

namespace KoiClothesOverlayX
{
    [RequireComponent(typeof(ChaControl))]
    public class KoiClothesOverlayController : CharaCustomFunctionController
    {
        // todo change based on coord event, maybe listen for updates in ui? or not necessary
        private Dictionary<ClothesTexId, Texture2D> _overlayTextures;

        private ClothesTexId _dumpClothesId;
        private Action<byte[]> _dumpCallback;

        public void SetOverlayTex(Texture2D tex, ClothesTexId texType)
        {
            if (tex == null)
                _overlayTextures.Remove(texType);
            else
                _overlayTextures[texType] = tex;
        }

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
            var data = new PluginData();
            data.version = 1;
            data.data.Add("Overlays", _overlayTextures.Select(x => new KeyValuePair<ClothesTexId, byte[]>(x.Key, x.Value.EncodeToPNG())).ToArray());

            SetExtendedData(data);
        }

        protected override void OnReload(GameMode currentGameMode)
        {
            var data = GetExtendedData();

            if (data?.data != null && data.data.TryGetValue("Overlays", out var obj) && obj is KeyValuePair<ClothesTexId, byte[]>[] overlayBytes)
                _overlayTextures = overlayBytes.ToDictionary(pair => pair.Key, pair => Util.TextureFromBytes(pair.Value));
            else
                _overlayTextures = new Dictionary<ClothesTexId, Texture2D>();
        }

        private void ApplyOverlays(ChaClothesComponent clothesCtrl)
        {
            if (_overlayTextures == null) return;

            var clothesName = clothesCtrl.name;

            var rendererArrs = new[] { clothesCtrl.rendNormal01, clothesCtrl.rendNormal02, clothesCtrl.rendAlpha01, clothesCtrl.rendAlpha02 };

            if (_dumpCallback != null && _dumpClothesId.ClothesName == clothesName)
                DumpBaseTextureImpl(rendererArrs);

            var overlays = _overlayTextures.Where(x => x.Key.ClothesName == clothesName).ToList();

            for (var i = 0; i < rendererArrs.Length; i++)
            {
                var renderers = rendererArrs[i];
                foreach (var overlay in overlays.Where(x => x.Key.RendererGroup == (ClothesRendererGroup)i))
                {
                    if (renderers.Length > overlay.Key.RendererId)
                    {
                        var mat = renderers[overlay.Key.RendererId].material;
                        KoiSkinOverlayController.ApplyOverlay((RenderTexture)mat.mainTexture, overlay.Value);
                    }
                    else
                    {
                        // todo handle properly
                        Logger.Log(LogLevel.Warning | LogLevel.Message, $"Unused clothes overlay for {overlay.Key.ClothesName} - {overlay.Key.RendererGroup} - {overlay.Key.RendererId}");
                    }
                }
            }
        }

        private void DumpBaseTextureImpl(Renderer[][] rendererArrs)
        {
            try
            {
                var renderer = rendererArrs.ElementAtOrDefault((int)_dumpClothesId.RendererGroup)?.ElementAtOrDefault(_dumpClothesId.RendererId);
                if (renderer == null) throw new Exception("Specified renderer doesn't exist");

                var renderTexture = (RenderTexture)renderer.material.mainTexture;

                var act = RenderTexture.active;
                RenderTexture.active = renderTexture;

                var tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false);
                tex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);

                RenderTexture.active = act;

                var png = tex.EncodeToPNG();

                Destroy(tex);

                _dumpCallback(png);
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error | LogLevel.Message, "Dumping texture failed - " + e.Message);
                Logger.Log(LogLevel.Debug, e);
                RenderTexture.active = null;
            }
            finally
            {
                _dumpCallback = null;
            }
        }

        internal static class Hooks
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(ChaControl), nameof(global::ChaControl.ChangeCustomClothes))]
            public static void ChangeCustomClothesPostHook(ChaControl __instance, bool main, int kind)
            {
                var controller = __instance.GetComponent<KoiClothesOverlayController>();
                if (controller == null) return;

                var clothesCtrl = GetClothingRootGo(__instance, main, kind)?.GetComponent<ChaClothesComponent>();
                if (clothesCtrl == null) return;

                controller.ApplyOverlays(clothesCtrl);
            }

            public static void Init(string guid)
            {
                HarmonyInstance.Create(guid).PatchAll(typeof(Hooks));
            }

            private static GameObject GetClothingRootGo(ChaControl __instance, bool main, int kind)
            {
                /* for top clothes it fires once at start with first bool true (main load), then for each subpart with bool false
                * if true, objClothes are used, if false objParts
                * ignore 0 main, handle separate sub parts instead
                */

                if (main)
                {
                    if (kind == 0)
                        return null;

                    return __instance.objClothes.ElementAtOrDefault(kind);
                }

                return __instance.objParts.ElementAtOrDefault(kind);
            }
        }

        public Texture2D GetOverlayTex(ClothesTexId clothesId)
        {
            _overlayTextures.TryGetValue(clothesId, out var tex);
            return tex;
        }

        public void DumpBaseTexture(ClothesTexId clothesId, Action<byte[]> callback, CvsClothes cvsClothes)
        {
            _dumpCallback = callback;
            _dumpClothesId = clothesId;

            // Force redraw to trigger the dump
            cvsClothes.FuncUpdateAllPtnAndColor();
        }
    }
}
