using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Ficha do dragão (Tab). É uma TELA DE LEITURA: não há pontos para gastar, os SEIS
/// atributos crescem sozinhos com a maturidade (DragonAttributes). A ficha existe
/// para o jogador entender o bicho que pilota — e é a ferramenta de balanceamento
/// mais rápida em jogo:
///
///  · ATRIBUTOS — 6 barras 0..10 com a maturidade que os move;
///  · DEGRAU    — a barra de maturidade que desbloqueia ataques;
///  · CORPO/LINHAGEM — natureza, os 6 IVs, traços, mutações, peso, idade;
///  · DERIVADOS — velocidades, vida/energia, teto, subida por batida, faro;
///  · PROJEÇÃO  — quanto ESTE dragão terá em cada fase da vida (estimativa).
///
/// Fora do Play, a mesma conta com mais detalhe está em Tools > Everwyrm >
/// Balanço do Dragão. Pausa o jogo (timeScale 0) e libera o cursor.
/// </summary>
public class DragonStatsMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    DragonAttributes attrs;
    DragonGrowth growth;
    DragonVitals vitals;
    DragonController dragon;
    DragonFlight flight;
    DragonAbilities abilities;   // opcional — prévia do próximo golpe a desbloquear
    DragonTraits traits;         // opcional — traços herdáveis

    GameObject panel;
    Text headerText, bodyText, statsText, tierText, projText;
    RectTransform tierBar;
    readonly Text[] attrValues = new Text[Order.Length];
    readonly RectTransform[] attrBars = new RectTransform[Order.Length];

    /// <summary>Ordem de exibição dos atributos na ficha (Força primeiro, etc.).</summary>
    static readonly DragonAttributes.Attribute[] Order =
    {
        DragonAttributes.Attribute.Might,
        DragonAttributes.Attribute.Ardor,
        DragonAttributes.Attribute.Agility,
        DragonAttributes.Attribute.Vigor,
        DragonAttributes.Attribute.Wind,
        DragonAttributes.Attribute.Instinct,
    };
    static readonly string[] AttrNames = { "FORÇA", "CHAMA", "AGILIDADE", "VIGOR", "FÔLEGO", "INSTINTO" };
    static readonly string[] AttrShort = { "For", "Cha", "Agi", "Vig", "Fôl", "Ins" };

    // ---- geometria do card: duas colunas com o mesmo padding dos dois lados,
    // tudo posicionado pela borda esquerda (ver TopLeft) para nunca vazar da coluna.
    const float CardW = 900f, CardH = 740f, Padding = 40f, ColumnGutter = 40f;
    const float LeftX = -(CardW * 0.5f) + Padding;                 // -410
    const float ColumnW = (CardW * 0.5f) - Padding - (ColumnGutter * 0.5f); // 390
    const float RightX = ColumnGutter * 0.5f;                      // 20
    const float ContentW = CardW - Padding * 2f;                   // 820 (título/header/footer)

    public void Bind(DragonVitals v, DragonController d)
    {
        vitals = v;
        dragon = d;
        growth = v.GetComponent<DragonGrowth>();
        attrs = v.GetComponent<DragonAttributes>();
        flight = v.GetComponent<DragonFlight>();
        abilities = v.GetComponent<DragonAbilities>();
        traits = v.GetComponent<DragonTraits>();
        if (attrs == null) { enabled = false; return; }

        Build();
        SetOpen(false);

        attrs.OnChanged += OnAttrsChanged;
    }

    void OnDestroy()
    {
        if (attrs != null) attrs.OnChanged -= OnAttrsChanged;
        if (IsOpen) SetOpen(false); // nunca deixar o jogo pausado
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) SetOpen(!IsOpen);
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
    }

    void OnAttrsChanged(DragonAttributes a) { if (IsOpen) Refresh(); }

    void SetOpen(bool open)
    {
        IsOpen = open;
        panel.SetActive(open);
        Time.timeScale = open ? 0f : 1f;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
        if (open) Refresh();
    }

    // ------------------------------------------------------------- REFRESH
    void Refresh()
    {
        var a = attrs;
        var s = a.Current;

        string dragonName = DragonBase.Instance != null && DragonBase.Instance.Current != null
            ? DragonBase.Instance.Current.dragonName : "—";

        // na Velhice a maturidade EXIBIDA cai (elderDecline), mas o Tier ficou com o
        // PICO — sem isto pareceria bug ("por que ainda tenho os golpes?").
        bool declined = growth.IsElder && a.PeakMaturity01 - s.maturity01 > 0.02f;
        headerText.text = $"{dragonName}   ·   {StageLabel(growth.Stage)}   ·   " +
                          $"maturidade {s.maturity01 * 100f:0}%" +
                          (declined ? $" (pico {a.PeakMaturity01 * 100f:0}%)" : "");
        headerText.color = growth.IsElder ? new Color(0.85f, 0.72f, 0.5f) : Color.white;

        tierText.text = a.Tier >= a.MaxTier
            ? $"Degrau de maturidade: {a.Tier}/{a.MaxTier} (máximo — todo golpe por idade já é seu)"
            : $"Degrau de maturidade: {a.Tier}/{a.MaxTier}   ·   {a.TierProgress01 * 100f:0}% para o próximo";
        tierBar.sizeDelta = new Vector2(ColumnW * a.TierProgress01, 8f);

        for (int i = 0; i < Order.Length; i++)
        {
            float val = s.Get(Order[i]);
            attrValues[i].text = $"{val:0.0}";
            attrBars[i].sizeDelta = new Vector2(
                ColumnW * Mathf.Clamp01(val / Mathf.Max(0.01f, a.MaxAttribute)), 11f);
        }

        bodyText.text =
            $"Natureza: {DragonNatureTable.Describe(a.Nature)}\n" +
            $"IVs (talento): {IvLine()}\n" +
            $"Traços: {TraitLine()}\n" +
            $"Mutações: {MutationLine()}\n" +
            $"Peso: {growth.WeightKg:0} kg ({ConditionLabel(growth.Condition01)}) · {growth.Scale:0.00}x\n" +
            $"Idade: {FormatAge(growth.AgeSeconds)} de ~{FormatAge(growth.LifespanSeconds)}\n" +
            $"Carne comida: {vitals.TotalEaten:0}\n" +
            $"Próximo golpe: {NextUnlockLabel()}";

        statsText.text =
            $"Corrida máx: {dragon.MaxGroundSpeed:0.0} m/s\n" +
            $"Voo máx: {dragon.MaxFlightSpeed:0.0} m/s\n" +
            $"Vida máx: {vitals.MaxHealthEff:0}\n" +
            $"Energia máx: {vitals.MaxEnergyEff:0}\n" +
            (flight != null ? $"Subida por batida: {flight.FlapLift:0.0} m/s\n" : "") +
            $"Teto de voo: {(flight != null ? flight.Ceiling : s.maxAltitude):0} m\n" +
            $"Fome: -{vitals.HungerDecayEff * 60f:0.0}/min\n" +
            $"Faro (dominância): {s.dominanceRadius:0} m\n" +
            $"Dano: x{s.damageMul:0.00}   Chama: x{s.flameMul:0.00}";

        RefreshProjection();
    }

    /// <summary>Uma linha com os 6 IVs (talento bruto por atributo).</summary>
    string IvLine() =>
        string.Join(" ", Order.Select((attr, i) => $"{AttrShort[i]}{attrs.Iv(attr)}"));

    /// <summary>Os traços ativos (nome + efeito). "nenhum" se não houver.</summary>
    string TraitLine()
    {
        if (traits == null || traits.Active.Count == 0) return "nenhum";
        return string.Join(" · ", traits.Active.Select(DragonTraitInfo.Name));
    }

    string MutationLine()
    {
        var rec = DragonBase.Instance != null ? DragonBase.Instance.Current : null;
        if (rec == null || rec.mutations <= 0) return "nenhuma";
        // no máx. 1 mutação por ninhada: ou o slot extra (preferido), ou um outlier de stat
        return rec.bonusAttackSlots > 0
            ? "slot de ataque extra (prodígio da linhagem)"
            : "talento acima do teto (prodígio da linhagem)";
    }

    /// <summary>Quanto ESTE dragão terá de cada atributo em cada fase da vida. Uma
    /// linha por fase, os 6 atributos concatenados (estimativa; assume comer bem).</summary>
    void RefreshProjection()
    {
        var sb = new System.Text.StringBuilder();
        int[] iv = IvArray();
        foreach (var (label, g, age, elder) in ProjectionStages())
        {
            float m = attrs.MaturityFor(g, age, elder);
            var p = attrs.SnapshotAt(m, attrs.Nature, iv);
            sb.Append($"{label,-9}");
            for (int i = 0; i < Order.Length; i++)
                sb.Append($" {AttrShort[i]}{p.Get(Order[i]):0.0}");
            sb.Append('\n');
        }
        projText.text = sb.ToString();
    }

    /// <summary>Os 6 IVs deste dragão no formato que SnapshotAt espera (índice = Attribute).</summary>
    int[] IvArray()
    {
        var iv = new int[DragonAttributes.Count];
        for (int i = 0; i < DragonAttributes.Count; i++)
            iv[i] = attrs.Iv((DragonAttributes.Attribute)i);
        return iv;
    }

    (string label, float growth, float age, float elder)[] ProjectionStages() => new[]
    {
        ("Filhote", growth.AdultAt * 0.5f, 0.15f, 0f),
        ("Adulto", (growth.AdultAt + growth.ColossalAt) * 0.5f, 0.45f, 0f),
        ("Colossal", 1f, 0.8f, 0f),
        ("Ancião", 1f, 1f, 1f),
    };

    static string StageLabel(DragonGrowth.LifeStage s) => s switch
    {
        DragonGrowth.LifeStage.Hatchling => "Filhote",
        DragonGrowth.LifeStage.Adult => "Adulto",
        DragonGrowth.LifeStage.Colossal => "Colossal",
        _ => "Ancião",
    };

    /// <summary>Primeiro ataque ainda travado — DragonAbilities.Known já vem
    /// ordenado por unlockLevel (degrau).</summary>
    string NextUnlockLabel()
    {
        if (abilities == null || abilities.Known.Count == 0) return "—";
        foreach (var k in abilities.Known)
            if (!abilities.Unlocked.Contains(k)) return $"{k.attackName} (degrau {k.unlockLevel})";
        return "todos os golpes já são seus";
    }

    static string ConditionLabel(float c) =>
        c < 0.3f ? "Magro" : c < 0.68f ? "Saudável" : "Gordo";

    static string FormatAge(float s) =>
        s < 60f ? $"{s:0} s" : $"{Mathf.FloorToInt(s / 60f)} min {Mathf.FloorToInt(s % 60f)} s";

    // --------------------------------------------------------------- BUILD
    void Build()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var canvasGo = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10; // acima do HUD
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        panel = new GameObject("Panel", typeof(Image));
        panel.transform.SetParent(canvasGo.transform, false);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        var pRt = panel.GetComponent<RectTransform>();
        pRt.anchorMin = Vector2.zero; pRt.anchorMax = Vector2.one;
        pRt.offsetMin = pRt.offsetMax = Vector2.zero;

        var card = new GameObject("Card", typeof(Image)).GetComponent<Image>();
        card.transform.SetParent(panel.transform, false);
        card.color = new Color(0.09f, 0.08f, 0.07f, 0.96f);
        var cRt = card.rectTransform;
        cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0.5f);
        cRt.pivot = new Vector2(0.5f, 0.5f);
        cRt.sizeDelta = new Vector2(CardW, CardH);

        var title = MakeText(card.transform, "FICHA DO DRAGÃO", 24, FontStyle.Bold, TextAnchor.MiddleCenter);
        Top(title.rectTransform, 0f, -36f, ContentW, 32f);

        headerText = MakeText(card.transform, "", 18, FontStyle.Bold, TextAnchor.MiddleCenter);
        Top(headerText.rectTransform, 0f, -80f, ContentW, 26f);

        Line(card.transform, LeftX, -112f, ContentW, true);

        // degrau de maturidade (o que desbloqueia ataques)
        tierText = MakeText(card.transform, "", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        TopLeft(tierText.rectTransform, LeftX, -124f, ColumnW, 18f);
        tierBar = MakeBar(card.transform, LeftX, -146f, 8f, new Color(0.95f, 0.85f, 0.4f));

        Line(card.transform, 0f, -112f, 1f, false, CardH - 112f - Padding); // divisor vertical entre colunas

        // ---- coluna esquerda: 6 atributos (nome+valor na linha, barra abaixo)
        const float rowH = 48f;
        for (int i = 0; i < Order.Length; i++)
        {
            float y = -172f - i * rowH;

            var name = MakeText(card.transform, AttrNames[i], 15, FontStyle.Bold, TextAnchor.MiddleLeft);
            TopLeft(name.rectTransform, LeftX, y, ColumnW - 70f, 22f);

            attrValues[i] = MakeText(card.transform, "0.0", 17, FontStyle.Bold, TextAnchor.MiddleRight);
            TopLeft(attrValues[i].rectTransform, LeftX + ColumnW - 64f, y, 64f, 22f);

            attrBars[i] = MakeBar(card.transform, LeftX, y - 26f, 12f, new Color(0.85f, 0.72f, 0.35f));
        }
        float attrsBottom = -172f - (Order.Length - 1) * rowH - 26f - 12f; // fim da última barra

        Line(card.transform, LeftX, attrsBottom - 22f, ColumnW, true);

        // ---- coluna esquerda-baixo: corpo e linhagem
        var bodyTitle = MakeText(card.transform, "CORPO E LINHAGEM", 14, FontStyle.Bold, TextAnchor.MiddleLeft);
        TopLeft(bodyTitle.rectTransform, LeftX, attrsBottom - 36f, ColumnW, 18f);
        bodyText = MakeText(card.transform, "", 13, FontStyle.Normal, TextAnchor.UpperLeft);
        bodyText.lineSpacing = 1.15f;
        TopLeft(bodyText.rectTransform, LeftX, attrsBottom - 58f, ColumnW, 170f);

        // ---- coluna direita: derivados
        var statsTitle = MakeText(card.transform, "STATS DERIVADOS", 14, FontStyle.Bold, TextAnchor.MiddleLeft);
        TopLeft(statsTitle.rectTransform, RightX, -172f, ColumnW, 18f);
        statsText = MakeText(card.transform, "", 13, FontStyle.Normal, TextAnchor.UpperLeft);
        statsText.lineSpacing = 1.15f;
        TopLeft(statsText.rectTransform, RightX, -196f, ColumnW, 190f);

        Line(card.transform, RightX, -426f, ColumnW, true);

        // ---- coluna direita-baixo: projeção por fase
        var projTitle = MakeText(card.transform, "PROJEÇÃO POR FASE (estimativa)", 14, FontStyle.Bold, TextAnchor.MiddleLeft);
        TopLeft(projTitle.rectTransform, RightX, -448f, ColumnW, 18f);
        projText = MakeText(card.transform, "", 13, FontStyle.Normal, TextAnchor.UpperLeft);
        projText.lineSpacing = 1.3f;
        TopLeft(projText.rectTransform, RightX, -472f, ColumnW, 160f);

        var footer = MakeText(card.transform,
            "Os atributos sobem sozinhos com a maturidade — não há pontos para gastar.   Tab: fechar",
            12, FontStyle.Italic, TextAnchor.MiddleCenter);
        footer.color = new Color(0.6f, 0.6f, 0.6f);
        Top(footer.rectTransform, 0f, -(CardH - 40f), ContentW, 20f);
    }

    /// <summary>Barra genérica: fundo escuro + preenchimento medido em Refresh. x/y são a borda
    /// superior-esquerda (ver TopLeft), para nunca vazar da largura da coluna.</summary>
    RectTransform MakeBar(Transform parent, float x, float y, float height = 14f,
                          Color? fillColor = null, float width = ColumnW)
    {
        var bg = new GameObject("Bar", typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(parent, false);
        bg.color = new Color(1f, 1f, 1f, 0.1f);
        TopLeft(bg.rectTransform, x, y, width, height);

        var fill = new GameObject("Fill", typeof(Image)).GetComponent<Image>();
        fill.transform.SetParent(bg.transform, false);
        fill.color = fillColor ?? new Color(0.85f, 0.72f, 0.35f);
        var rt = fill.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, height);
        return rt;
    }

    /// <summary>Linha divisória fina (horizontal se vertical=false com largura w, ou vertical com
    /// altura h). Só cosmética — separa os blocos que antes ficavam colados uns nos outros.</summary>
    static void Line(Transform parent, float x, float y, float wOrThickness, bool horizontal, float length = 0f)
    {
        var img = new GameObject("Divider", typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = new Color(1f, 1f, 1f, 0.1f);
        if (horizontal) TopLeft(img.rectTransform, x, y, wOrThickness, 1f);
        else TopLeft(img.rectTransform, x, y, wOrThickness, length);
    }

    // posiciona ancorado ao topo-centro do card (x = centro do elemento, relativo ao centro do card)
    static void Top(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    // posiciona ancorado ao topo do card, mas x = BORDA ESQUERDA do elemento (não o centro) —
    // assim w nunca faz o elemento vazar pra fora da coluna que ele deveria ocupar.
    static void TopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    Text MakeText(Transform parent, string content, int size, FontStyle style, TextAnchor align)
    {
        var t = new GameObject("Text", typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = Color.white;
        t.alignment = align;
        return t;
    }
}
