using UnityEditor;
using UnityEngine;
using Attr = DragonAttributes.Attribute;

/// <summary>
/// <b>Balanço do Dragão</b> — simulador de potência por fase da vida, SEM entrar
/// em Play (Tools > Everwyrm > Balanço do Dragão).
///
/// Lê os componentes do PREFAB (DragonAttributes/Growth/Controller/Vitals) e o
/// FlightProfile e roda as MESMAS contas puras do runtime (SnapshotAt,
/// MaturityFor, ScaleAt, FlapLiftMulAt...). Mexeu numa curva ou num ganho por
/// ponto no Inspector do prefab? A tabela reflete ao focar a janela.
///
/// Rework de 6 atributos: os sliders e as colunas cobrem Força · Chama · Agilidade
/// · Vigor · Fôlego · Instinto. O IV de Vigor também é o que alonga a vida.
///
/// A coluna "Potência" é um índice grosseiro (dano × vida × velocidade) só para
/// comparar fases e dragões entre si — não é um número de design.
/// </summary>
public class DragonBalanceWindow : EditorWindow
{
    const string PrefabPath = "Assets/Prefabs/Unka Realistic.prefab";
    const string ProfilePath = "Assets/Scriptables/Resources/Balance/FlightProfile.asset";

    GameObject prefab;
    DragonAttributes attrs;
    DragonGrowth growth;
    DragonController controller;
    DragonVitals vitals;
    FlightProfile profile;

    DragonNature nature = DragonNature.Balanced;
    // um IV por atributo (índice = DragonAttributes.Attribute)
    readonly int[] iv = { 16, 16, 16, 16, 16, 16 };
    float condition = 0.5f;
    bool wellFed = true;
    Vector2 scroll;

    static readonly string[] IvLabels =
        { "IV Agilidade", "IV Força", "IV Vigor (vida longa)", "IV Chama", "IV Fôlego", "IV Instinto" };

    [MenuItem("Tools/Everwyrm/Balanço do Dragão (simulador de fases)")]
    public static void Open()
    {
        var w = GetWindow<DragonBalanceWindow>("Balanço do Dragão");
        w.minSize = new Vector2(900f, 440f);
        w.Reload();
    }

    void OnFocus() => Reload();

    void Reload()
    {
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        profile = AssetDatabase.LoadAssetAtPath<FlightProfile>(ProfilePath);
        if (prefab == null) return;
        attrs = prefab.GetComponentInChildren<DragonAttributes>(true);
        growth = prefab.GetComponentInChildren<DragonGrowth>(true);
        controller = prefab.GetComponentInChildren<DragonController>(true);
        vitals = prefab.GetComponentInChildren<DragonVitals>(true);
    }

    int IvVigor => iv[(int)DragonAttributes.Attribute.Vigor];

    void OnGUI()
    {
        if (attrs == null || growth == null || controller == null || vitals == null)
        {
            EditorGUILayout.HelpBox(
                $"Não achei os componentes do dragão em {PrefabPath}.\n" +
                "A janela lê os valores serializados do prefab — se ele mudou de lugar, " +
                "ajuste PrefabPath em DragonBalanceWindow.cs.", MessageType.Error);
            if (GUILayout.Button("Recarregar")) Reload();
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Dragão simulado", EditorStyles.boldLabel);
        nature = (DragonNature)EditorGUILayout.EnumPopup("Natureza", nature);
        EditorGUILayout.LabelField(" ", DragonNatureTable.Describe(nature), EditorStyles.miniLabel);

        int maxIV = attrs.MaxIV;
        for (int i = 0; i < iv.Length; i++)
            iv[i] = EditorGUILayout.IntSlider(IvLabels[i], iv[i], 0, maxIV);

        EditorGUILayout.Space(4f);
        condition = EditorGUILayout.Slider(
            new GUIContent("Condição corporal", "0 esquelético · 0.5 saudável · 1 gordo — " +
                                                "é o peso, e é ele que manda na subida do voo"),
            condition, 0f, 1f);
        wellFed = EditorGUILayout.Toggle(
            new GUIContent("Bem alimentado", "Ligado: cresce no ritmo cheio. Desligado: " +
                                             "cresce devagar, então amadurece mais pela IDADE que pelo tamanho"),
            wellFed);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("IVs no mínimo (0)")) SetAllIv(0);
            if (GUILayout.Button("IVs medianos")) SetAllIv(maxIV / 2);
            if (GUILayout.Button("IVs perfeitos")) SetAllIv(maxIV);
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Potência por fase da vida", EditorStyles.boldLabel);
        DrawTable();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Tempo de vida", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"Velhice começa em {growth.OldAgeSecondsFor(IvVigor) / 60f:0.0} min · " +
            $"morte natural em {growth.LifespanSecondsFor(IvVigor) / 60f:0.0} min " +
            $"(Vigor {IvVigor} alonga x{growth.LifespanMulFor(IvVigor):0.00})");

