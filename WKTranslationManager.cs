using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WKTranslator;

public class WKTranslationManager : MonoBehaviour
{
    public static WKTranslationManager Instance;
    private static readonly int MainTexture = Shader.PropertyToID("_MainTex");
    
    public void Awake()
    {
        if (Instance is not null && Instance != this)
        {
            LogManager.Warn("Destroying duplicate WKTranslationManager");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LogManager.Info("WKTranslationManager Awake");
        DontDestroyOnLoad(gameObject);
        
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public async void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!CanContinueScene(scene.name)) return;

        await PrepareAsync();
        
        ReplaceAllMaterial();
        ReplaceTextures();
        ReplaceAllMaterial();
        ReplaceOnSources();
        
        CommandConsole.AddCommand("startscanner", _ => TextScanner.RunScanner(), false);
        CommandConsole.AddCommand("endscanner", _ => TextScanner.StopScanner(), false);
        CommandConsole.AddCommand("reloadtranslation", _ => Plugin.Instance.ReloadLanguage(), false);
        LogManager.Warn("Added commands");
    }

    private async Task PrepareAsync()
    {
        LogManager.Info($"Scanning scene: {SceneManager.GetActiveScene().name} for static text...");

        // Find ALL TextMeshPro objects (including inactive ones in menus)
        // then filter to ensure they belong to the current scene.
        TMP_Text[] allTmp = Resources.FindObjectsOfTypeAll<TMP_Text>();
        foreach (var txt in allTmp)
        {
            // Safety check: ensure the object is actually part of the loaded scene 
            if (ValidForTranslation(txt.gameObject))
            {
                TryTranslate(txt);
            }
        }

        // Do the same for Legacy Text (just in case)
        Text[] allLegacy = Resources.FindObjectsOfTypeAll<Text>();
        foreach (var txt in allLegacy)
        {
            if (ValidForTranslation(txt.gameObject))
            {
                TryTranslateLegacy(txt);
            }
        }
        
        RectTransform[] rectTransforms = Resources.FindObjectsOfTypeAll<RectTransform>();
        foreach (var rectTransform in rectTransforms)
        {
            rectTransform.ForceUpdateRectTransforms();
        }
        
    }
    
    #region Text Replacement

    private bool ValidForTranslation(GameObject go)
    {
        // We only want to translate objects that are in the scene or DontDestroyOnLoad
        // ignoring "assets" (prefabs) that haven't been instantiated yet.
        return go.scene.isLoaded || go.scene.name == "DontDestroyOnLoad";
    }
    
    // Helper method to apply translation
    public static void TryTranslate(TMP_Text txtComponent)
    {
        if (txtComponent == null || string.IsNullOrEmpty(txtComponent.text)) return;

        if (Plugin.Translations.TryGetValue(txtComponent.text, out string translated))
        {
            txtComponent.text = translated;
            txtComponent.autoSizeTextContainer = true;
            txtComponent.alignment = TextAlignmentOptions.Midline;
            var parentRect = txtComponent.transform.parent.GetComponent<RectTransform>();
            if (parentRect)
                parentRect.ForceUpdateRectTransforms();
        }
    }
    
    // Helper method to apply legacy translation
    public static void TryTranslateLegacy(Text txtComponent)
    {
        if (txtComponent == null || string.IsNullOrEmpty(txtComponent.text)) return;

        if (Plugin.Translations.TryGetValue(txtComponent.text, out string translated))
        {
            txtComponent.text = translated;
            var parentRect = txtComponent.transform.parent.GetComponent<RectTransform>();
            if (parentRect)
                parentRect.ForceUpdateRectTransforms();
        }
    }

    [HarmonyPatch(typeof(TMP_Text))]
    public static class TMPTextPatches
    {
        [HarmonyPatch("text", MethodType.Setter), HarmonyPrefix]
        public static bool PrefixText(TMP_Text __instance, ref string __0)
        {
            if (string.IsNullOrEmpty(__0)) return true;

            if (Plugin.Translations.TryGetValue(__0, out var tr))
            {
                __0 = tr;
            }
            
            return true;
        }
    }
    
    [HarmonyPatch(typeof(Text))]
    public static class LegacyTextPatches
    {
        [HarmonyPatch("text", MethodType.Setter), HarmonyPrefix]
        public static bool PrefixText(Text __instance, ref string __0)
        {
            if (string.IsNullOrEmpty(__0)) return true;

            if (Plugin.Translations.TryGetValue(__0, out var tr))
            {
                __0 = tr;
            }
            
            return true;
        }
    }

    #endregion
    
    #region Material Replacement

