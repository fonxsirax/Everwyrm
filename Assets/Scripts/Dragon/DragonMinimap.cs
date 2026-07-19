using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimapa circular no canto inferior direito (oposto ao HUD de barras).
///  - O dragão é uma SETA VERMELHA sempre no CENTRO, girando com a direção dele.
///  - Comidas aparecem como X dentro do alcance (o raio do faro/Dominância).
///  - Sprites fáceis de trocar: arraste arte real nos campos públicos
///    `dragonSprite` / `foodSprite`; enquanto forem nulos, uso sprites gerados.
/// Mapa "norte pra cima": os marcadores se movem, a seta gira.
/// </summary>
public class DragonMinimap : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] float size = 220f;
    [SerializeField] float margin = 30f;
    [SerializeField] float rangeOverride = 0f;     // 0 = usa o faro (Dominância do Poder)
    [SerializeField] float updateInterval = 0.1f;

    // Ícones vêm do asset Assets/Scriptables/Resources/MinimapIcons.asset
    // (MinimapIconSet) — configure sprites/cores/tamanhos lá, sem tocar na cena.
    Sprite dragonSprite, foodSprite, animalSprite;
    Color dragonColor = Color.white;
    Color foodColor = new(1f, 0.7f, 0.25f);
    Color animalColor = new(0.55f, 0.85f, 1f, 0.9f);
    Color predatorColor = new(1f, 0.4f, 0.35f, 0.95f);
    float dragonSize = 24f, foodSize = 14f, animalSize = 8f;

    Transform dragon;
    DragonAttributes attrs;
    RectTransform center, foodLayer, animalLayer, arrow;
    readonly List<Image> foodMarkers = new();
    readonly List<Image> animalMarkers = new();
    Sprite generatedArrow, generatedX, generatedDot;
    float nextUpdate;

    public void Bind(DragonController d, DragonAttributes a)
    {
        dragon = d.transform;
        attrs = a;

        var icons = Resources.Load<MinimapIconSet>("MinimapIcons");
        if (icons != null)
        {
            dragonSprite = icons.dragonSprite;
            foodSprite = icons.foodSprite;
            animalSprite = icons.animalSprite;
            dragonColor = icons.dragonColor;
            foodColor = icons.foodColor;
            animalColor = icons.animalColor;
            predatorColor = icons.predatorColor;
            dragonSize = icons.dragonSize;
            foodSize = icons.foodSize;
            animalSize = icons.animalSize;
        }
        Build();
    }

    void Update()
    {
        if (dragon == null || Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + updateInterval;

        float range = rangeOverride > 0f ? rangeOverride
                    : attrs != null ? attrs.DominanceRadius : 120f;
        float uiRadius = size * 0.5f - 14f;

        // seta central gira com o dragão (mapa fixo, norte pra cima)
        arrow.localEulerAngles = new Vector3(0f, 0f, -dragon.eulerAngles.y);

        // comidas dentro do alcance viram X
        int used = 0;
        Vector3 origin = dragon.position;
        foreach (var c in Carcass.All)
        {
            if (c == null || c.IsEmpty) continue;
            float dx = c.transform.position.x - origin.x;
            float dz = c.transform.position.z - origin.z;
            if (dx * dx + dz * dz > range * range) continue;

            var m = GetMarker(used++);
            m.rectTransform.anchoredPosition = new Vector2(dx, dz) / range * uiRadius;
        }
        for (int i = used; i < foodMarkers.Count; i++)
            foodMarkers[i].gameObject.SetActive(false);

        // fauna viva dentro do faro — presas azuladas, predadores avermelhados
        int usedAnimals = 0;
        foreach (var a in AnimalAgent.All)
        {
            if (a == null || a.IsDead) continue;
            float dx = a.transform.position.x - origin.x;
            float dz = a.transform.position.z - origin.z;
            if (dx * dx + dz * dz > range * range) continue;

            var m = GetAnimalMarker(usedAnimals++);
            m.color = a.Def != null && a.Def.predator ? predatorColor : animalColor;
            m.rectTransform.anchoredPosition = new Vector2(dx, dz) / range * uiRadius;
        }
        for (int i = usedAnimals; i < animalMarkers.Count; i++)
            animalMarkers[i].gameObject.SetActive(false);
    }

    // --------------------------------------------------------------- BUILD
    void Build()
    {
        var root = (RectTransform)transform;
        root.anchorMin = root.anchorMax = new Vector2(1f, 0f);   // canto inferior DIREITO
        root.pivot = new Vector2(1f, 0f);
        root.anchoredPosition = new Vector2(-margin, margin);
        root.sizeDelta = new Vector2(size, size);

        var bg = new GameObject("BG", typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(root, false);
        bg.sprite = CircleSprite();
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // container central: (0,0) = posição do dragão
        center = new GameObject("Center", typeof(RectTransform)).GetComponent<RectTransform>();
        center.SetParent(root, false);
        center.anchorMin = center.anchorMax = new Vector2(0.5f, 0.5f);
        center.sizeDelta = Vector2.zero;

        // camadas em ordem: fauna embaixo, comida no meio, seta SEMPRE por cima
        animalLayer = MakeLayerRect("Animals");
        foodLayer = MakeLayerRect("Foods");

        var arrowImg = new GameObject("Dragon", typeof(Image)).GetComponent<Image>();
        arrowImg.transform.SetParent(center, false);
        arrowImg.sprite = dragonSprite != null ? dragonSprite : (generatedArrow ??= ArrowSprite());
        arrowImg.color = dragonColor;
        arrowImg.raycastTarget = false;
        arrow = arrowImg.rectTransform;
        arrow.sizeDelta = new Vector2(dragonSize, dragonSize);
        arrow.anchoredPosition = Vector2.zero;                   // SEMPRE no centro
    }

    RectTransform MakeLayerRect(string name)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(center, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    Image GetMarker(int index)
    {
        while (foodMarkers.Count <= index)
        {
            var img = new GameObject("Food", typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(foodLayer, false);
            img.sprite = foodSprite != null ? foodSprite : (generatedX ??= XSprite());
            img.color = foodColor;
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(foodSize, foodSize);
            foodMarkers.Add(img);
        }
        var m = foodMarkers[index];
        m.gameObject.SetActive(true);
        return m;
    }

    Image GetAnimalMarker(int index)
    {
        while (animalMarkers.Count <= index)
        {
            var img = new GameObject("Animal", typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(animalLayer, false);
            img.sprite = animalSprite != null ? animalSprite : (generatedDot ??= DotSprite());
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(animalSize, animalSize);
            animalMarkers.Add(img);
        }
        var m = animalMarkers[index];
        m.gameObject.SetActive(true);
        return m;
    }

    // ------------------------------------------------- SPRITES PROCEDURAIS
    static Sprite CircleSprite()
    {
        const int s = 128; float r = s * 0.5f - 1f;
        var tex = NewTex(s);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(s / 2f, s / 2f));
                tex.SetPixel(x, y,
                    d < r - 3f ? new Color(0f, 0f, 0f, 0.55f) :
                    d < r ? new Color(1f, 1f, 1f, 0.35f) : Color.clear);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
    }

    static Sprite ArrowSprite()
    {
        const int s = 32;
        var tex = NewTex(s);
        var red = new Color(0.9f, 0.12f, 0.1f);
        for (int y = 3; y < s - 2; y++)
        {
            // base larga embaixo, PONTA PRA CIMA (frente do dragão)
            float half = (s - 3f - y) / (s - 6f) * (s * 0.38f);
            for (int x = 0; x < s; x++)
                if (Mathf.Abs(x - s / 2f) <= half)
                    tex.SetPixel(x, y, red);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
    }

    static Sprite DotSprite()
    {
        const int s = 16; float r = s * 0.5f - 1f;
        var tex = NewTex(s);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                if (Vector2.Distance(new Vector2(x, y), new Vector2(s / 2f, s / 2f)) < r)
                    tex.SetPixel(x, y, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
    }

    static Sprite XSprite()
    {
        const int s = 24;
        var tex = NewTex(s);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                if (Mathf.Abs(x - y) <= 2 || Mathf.Abs(x + y - (s - 1)) <= 2)
                    tex.SetPixel(x, y, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
    }

    static Texture2D NewTex(int s)
    {
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var clear = new Color[s * s];
        tex.SetPixels(clear);
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }
}
