using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Barra de habilidades (teclas 1-4) — gerada em código, sem prefab, no mesmo
/// padrão do DragonHUD. Mostra ícone (ou iniciais), tecla, cooldown descendo e
/// avisos de desbloqueio. O overlay de cooldown atualiza por frame; o resto é
/// observer (OnLoadoutChanged / OnAttackUnlocked).
/// </summary>
public class DragonAttackHUD : MonoBehaviour
{
    const float SlotSize = 64f, Gap = 8f;

    DragonAbilities abilities;
    DragonController dragon;
    DragonVitals vitals;

    readonly Image[] bg = new Image[DragonAbilities.MaxSlotCount];
    readonly Image[] iconImg = new Image[DragonAbilities.MaxSlotCount];
    readonly Text[] abbrev = new Text[DragonAbilities.MaxSlotCount];
    readonly Text[] keyLabel = new Text[DragonAbilities.MaxSlotCount];
    readonly RectTransform[] cdOverlay = new RectTransform[DragonAbilities.MaxSlotCount];
    readonly Text[] nameLabel = new Text[DragonAbilities.MaxSlotCount];

    Text notifyText;
    Coroutine notifyFade;

    static readonly Color SlotEmpty = new(0f, 0f, 0f, 0.35f);
    static readonly Color SlotReady = new(0.12f, 0.1f, 0.08f, 0.72f);
    static readonly Color SlotBlocked = new(0.4f, 0.08f, 0.06f, 0.72f);

    public void Bind(DragonAbilities a, DragonController d, DragonVitals v)
    {
        abilities = a;
        dragon = d;
        vitals = v;
        Build();

        abilities.OnLoadoutChanged += Refresh;
        abilities.OnAttackUnlocked += OnUnlocked;
        Refresh();
    }

    void OnDestroy()
    {
        if (abilities == null) return;
        abilities.OnLoadoutChanged -= Refresh;
        abilities.OnAttackUnlocked -= OnUnlocked;
    }

    void OnUnlocked(DragonAttackData a)
    {
        Notify($"Novo ataque desbloqueado: {a.attackName}");
        Refresh();
    }

    // ------------------------------------------------------------- REFRESH
    /// <summary>Estático (ícones/nomes/visibilidade) — só quando o loadout muda.
    /// O 5º slot (mutação) só aparece se o dragão tiver o bônus — a maioria nunca
    /// o vê. Recalculado aqui (não só no Build) porque LoadFrom pode carregar o
    /// bônus DEPOIS da HUD já ter sido construída (ordem de Start entre componentes
    /// não é garantida).</summary>
    void Refresh()
    {
        int active = abilities.ActiveSlotCount;
        for (int i = 0; i < DragonAbilities.MaxSlotCount; i++)
        {
            bool visible = i < active;
            bg[i].gameObject.SetActive(visible);
            nameLabel[i].gameObject.SetActive(visible);
            if (!visible) continue;

            var a = abilities.GetSlot(i);
            bool has = a != null;
            iconImg[i].enabled = has && a.icon != null;
            if (has && a.icon != null) iconImg[i].sprite = a.icon;
            abbrev[i].text = !has ? "–"
                : a.icon != null ? ""
                : Abbreviate(a.attackName);
            nameLabel[i].text = has ? a.attackName : "";
        }
    }

    /// <summary>Dinâmico (cooldown/estado) — leve, por frame.</summary>
    void Update()
    {
        if (abilities == null) return;
        int active = abilities.ActiveSlotCount;
        for (int i = 0; i < active; i++)
        {
            var a = abilities.GetSlot(i);
            if (a == null)
            {
                bg[i].color = SlotEmpty;
                cdOverlay[i].sizeDelta = new Vector2(SlotSize, 0f);
                continue;
            }

            bool blockedInFlight = dragon != null && dragon.IsFlying && !a.usableInFlight;
            bg[i].color = blockedInFlight ? SlotBlocked : SlotReady;

            float frac = abilities.CooldownFraction(i);
            cdOverlay[i].sizeDelta = new Vector2(SlotSize, SlotSize * frac);
        }
    }

