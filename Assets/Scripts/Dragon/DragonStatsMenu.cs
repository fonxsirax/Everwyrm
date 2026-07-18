using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Ficha do dragão — menu de atributos (Tab).
///  - Abre sozinho ao subir de nível (se não estiver voando).
///  - Pausa o jogo (timeScale 0) e libera o cursor.
///  - Gasta pontos clicando em [+] ou com as teclas 1/2/3.
///  - Mostra os stats DERIVADOS dos atributos (velocidades, vida, energia, faro...)
///    e dados do corpo: peso, condição, tamanho, idade e carne comida.
/// Observer: assina OnChanged/OnLevelUp — nada de polling.
/// </summary>
public class DragonStatsMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    DragonAttributes attrs;
    DragonGrowth growth;
    DragonVitals vitals;
    DragonController dragon;

    GameObject panel;
    Text headerText, bodyText, statsText;
    readonly Text[] attrPoints = new Text[3];
    readonly Button[] plusButtons = new Button[3];

    static readonly string[] AttrNames = { "VELOCIDADE", "PODER", "RESISTÊNCIA" };

    public void Bind(DragonVitals v, DragonController d)
    {
        vitals = v;
        dragon = d;
        growth = v.GetComponent<DragonGrowth>();
        attrs = v.GetComponent<DragonAttributes>();
        if (attrs == null) { enabled = false; return; }

        Build();
        SetOpen(false);

        attrs.OnChanged += OnAttrsChanged;
        attrs.OnLevelUp += OnLevelUp;
    }

    void OnDestroy()
    {
        if (attrs != null)
        {
            attrs.OnChanged -= OnAttrsChanged;
            attrs.OnLevelUp -= OnLevelUp;
        }
        if (IsOpen) SetOpen(false); // nunca deixar o jogo pausado
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) SetOpen(!IsOpen);
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
    }

    void OnAttrsChanged(DragonAttributes a) { if (IsOpen) Refresh(); }

    void OnLevelUp(int level)
    {
        if (!dragon.IsFlying && !dragon.IsDead) SetOpen(true);
    }

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
        headerText.text = $"NÍVEL {a.Level}" +
            (a.Unspent > 0 ? $"   ·   {a.Unspent} ponto{(a.Unspent > 1 ? "s" : "")} para gastar" : "");
        headerText.color = a.Unspent > 0 ? new Color(0.55f, 1f, 0.55f) : Color.white;

        int[] pts = { a.Velocidade, a.Poder, a.Resistencia };
        for (int i = 0; i < 3; i++)
        {
            attrPoints[i].text = pts[i].ToString();
            plusButtons[i].gameObject.SetActive(a.Unspent > 0);
        }

        bodyText.text =
            $"Fase: {growth.Stage}\n" +
            $"Peso: {growth.WeightKg:0} kg\n" +
            $"Condição: {ConditionLabel(growth.Condition01)}\n" +
            $"Tamanho: {growth.Scale:0.00}x\n" +
            $"Idade: {FormatAge(growth.AgeSeconds)}\n" +
            $"Carne comida: {vitals.TotalEaten:0}";

        statsText.text =
            $"Corrida máx: {dragon.MaxGroundSpeed:0.0} m/s\n" +
            $"Voo máx: {dragon.MaxFlightSpeed:0.0} m/s\n" +
            $"Tempo até correr: {dragon.TimeToRun:0.0} s\n" +
            $"Vida máx: {vitals.MaxHealthEff:0}\n" +
            $"Energia máx: {vitals.MaxEnergyEff:0}\n" +
            $"Fome: -{vitals.HungerDecayEff * 60f:0.0}/min\n" +
            $"Faro (dominância): {a.DominanceRadius:0} m\n" +
            $"Dano: x{a.DamageMul:0.00}   Chama: x{a.FlameSizeMul:0.00}";
    }

    string DescriptionFor(int i) => i switch
    {
        0 => $"+{attrs.SpeedPerPoint:P0} vel. máxima · +{attrs.AccelSpeedPerPoint:P0} aceleração",
        1 => $"+{attrs.DamagePerPoint:P0} dano · +{attrs.FlamePerPoint:P0} chama · +{attrs.DominancePerPoint:0} m de faro",
        _ => $"+{attrs.HealthPerPoint:P0} vida · +{attrs.EnergyPerPoint:P0} energia · -{attrs.HungerResistPerPoint:P0} fome",
    };

    static string ConditionLabel(float c) =>
        c < 0.3f ? "Magro" : c < 0.68f ? "Saudável" : "Gordo";

    static string FormatAge(float s) =>
        s < 60f ? $"{s:0} s" : $"{Mathf.FloorToInt(s / 60f)} min {Mathf.FloorToInt(s % 60f)} s";

    // --------------------------------------------------------------- BUILD
    void Build()
    {
        // EventSystem para os botões funcionarem
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

        // fundo escurecido + painel central
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
        cRt.sizeDelta = new Vector2(780f, 520f);

        var title = MakeText(card.transform, "FICHA DO DRAGÃO", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
        Top(title.rectTransform, 0f, -16f, 740f, 34f);

        headerText = MakeText(card.transform, "", 20, FontStyle.Bold, TextAnchor.MiddleCenter);
        Top(headerText.rectTransform, 0f, -52f, 740f, 28f);

        // ---- coluna esquerda: atributos
        for (int i = 0; i < 3; i++)
        {
            float y = -100f - i * 96f;
            int idx = i;

            // LABEL  [+]  pontos  [tecla]  — tudo junto, à esquerda
            var name = MakeText(card.transform, AttrNames[i], 20, FontStyle.Bold, TextAnchor.MiddleLeft);
            Top(name.rectTransform, -245f, y, 230f, 26f);

            var desc = MakeText(card.transform, DescriptionFor(i), 13, FontStyle.Normal, TextAnchor.UpperLeft);
            desc.color = new Color(0.75f, 0.73f, 0.68f);
            Top(desc.rectTransform, -245f, y - 26f, 230f, 40f);

            var btnGo = new GameObject("Plus", typeof(Image), typeof(Button));
            btnGo.transform.SetParent(card.transform, false);
            btnGo.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.25f);
            Top(btnGo.GetComponent<RectTransform>(), -100f, y - 10f, 44f, 44f);
            var plus = MakeText(btnGo.transform, "+", 30, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(plus.rectTransform);
            plusButtons[i] = btnGo.GetComponent<Button>();
            plusButtons[i].onClick.AddListener(() =>
                attrs.SpendPoint((DragonAttributes.Attribute)idx));

            attrPoints[i] = MakeText(card.transform, "0", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            Top(attrPoints[i].rectTransform, -45f, y - 10f, 50f, 40f);

            var key = MakeText(card.transform, $"[{i + 1}]", 14, FontStyle.Normal, TextAnchor.MiddleCenter);
            key.color = new Color(0.6f, 0.6f, 0.6f);
            Top(key.rectTransform, 5f, y - 10f, 40f, 40f);
        }

        // ---- coluna direita (bem separada): corpo e stats derivados
        var bodyTitle = MakeText(card.transform, "CORPO", 16, FontStyle.Bold, TextAnchor.MiddleLeft);
        Top(bodyTitle.rectTransform, 255f, -100f, 250f, 22f);
        bodyText = MakeText(card.transform, "", 15, FontStyle.Normal, TextAnchor.UpperLeft);
        Top(bodyText.rectTransform, 255f, -126f, 250f, 130f);

        var statsTitle = MakeText(card.transform, "STATS DERIVADOS", 16, FontStyle.Bold, TextAnchor.MiddleLeft);
        Top(statsTitle.rectTransform, 255f, -266f, 250f, 22f);
        statsText = MakeText(card.transform, "", 15, FontStyle.Normal, TextAnchor.UpperLeft);
        Top(statsText.rectTransform, 255f, -292f, 250f, 190f);

        var footer = MakeText(card.transform, "Tab — fechar", 14, FontStyle.Italic, TextAnchor.MiddleCenter);
        footer.color = new Color(0.6f, 0.6f, 0.6f);
        Top(footer.rectTransform, 0f, -488f, 740f, 22f);
    }

    // posiciona ancorado ao topo-centro do card (x relativo ao centro)
    static void Top(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
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