        if (profile != null)
            EditorGUILayout.HelpBox(
                $"Voo: cada batida de Space soma {profile.flapLift:0.0} m/s × peso, com teto de " +
                $"{profile.maxRiseSpeed:0.0} m/s × peso. Estes números vivem em {ProfilePath}.",
                MessageType.Info);

        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(
            "As curvas de maturação (quando cada atributo chega) estão no componente " +
            "DragonAttributes do prefab. Selecione o prefab, mexa numa curva e volte " +
            "aqui: a tabela recalcula ao focar a janela.", MessageType.None);
        if (GUILayout.Button("Selecionar o prefab do dragão")) Selection.activeObject = prefab;

        EditorGUILayout.EndScrollView();
    }

    void SetAllIv(int v) { for (int i = 0; i < iv.Length; i++) iv[i] = v; }

    // ------------------------------------------------------------- TABELA
    void DrawTable()
    {
        string[] headers = { "Fase", "Mat.", "For", "Cha", "Agi", "Vig", "Fôl", "Ins",
                             "Corrida", "Voo", "Vida", "Energia", "Teto", "Dano", "Subida", "Peso", "Potência" };
        float[] widths = { 74f, 44f, 38f, 38f, 38f, 38f, 38f, 38f,
                           62f, 52f, 48f, 56f, 50f, 46f, 56f, 58f, 60f };

        using (new EditorGUILayout.HorizontalScope())
            for (int i = 0; i < headers.Length; i++)
                EditorGUILayout.LabelField(headers[i], EditorStyles.miniBoldLabel, GUILayout.Width(widths[i]));

        foreach (var stage in Stages())
            DrawRow(stage, widths);
    }

    void DrawRow((string label, float growth01, float age01, float elder01) stage, float[] w)
    {
        // sem comer bem o corpo cresce pouco: o crescimento cai, a idade não
        float g = wellFed ? stage.growth01 : stage.growth01 * 0.45f;

        float maturity = attrs.MaturityFor(g, stage.age01, stage.elder01);
        var s = attrs.SnapshotAt(maturity, nature, iv);

        float elderMul = growth.ElderMulFor(stage.elder01);
        float scale = growth.ScaleAt(g);
        float bodyScale = scale * s.speedMul;                       // o "S" do DragonController
        float run = controller.BaseRunSpeed * growth.RunSpeedMulAt(condition) * elderMul * bodyScale;
        float fly = controller.BaseMaxFlySpeed * bodyScale;
        float health = vitals.BaseMaxHealth * s.healthMul;
        float energy = vitals.BaseMaxEnergy * s.energyMul;
        float lift = (profile != null ? profile.flapLift : 4.25f) *
                     growth.FlapLiftMulAt(g, condition) * elderMul;
        float weight = growth.WeightAt(g, condition);
        float power = s.damageMul * (health / 100f) * (run / 10f);  // índice comparativo

        using (new EditorGUILayout.HorizontalScope())
        {
            int i = 0;
            Cell(stage.label, w[i++], EditorStyles.miniBoldLabel);
            Cell($"{maturity * 100f:0}%", w[i++]);
            Cell($"{s.Get(Attr.Might):0.0}", w[i++]);
            Cell($"{s.Get(Attr.Ardor):0.0}", w[i++]);
            Cell($"{s.Get(Attr.Agility):0.0}", w[i++]);
            Cell($"{s.Get(Attr.Vigor):0.0}", w[i++]);
            Cell($"{s.Get(Attr.Wind):0.0}", w[i++]);
            Cell($"{s.Get(Attr.Instinct):0.0}", w[i++]);
            Cell($"{run:0.0}", w[i++]);
            Cell($"{fly:0.0}", w[i++]);
            Cell($"{health:0}", w[i++]);
            Cell($"{energy:0}", w[i++]);
            Cell($"{s.maxAltitude:0}", w[i++]);
            Cell($"x{s.damageMul:0.00}", w[i++]);
            Cell($"{lift:0.0}", w[i++]);
            Cell($"{weight:0}", w[i++]);
            Cell($"{power:0.0}", w[i], EditorStyles.miniBoldLabel);
        }
    }

    static void Cell(string text, float width, GUIStyle style = null) =>
        EditorGUILayout.LabelField(text, style ?? EditorStyles.miniLabel, GUILayout.Width(width));

    /// <summary>Pontos representativos da vida: crescimento, fração da idade até a
    /// velhice e progresso da velhice. Os limiares de fase vêm do DragonGrowth.</summary>
    (string, float, float, float)[] Stages() => new (string, float, float, float)[]
    {
        ("Recém-nato", 0f, 0f, 0f),
        ("Filhote", growth.AdultAt * 0.5f, 0.15f, 0f),
        ("Adulto", (growth.AdultAt + growth.ColossalAt) * 0.5f, 0.45f, 0f),
        ("Colossal", 1f, 0.8f, 0f),
        ("Auge", 1f, 1f, 0f),
        ("Ancião", 1f, 1f, 1f),
    };
}