    private void ReplaceAllMaterial()
    {
        
        foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (!material.HasProperty(MainTexture)) continue;
            if (material?.mainTexture is null) continue;
            
            LogManager.Debug($"Found {material.mainTexture.name}");
            if (Plugin.TextureRegistry.TryGetValue(material.mainTexture.name, out var tex))
            {
                LogManager.Debug($"Replacing {material.mainTexture.name}");
                material.mainTexture = tex;
            }
                
        }
        
    }
    
    private void ReplaceTextures()
    {
        foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img.sprite is null) continue;
            
            if (!Plugin.TextureRegistry.TryGetValue(img.sprite.name, out var newTex)) continue;

            var oldSprite = img.sprite;
            
            var fullRect = new Rect(0, 0, newTex.width, newTex.height);

            var newSprite = Sprite.Create(
                newTex,
                fullRect,
                new Vector2(0.5f, 0.5f),
                oldSprite.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                oldSprite.border
            );
            
            img.sprite = newSprite;
            img.overrideSprite = newSprite;
            
            if (img.rectTransform is not null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(img.rectTransform);
            
            // LogManager.Debug($"Replaced texture: {img.sprite.name}");
        }
    }
    
    [HarmonyPatch(typeof(Sprite))]
    private static class SpritePatches
    {
        [HarmonyPatch("texture", MethodType.Getter), HarmonyPostfix]
        private static void PostFix_texture(ref Texture2D __result)
        {
            if (__result is null) return;
            if (!Plugin.TextureRegistry.TryGetValue(__result.name, out var texture)) return;
            __result = texture;
        }
    }
    
    static void OverrideRendererTexture(SpriteRenderer sr, Texture2D newTex)
    {
        var mpb = new MaterialPropertyBlock();
        sr.GetPropertyBlock(mpb);
        mpb.SetTexture(MainTexture, newTex);
        sr.SetPropertyBlock(mpb);
        //LogManager.Debug($"[MaterialOverride] {sr.gameObject.name}: _MainTex → {newTex.name}");
    }

    [HarmonyPatch(typeof(SpriteRenderer))]
    private static class SpriteRendererTextureOverridePatch
    {
        // Postfix on the sprite setter so every time `.sprite = ...` runs
        [HarmonyPatch("sprite", MethodType.Setter), HarmonyPostfix]
        private static void Postfix_SetSprite(SpriteRenderer __instance)
        {
            try
            {
                if (__instance is null) return;
                var spr = __instance.sprite;

                var origTexName = spr?.texture?.name;
                if (origTexName == null) return;

                if (Plugin.TextureRegistry.TryGetValue(origTexName, out var replacement) 
                    && replacement is not null)
                {
                    OverrideRendererTexture(__instance, replacement);
                }
            }
            catch (Exception ex)
            {
                LogManager.Debug($"[MaterialOverride] failed on {__instance.name}: {ex}");
            }
        }
    }
    
    #endregion
    
    #region AudioReplacement
    
    private void ReplaceOnSources()
    {
        foreach (var src in FindObjectsOfType<AudioSource>(true))
        {
            if (src.clip is null) continue;

            var clipName = src.clip.GetName();
            if (!Plugin.AudioRegistry.TryGetValue(clipName, out var newClip)) continue;
            if (newClip is null) continue;

            var wasPlaying = src.isPlaying;
            var playingOnAwake = src.playOnAwake;
            src.clip = newClip;
            
            if (wasPlaying || playingOnAwake)
                src.Play();
            LogManager.Debug($"Replaced AudioSource Clip on {src.gameObject.name} ({clipName})");
        }
    }
    
    [HarmonyPatch]
    private static class AudioSourcePatches
    {
        // Patch parameterless Play()
        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.Play), new Type[0])]
        [HarmonyPrefix]
        private static void Play_NoArgs_Postfix(AudioSource __instance)
            => SwapClip(__instance);

        // Patch Play(double delay)
        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.Play), new[] { typeof(double) })]
        [HarmonyPrefix]
        private static void Play_DelayDouble_Postfix(AudioSource __instance)
            => SwapClip(__instance);

        // Patch Play(ulong delaySamples)
        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.Play), new[] { typeof(ulong) })]
        [HarmonyPrefix]
        private static void Play_DelayUlong_Postfix(AudioSource __instance)
            => SwapClip(__instance);

        // Patch PlayOneShot(AudioClip)
        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip) })]
        [HarmonyPrefix]
        private static void PlayOneShot_ClipOnly_Postfix(AudioSource __instance, ref AudioClip __0)
        {
            if (Plugin.AudioRegistry.TryGetValue(__0.name, out var clip))
                __0 = clip;
        }

        // Patch PlayOneShot(AudioClip, float volumeScale)
        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip), typeof(float) })]
        [HarmonyPrefix]
        private static void PlayOneShot_ClipAndVolume_Postfix(AudioSource __instance, ref AudioClip __0)
        {
            if (Plugin.AudioRegistry.TryGetValue(__0.name, out var clip))
                __0 = clip;
        }

        // Shared logic
        private static void SwapClip(AudioSource src)
        {
            if (src?.clip is null) 
                return;

            var name = src.clip.name;
            if (!Plugin.AudioRegistry.TryGetValue(name, out var clip)) 
                return;

            src.clip = clip;
            // LogManager.Debug($"[PlayPatch] Swapped '{name}' → '{clip.name}'");
        }
    }
    
    #endregion
    
    #region Helper Methods
    
    private bool CanContinueScene(string sceneName)
    {
        return sceneName switch
        {
            _ => true
        };
    }
    
    #endregion
}