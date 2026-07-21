using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Habilita o HDRP Water System + SSR no projeto (os lagos do InfiniteTerrain
/// usam uma WaterSurface tipo Pool). Chamado automaticamente pelo EverwyrmAutoSetup.
///
///  Liga em TODOS os HDRP Assets em uso (Graphics default + cada Quality):
///   - supportWater: sem isso a WaterSurface não renderiza;
///   - supportSSR + supportSSRTransparent: reflexo de tela na ÁGUA (dragão,
///     pedras, margens) — SSR e TransparentSSR já estão nos defaults de frame
///     settings da câmera, só o suporte do asset faltava. O reflexo do CÉU
///     (estrelas/lua/nuvens) é o fallback nativo e não depende de SSR.
/// </summary>
public static class WaterSetup
{
    static readonly string[] Props =
    {
        "m_RenderPipelineSettings.supportWater",
        "m_RenderPipelineSettings.supportSSR",
        "m_RenderPipelineSettings.supportSSRTransparent",
    };

    public static void Enable(bool quiet = false)
    {
        var assets = new HashSet<HDRenderPipelineAsset>();
        if (GraphicsSettings.defaultRenderPipeline is HDRenderPipelineAsset def) assets.Add(def);
        for (int i = 0; i < QualitySettings.names.Length; i++)
            if (QualitySettings.GetRenderPipelineAssetAt(i) is HDRenderPipelineAsset q) assets.Add(q);

        if (assets.Count == 0)
        {
            Debug.LogError("Nenhum HDRP Asset encontrado — o projeto está mesmo em HDRP?");
            return;
        }

        int changed = 0;
        foreach (var asset in assets)
        {
            var so = new SerializedObject(asset);
            bool dirty = false;
            foreach (var path in Props)
            {
                var prop = so.FindProperty(path);
                if (prop == null)
                {
                    Debug.LogWarning($"{asset.name}: propriedade {path} não encontrada " +
                                     "(versão do HDRP diferente?). Ative manualmente no asset.");
                    continue;
                }
                if (!prop.boolValue) { prop.boolValue = true; dirty = true; }
            }
            if (dirty)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                changed++;
            }
        }
        if (changed > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"<b>HDRP Water + SSR ativados</b> em {changed} asset(s) — água reflete céu e cena no próximo Play.");
        }
        else if (!quiet)
            Debug.Log("HDRP Water e SSR já estavam ativos em todos os assets.");
    }
}
