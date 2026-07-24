using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Rede de segurança do balanceamento data-driven.
///
///  · <b>Recriar Assets Faltantes</b> — varre TODA subclasse de BalanceProfile e cria o
///    asset padrão de quem estiver faltando (com os defaults da classe). É o que se roda
///    depois de apagar um asset sem querer, ou ao criar um profile novo: escreveu a
///    classe com [BalanceAsset("Balance/X")], rodou o menu, o asset existe.
///  · <b>Selecionar Assets</b> — joga a pasta inteira na seleção, para revisar o tuning
///    num Inspector só.
///
/// Menu: Tools > Everwyrm > Balanceamento
/// </summary>
public static class BalanceSetup
{
    const string ResourcesRoot = "Assets/Scriptables/Resources/";

    [MenuItem("Tools/Everwyrm/Balanceamento — Recriar Assets Faltantes")]
    public static void CreateMissing()
    {
        var created = new List<string>();
        var kept = new List<string>();

        foreach (var type in ProfileTypes())
        {
            var attr = type.GetCustomAttribute<BalanceAssetAttribute>();
            if (attr == null)
            {
                Debug.LogWarning($"{type.Name} não tem [BalanceAsset(\"...\")] — sem isso " +
                                 "não há como saber onde o asset padrão mora. Ignorado.");
                continue;
            }

            string path = ResourcesRoot + attr.resourcePath + ".asset";
            if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) != null) { kept.Add(path); continue; }

            EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            var asset = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(asset, path);
            created.Add(path);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Balance.ClearCache();   // o cache pode guardar o null de antes da criação

        if (created.Count == 0)
            Debug.Log($"<b>Balanceamento OK.</b> Os {kept.Count} assets de balanceamento existem.");
        else
            Debug.Log($"<b>Balanceamento:</b> {created.Count} asset(s) recriado(s) com os " +
                      $"defaults da classe — REVISE o tuning:\n· " + string.Join("\n· ", created));
    }

    [MenuItem("Tools/Everwyrm/Balanceamento — Selecionar Assets")]
    public static void SelectAll()
    {
        var folder = ResourcesRoot + "Balance";
        Selection.objects = AssetDatabase.FindAssets("t:BalanceProfile", new[] { folder })
            .Select(g => AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(o => o != null)
            .Cast<UnityEngine.Object>()
            .ToArray();
        EditorUtility.FocusProjectWindow();
        Debug.Log($"<b>Balanceamento:</b> {Selection.objects.Length} asset(s) selecionado(s) em {folder}.");
    }

    /// <summary>Toda subclasse CONCRETA de BalanceProfile carregada no domínio.</summary>
    static IEnumerable<Type> ProfileTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => !t.IsAbstract && typeof(BalanceProfile).IsAssignableFrom(t))
            .OrderBy(t => t.Name);

    /// <summary>Cria a árvore de pastas nível a nível — o AssetDatabase não faz aninhado.</summary>
    static void EnsureFolder(string folder)
    {
        var parts = folder.Split('/');
        string acc = parts[0];                       // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = acc + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(acc, parts[i]);
            acc = next;
        }
    }
}
