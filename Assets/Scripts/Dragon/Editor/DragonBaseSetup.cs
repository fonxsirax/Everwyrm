using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// O laboratório de genética da linhagem. Três ferramentas:
///
///  · <b>Base de Dragões (aleatórios)</b> — gera uma leva de DragonRecord com genoma
///    sorteado em Assets/Data/Dragons e coloca os 4 primeiros na Base da cena.
///  · <b>Mais Dragões Aleatórios</b> — só gera assets novos, sem mexer na cena.
///  · <b>Salvar Dragão Atual</b> — funciona em Play: pega o dragão que a Base está
///    possuindo (inclusive um improvisado aleatório) e o grava como asset. É assim que
///    um sorteio que ficou bonito vira parte permanente do acervo.
///
/// Todos os dragões dividem UMA DragonSkin: a skin define o espaço possível (paleta de
/// materiais do corpo, material das asas, piso do tint) e o genoma de cada record
/// escolhe seu ponto dentro dele. Skins separadas por dragão só fariam sentido para um
/// visual autoral que a genética não deve tocar.
///
/// Requer o avatar Unka na cena com DragonPossession (Tools > Dragão > Setup Completo).
/// </summary>
public static class DragonBaseSetup
{
    const string DataDir = "Assets/Data";
    const string OutDir = DataDir + "/Dragons";
    const string SkinPath = OutDir + "/Skin_Unka.asset";
    const string MatDir = "Assets/Malbers Animations/Dragons/4 - Unka the Dragon/Materials/Realistic";

    /// <summary>Quantos assets a leva inicial gera e quantos vão para a cena.</summary>
    const int BatchCount = 8;
    const int InSceneCount = 4;

    const int MaxIV = 31;

    /// <summary>A paleta de corpo que o genoma sorteia. O pack Malbers é vendorizado,
    /// então o caminho é fixo de propósito.</summary>
    static readonly string[] BodyPalette =
    {
        $"{MatDir}/Unka Body Black.mat",
        $"{MatDir}/Unka Body Brown.mat",
        $"{MatDir}/Unka Body Green.mat",
    };
    const string WingMaterial = MatDir + "/Unka Wings.mat";

    // ==================================================================== SETUP
    [MenuItem("Tools/Everwyrm/Base de Dragões (aleatórios)")]
    public static void SetupBase()
    {
        var avatar = EnsureAvatar();
        if (avatar == null) return;

        var skin = EnsureSkin();
        var records = GenerateRandom(BatchCount, skin);

        var baseGo = Object.FindFirstObjectByType<DragonBase>();
        if (baseGo == null)
        {
            var go = new GameObject("Dragon Base");
            baseGo = go.AddComponent<DragonBase>();
            Undo.RegisterCreatedObjectUndo(go, "Create Dragon Base");
        }

        // só uma parte vai para a cena; o resto fica no acervo para trocas manuais
        var inScene = records.GetRange(0, Mathf.Min(InSceneCount, records.Count));
        baseGo.EditorConfigure(inScene, avatar, null, skin);   // spawn null = troca no lugar

        EditorUtility.SetDirty(baseGo);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Selection.activeObject = inScene.Count > 0 ? inScene[0] : (Object)skin;
        Debug.Log($"<b>Base de Dragões pronta:</b> {records.Count} dragões aleatórios em " +
                  $"{OutDir}, {inScene.Count} deles na cena. Esvazie a lista da Base para " +
                  $"que cada Play sorteie um dragão inédito.");
    }

    [MenuItem("Tools/Everwyrm/Mais Dragões Aleatórios")]
    public static void MoreRandom()
    {
        var skin = EnsureSkin();
        var records = GenerateRandom(BatchCount, skin);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.objects = records.ToArray();
        Debug.Log($"<b>+{records.Count} dragões aleatórios</b> em {OutDir}.");
    }

    // ============================================================ SALVAR O VIVO
    [MenuItem("Tools/Everwyrm/Salvar Dragão Atual")]
    public static void SaveCurrent()
    {
        var lineage = Object.FindFirstObjectByType<DragonBase>();
        var record = lineage != null ? lineage.Current : null;
        if (record == null)
        {
            EditorUtility.DisplayDialog("Salvar Dragão Atual",
                "Nenhum dragão sendo possuído. Dê Play — a Base possui um dragão (ou " +
                "improvisa um aleatório se a lista estiver vazia) e então este menu o grava.",
                "Ok");
            return;
        }

        if (AssetDatabase.Contains(record))
        {
            EditorUtility.SetDirty(record);
            AssetDatabase.SaveAssets();
            Debug.Log($"<b>{record.dragonName}</b> já é asset ({AssetDatabase.GetAssetPath(record)}) — estado gravado.");
            Selection.activeObject = record;
            return;
        }

        EnsureFolders();
        // clona: gravar o record VIVO o transformaria em asset no meio da possessão,
        // e ele seria destruído ao sair do Play junto com a cena.
        var copy = Object.Instantiate(record);
        copy.name = record.dragonName;
        string path = AssetDatabase.GenerateUniqueAssetPath($"{OutDir}/{Sanitize(record.dragonName)}.asset");
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = copy;
        Debug.Log($"<b>{record.dragonName} guardado</b> em {path} — genoma, natureza e IVs " +
                  $"preservados. Arraste-o para a lista da Dragon Base para jogá-lo de novo.");
    }

