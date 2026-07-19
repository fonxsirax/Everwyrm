using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Habilita o HDRP Water System no projeto (os lagos do InfiniteTerrain usam
/// uma WaterSurface tipo Pool). Chamado automaticamente pelo EverwyrmAutoSetup.
///
///  Liga "Water" (m_RenderPipelineSettings.supportWater) em TODOS os HDRP Assets
///  em uso (Graphics default + cada nível de Quality). Sem isso a WaterSurface
///  simplesmente não renderiza — e o HDRP mostra um aviso no componente.
/// </summary>
public static class WaterSetup
{
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
            var prop = so.FindProperty("m_RenderPipelineSettings.supportWater");
            if (prop == null)
            {
                Debug.LogWarning($"{asset.name}: propriedade supportWater não encontrada " +
                                 "(versão do HDRP diferente?). Ative manualmente em " +
                                 "Project Settings > Quality > HDRP > Rendering > Water.");
                continue;
            }
            if (!prop.boolValue)
            {
                prop.boolValue = true;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                changed++;
            }
        }
        if (changed > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"<b>HDRP Water ativado</b> em {changed} asset(s) — os lagos renderizam no próximo Play.");
        }
        else if (!quiet)
            Debug.Log("HDRP Water já estava ativo em todos os assets.");
    }
}