    static string Abbreviate(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        var parts = name.Split(' ');
        return parts.Length >= 2
            ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[^1][0])}"
            : name.Substring(0, Mathf.Min(2, name.Length)).ToUpper();
    }

    // -------------------------------------------------------------- AVISOS
    public void Notify(string message)
    {
        notifyText.text = message;
        if (notifyFade != null) StopCoroutine(notifyFade);
        notifyFade = StartCoroutine(FadeNotify());
    }

    IEnumerator FadeNotify()
    {
        var c = new Color(1f, 0.85f, 0.4f, 1f);
        notifyText.color = c;
        yield return new WaitForSeconds(2.2f);
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            c.a = 1f - t / 0.8f;
            notifyText.color = c;
            yield return null;
        }
        notifyText.text = "";
        notifyFade = null;
    }

    // --------------------------------------------------------------- BUILD
    void Build()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // largura sempre para o TETO (5 slots): dragões sem o bônus de mutação
        // ficam com a fileira de 4 levemente à esquerda do centro — troca aceitável
        // por não ter que realinhar a barra toda quando o 5º slot aparece/some
        float total = DragonAbilities.MaxSlotCount * SlotSize +
                      (DragonAbilities.MaxSlotCount - 1) * Gap;

        for (int i = 0; i < DragonAbilities.MaxSlotCount; i++)
        {
            float x = -total * 0.5f + i * (SlotSize + Gap);

            bg[i] = MakeImage(canvasGo.transform, SlotEmpty);
            PlaceBottom(bg[i].rectTransform, x, 22f, SlotSize, SlotSize);

            iconImg[i] = MakeImage(bg[i].transform, Color.white);
            Fill(iconImg[i].rectTransform, 4f);
            iconImg[i].preserveAspect = true;
            iconImg[i].enabled = false;

            abbrev[i] = MakeText(bg[i].transform, "–", 22, FontStyle.Bold,
                                 TextAnchor.MiddleCenter);
            Fill(abbrev[i].rectTransform, 0f);

            // overlay de cooldown: cresce de baixo pra cima e "esvazia" ao zerar
            var overlay = MakeImage(bg[i].transform, new Color(0f, 0f, 0f, 0.75f));
            var ort = overlay.rectTransform;
            ort.anchorMin = new Vector2(0f, 0f);
            ort.anchorMax = new Vector2(1f, 0f);
            ort.pivot = new Vector2(0.5f, 0f);
            ort.anchoredPosition = Vector2.zero;
            ort.sizeDelta = new Vector2(SlotSize, 0f);
            cdOverlay[i] = ort;
            overlay.raycastTarget = false;

            keyLabel[i] = MakeText(bg[i].transform, (i + 1).ToString(), 14,
                                   FontStyle.Bold, TextAnchor.UpperLeft);
            Fill(keyLabel[i].rectTransform, 4f);
            keyLabel[i].color = new Color(1f, 0.95f, 0.6f);

            nameLabel[i] = MakeText(canvasGo.transform, "", 12, FontStyle.Normal,
                                    TextAnchor.MiddleCenter);
            PlaceBottom(nameLabel[i].rectTransform, x, 6f, SlotSize, 14f);
        }

        notifyText = MakeText(canvasGo.transform, "", 20, FontStyle.Bold,
                              TextAnchor.MiddleCenter);
        PlaceBottom(notifyText.rectTransform, -260f, 100f, 520f, 28f);
    }

    static void PlaceBottom(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void Fill(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    static Image MakeImage(Transform parent, Color color)
    {
        var img = new GameObject("Image", typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static Text MakeText(Transform parent, string content, int size,
                         FontStyle style, TextAnchor anchor)
    {
        var t = new GameObject("Text", typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = Color.white;
        t.alignment = anchor;
        t.raycastTarget = false;
        return t;
    }
}
