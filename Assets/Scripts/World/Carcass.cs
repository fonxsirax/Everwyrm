using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Uma carcaça/presa abatida no mundo. O dragão come com G (algumas mordidas).
/// Visual placeholder gerado em código — será substituído por presas de verdade
/// quando o sistema de caça chegar.
/// </summary>
public class Carcass : MonoBehaviour
{
    public static readonly List<Carcass> All = new();

    [SerializeField] float nutritionTotal = 40f;
    [SerializeField] float nutritionPerBite = 12f;
    [SerializeField] bool showVisual = true;   // false: o corpo do animal É o visual

    float remaining;
    Transform visual;
    float initialScale;

    public bool IsEmpty => remaining <= 0f;

    public static Carcass Spawn(Vector3 position, float nutrition, bool buildVisual = true)
    {
        var go = new GameObject("Carcass");
        go.transform.position = position;
        var c = go.AddComponent<Carcass>();
        c.nutritionTotal = nutrition;
        c.showVisual = buildVisual;
        return c;
    }

    // Start (não Awake): Spawn() ajusta nutritionTotal DEPOIS do AddComponent —
    // em Awake a carcaça congelava no valor default do campo.
    void Start()
    {
        remaining = nutritionTotal;
        if (showVisual) BuildVisual();
        else { visual = transform; initialScale = 1f; }
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void BuildVisual()
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(body.GetComponent<Collider>()); // comer é por distância
        body.transform.SetParent(transform, false);
        body.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        body.transform.localScale = new Vector3(1.7f, 0.6f, 1.1f);
        Tint(body, new Color(0.45f, 0.13f, 0.1f));

        var bone = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.Destroy(bone.GetComponent<Collider>());
        bone.transform.SetParent(transform, false);
        bone.transform.localPosition = new Vector3(0.4f, 0.45f, 0.2f);
        bone.transform.localRotation = Quaternion.Euler(0f, 0f, 70f);
        bone.transform.localScale = new Vector3(0.12f, 0.45f, 0.12f);
        Tint(bone, new Color(0.85f, 0.8f, 0.7f));

        visual = transform;
        initialScale = 1f;
    }

    static void Tint(GameObject go, Color color)
    {
        var mr = go.GetComponent<MeshRenderer>();
        var shader = Shader.Find("HDRP/Lit");
        if (shader != null)
        {
            var m = new Material(shader);
            m.SetColor("_BaseColor", color);
            mr.material = m;
        }
        else mr.material.color = color; // fallback (Built-in/URP)
    }

    /// <summary>Uma mordida. Retorna a nutrição obtida; some quando acaba.</summary>
    public float Consume()
    {
        float bite = Mathf.Min(nutritionPerBite, remaining);
        remaining -= bite;

        float t = Mathf.Max(0.25f, remaining / nutritionTotal);
        visual.localScale = Vector3.one * initialScale * t;

        if (IsEmpty) Destroy(gameObject, 0.5f);
        return bite;
    }

    /// <summary>Carcaça mais próxima dentro do alcance, ou null.</summary>
    public static Carcass FindNearest(Vector3 position, float range)
    {
        Carcass best = null;
        float bestSqr = range * range;
        foreach (var c in All)
        {
            if (c.IsEmpty) continue;
            float sqr = (c.transform.position - position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = c; }
        }
        return best;
    }
}
