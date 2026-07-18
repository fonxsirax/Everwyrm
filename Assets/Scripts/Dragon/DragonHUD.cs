using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD gerado em código (sem prefab), 100% OBSERVER: nenhum polling — assina os
/// eventos de DragonVitals (OnStatsChanged/OnDeath), DragonGrowth
/// (OnGrowthChanged/OnStageChanged) e DragonController (OnHintChanged),
/// e só redesenha quando algo muda.
/// Barras: Vida, Energia, Fome, Crescimento + linha de status e dica contextual.
/// </summary>
public class DragonHUD : MonoBehaviour
{
    DragonVitals vitals;
    DragonController dragon;
    DragonGrowth growth;
    DragonAttributes attrs;

    RectTransform healthFill, energyFill, hungerFill, growthFill;
    Transform canvasRoot;
    Image hungerImg;
    Text statusText, infoText, hintText, attrText;
    const float BarWidth = 260f;
    static readonly Color HungerNormal = new(0.9f, 0.45f, 0.15f);
    static readonly Color HungerLow = new(0.95f, 0.12f, 0.1f);   // fome < 20%

    public void Bind(DragonVitals v, DragonController d)
    {
        vitals = v;
        dragon = d;
        growth = v.GetComponent<DragonGrowth>();
        attrs = v.GetComponent<DragonAttributes>();
        Build();

        // ---- observer: assina tudo, sem Update()
        vitals.OnStatsChanged += OnStats;
        vitals.OnDeath += OnDeath;
        dragon.OnHintChanged += OnHint;
        if (growth != null)
        {
            growth.OnGrowthChanged += OnGrowth;
            growth.OnStageChanged += OnStage;
            OnGrowth(growth);
        }
        if (attrs != null)
        {
            attrs.OnChanged += OnAttrs;
            attrs.OnLevelUp += OnLevelUp;
            OnAttrs(attrs);

            // ficha do dragão (Tab)
            var menu = new GameObject("Stats Menu").AddComponent<DragonStatsMenu>();
            menu.transform.SetParent(transform, false);
            menu.Bind(vitals, dragon);
        }

        // minimapa no canto inferior direito (oposto às barras)
        var minimap = new GameObject("Minimap", typeof(RectTransform)).AddComponent<DragonMinimap>();
        minimap.transform.SetParent(canvasRoot, false);
        minimap.Bind(dragon, attrs);

        OnStats(vitals);
    }

    void OnDestroy()
    {
        if (vitals != null)
        {
            vitals.OnStatsChanged -= OnStats;
            vitals.OnDeath -= OnDeath;
        }
        if (dragon != null) dragon.OnHintChanged -= OnHint;
        if (growth != null)
        {
            growth.OnGrowthChanged -= OnGrowth;
            growth.OnStageChanged -= OnStage;
        }
        if (attrs != null)
        {
            attrs.OnChanged -= OnAttrs;
            attrs.OnLevelUp -= OnLevelUp;
        }
    }

    // ------------------------------------------------------------ HANDLERS
    void OnStats(DragonVitals v)
    {
        SetFill(healthFill, v.Health01);
        SetFill(energyFill, v.Energy01);
        SetFill(hungerFill, v.Hunger01);
        hungerImg.color = v.Hunger01 < 0.2f ? HungerLow : HungerNormal; // emagrecendo rápido

        statusText.text =
            v.IsDead ? "MORTO — Enter para renascer" :
            v.IsStarving ? "MORRENDO DE FOME!" :
            v.IsExhausted && dragon.IsFlying ? "EXAUSTO — ESTOL!" :
            v.IsExhausted ? "Exausto — descanse (R)" :
            v.IsHungerCritical ? "Faminto — cace algo" :
            dragon.IsResting ? "Descansando..." : "";
        statusText.color = v.IsDead || v.IsStarving
            ? new Color(1f, 0.3f, 0.25f) : Color.white;
    }