    // ==================================================================== AVATAR
    /// <summary>Garante que o Unka da cena saiba ser possuído. O DragonPossession
    /// nasceu depois do último setup do prefab (e o menu "Tools > Dragão > Setup
    /// Completo" foi retirado na limpeza de jul/2026), então prefabs antigos chegam
    /// aqui sem ele — em vez de mandar o usuário a um menu que não existe mais, a
    /// própria Base repara o avatar.
    ///
    /// Conserta o PREFAB, não a instância: assim a possessão vale em qualquer cena e
    /// não vira um override solto na Main.</summary>
    static DragonPossession EnsureAvatar()
    {
        var existing = Object.FindFirstObjectByType<DragonPossession>();
        if (existing != null) return existing;

        var controller = Object.FindFirstObjectByType<DragonController>();
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Base de Dragões",
                "Nenhum dragão na cena aberta. Arraste o prefab 'Assets/Prefabs/Unka " +
                "Realistic.prefab' para a cena e rode este menu de novo — ele instala " +
                "sozinho o que faltar (DragonPossession + DragonDissolve).", "Ok");
            return null;
        }

        var go = controller.gameObject;
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
        if (!string.IsNullOrEmpty(prefabPath))
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (root.GetComponent<DragonPossession>() == null) root.AddComponent<DragonPossession>();
                if (root.GetComponent<DragonDissolve>() == null) root.AddComponent<DragonDissolve>();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log($"<b>Avatar reparado:</b> '{prefabPath}' recebeu DragonPossession + DragonDissolve.");
        }
        else
        {
            // objeto solto na cena (não é instância de prefab): conserta no lugar
            Undo.AddComponent<DragonPossession>(go);
            if (go.GetComponent<DragonDissolve>() == null) Undo.AddComponent<DragonDissolve>(go);
            Debug.Log($"<b>Avatar reparado:</b> '{go.name}' recebeu DragonPossession + DragonDissolve.");
        }

        // a instância herda do prefab na hora, mas o Find pode não refletir ainda
        // sem `??`: em Unity ele ignora o == sobrecarregado e engana com objetos destruídos
        var avatar = go.GetComponent<DragonPossession>();
        if (avatar == null) avatar = Object.FindFirstObjectByType<DragonPossession>();
        if (avatar == null)
            Debug.LogError("Base de Dragões: não consegui instalar o DragonPossession no avatar.");
        return avatar;
    }

    // =================================================================== GERAÇÃO
    static List<DragonRecord> GenerateRandom(int count, DragonSkin skin)
    {
        EnsureFolders();
        var records = new List<DragonRecord>(count);
        for (int i = 0; i < count; i++)
        {
            var record = DragonRecord.CreateRandom(DragonNames.Random(), skin, MaxIV);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{OutDir}/{Sanitize(record.dragonName)}.asset");
            AssetDatabase.CreateAsset(record, path);
            records.Add(record);
        }
        return records;
    }

    /// <summary>A skin compartilhada. Reaproveita a existente (para não descartar
    /// ajustes de paleta feitos à mão) e só repõe o que estiver faltando.</summary>
    static DragonSkin EnsureSkin()
    {
        EnsureFolders();
        var skin = AssetDatabase.LoadAssetAtPath<DragonSkin>(SkinPath);
        bool created = skin == null;
        if (created)
        {
            skin = ScriptableObject.CreateInstance<DragonSkin>();
            skin.skinName = "Unka";
            AssetDatabase.CreateAsset(skin, SkinPath);
        }

        if (skin.bodyPalette == null || skin.bodyPalette.Length == 0) skin.bodyPalette = LoadPalette();
        if (skin.wingMaterial == null)
        {
            skin.wingMaterial = AssetDatabase.LoadAssetAtPath<Material>(WingMaterial);
            if (skin.wingMaterial == null)
                Debug.LogWarning($"Base de Dragões: '{WingMaterial}' não encontrado — as asas ficam " +
                                 "com o material atual do avatar (o _Hue genético ainda se aplica).");
        }
        EditorUtility.SetDirty(skin);
        return skin;
    }

    /// <summary>Carrega os materiais de corpo existentes; os que faltarem ficam de fora
    /// (a paleta encolhe em vez de gerar buracos que o genoma sortearia).</summary>
    static Material[] LoadPalette()
    {
        var found = new List<Material>();
        foreach (var path in BodyPalette)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) found.Add(m);
            else Debug.LogWarning($"Base de Dragões: material '{path}' não encontrado — fora da paleta.");
        }
        return found.ToArray();
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(DataDir)) AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder(DataDir, "Dragons");
    }

    static string Sanitize(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Dragao" : name;
    }
}
