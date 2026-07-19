using UnityEditor;
using UnityEngine;

/// <summary>
/// Galeria de diagnóstico: instancia TODOS os prefabs ligados aos arrays
/// PaintTree do InfiniteTerrain numa grade — uma linha por categoria (o nome
/// da linha = nome do campo). Paint trees do terreno não são clicáveis, mas
/// aqui cada um vira um GameObject normal: o que estiver ROSA, clique e leia
/// o nome no Hierarchy e o material/shader no Inspector.
/// Rodar o menu de novo REMOVE a galeria (toggle).
/// </summary>
public static class PrefabGallery
{
    const string RootName = "Prefab Gallery (diagnóstico — apagar depois)";

    [MenuItem("Tools/Everwyrm/Galeria de prefabs (diagnóstico)")]
    public static void Toggle()
    {
        var old = GameObject.Find(RootName);
        if (old != null)
        {
            Object.DestroyImmediate(old);
            Debug.Log("[PrefabGallery] Galeria removida.");
            return;
        }

        var it = Object.FindFirstObjectByType<InfiniteTerrain>(FindObjectsInactive.Include);
        if (it == null)
        {
            Debug.LogError("[PrefabGallery] Nenhum InfiniteTerrain na cena aberta.");
            return;
        }

        var root = new GameObject(RootName);
        Vector3 origin = it.transform.position + new Vector3(0f, 0f, -80f);

        float z = 0f;
        int total = 0;
        foreach (var f in typeof(InfiniteTerrain).GetFields(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (f.FieldType != typeof(InfiniteTerrain.PaintTree[])) continue;
            var arr = (InfiniteTerrain.PaintTree[])f.GetValue(it);
            if (arr == null || arr.Length == 0) continue;

            var row = new GameObject($"— {f.Name} —");
            row.transform.SetParent(root.transform, false);
            row.transform.position = origin + new Vector3(0f, 0f, z);

            float x = 0f;
            foreach (var pt in arr)
            {
                if (pt == null || pt.prefab == null) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pt.prefab);
                inst.name = $"{f.Name} · {pt.prefab.name}";
                inst.transform.SetParent(row.transform);
                inst.transform.position = row.transform.position + new Vector3(x, 0f, 0f);
                x += 8f;
                total++;
            }
            z += 16f;   // uma linha por categoria
        }

        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log($"[PrefabGallery] {total} prefabs na galeria (uma linha por categoria). " +
                  "Os ROSA são os bugados — clique e veja o material no Inspector. " +
                  "Rode o menu de novo para remover. (A galeria NÃO deve ficar na cena salva.)");
    }
}