    void OnGrowth(DragonGrowth g)
    {
        SetFill(growthFill, g.Growth01);
        infoText.text = $"{g.Stage} · {ConditionLabel(g.Condition01)} · {g.WeightKg:0} kg";
    }

    void OnStage(DragonGrowth.LifeStage stage)
    {
        statusText.text = $"Você cresceu: agora é {stage}!";
        statusText.color = new Color(0.5f, 1f, 0.5f);
    }

    void OnDeath() => OnStats(vitals);

    void OnHint(string hint) => hintText.text = hint;

    void OnAttrs(DragonAttributes a)
    {
        attrText.text = a.Unspent > 0
            ? $"Nv {a.Level} · PONTO DISPONÍVEL ({a.Unspent}) — Tab abre a ficha"
            : $"Nv {a.Level} · Vel {a.Velocidade} · Poder {a.Poder} · Res {a.Resistencia} · Tab: ficha";
        attrText.color = a.Unspent > 0 ? new Color(0.5f, 1f, 0.5f) : Color.white;
    }

    void OnLevelUp(int level)
    {
        statusText.text = $"Nível {level}! Escolha um atributo (1/2/3)";
        statusText.color = new Color(0.5f, 1f, 0.5f);
    }

    static string ConditionLabel(float c) =>
        c < 0.3f ? "Magro" : c < 0.68f ? "Saudável" : "Gordo";

    // --------------------------------------------------------------- BUILD
    void Build()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        canvasRoot = canvasGo.transform;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        healthFill = Bar(canvasGo.transform, 0, "Vida", new Color(0.85f, 0.22f, 0.2f));
        energyFill = Bar(canvasGo.transform, 1, "Energia", new Color(0.95f, 0.78f, 0.2f));
        hungerFill = Bar(canvasGo.transform, 2, "Fome", HungerNormal);
        hungerImg = hungerFill.GetComponent<Image>();
        growthFill = Bar(canvasGo.transform, 3, "Crescimento", new Color(0.35f, 0.75f, 0.35f));

        infoText = MakeText(canvasGo.transform, "", 16, FontStyle.Normal);
        Place(infoText.rectTransform, 30f, 136f, 500f, 22f);

        statusText = MakeText(canvasGo.transform, "", 22, FontStyle.Bold);
        Place(statusText.rectTransform, 30f, 162f, 600f, 30f);

        hintText = MakeText(canvasGo.transform, "", 18, FontStyle.Bold);
        Place(hintText.rectTransform, 30f, 196f, 400f, 24f);
        hintText.color = new Color(1f, 0.95f, 0.6f);

        attrText = MakeText(canvasGo.transform, "", 16, FontStyle.Normal);
        Place(attrText.rectTransform, 30f, 224f, 650f, 22f);
    }

    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    RectTransform Bar(Transform parent, int index, string label, Color color)
    {
        float y = 26f + index * 26f;

        var bg = new GameObject(label, typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(parent, false);
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        Place(bg.rectTransform, 30f, y, BarWidth, 18f);

        var fill = new GameObject("Fill", typeof(Image)).GetComponent<Image>();
        fill.transform.SetParent(bg.transform, false);
        fill.color = color;
        var fillRt = fill.rectTransform;
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.anchoredPosition = new Vector2(2f, 0f);
        fillRt.sizeDelta = new Vector2(BarWidth - 4f, -4f);

        var txt = MakeText(bg.transform, label, 13, FontStyle.Normal);
        var txtRt = txt.rectTransform;
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(6f, 0f);
        txtRt.offsetMax = Vector2.zero;

        return fillRt;
    }

    Text MakeText(Transform parent, string content, int size, FontStyle style)
    {
        var t = new GameObject("Text", typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleLeft;
        return t;
    }

    void SetFill(RectTransform rt, float value01)
    {
        rt.sizeDelta = new Vector2((BarWidth - 4f) * Mathf.Clamp01(value01), -4f);
    }
}
