using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Tipos de impacto do mundo. Novos efeitos = novo valor aqui + entrada
/// na tabela do ImpactEffects (prefab) ou um caso no ImpactVFX (procedural).</summary>
public enum ImpactKind
{
    Landing,          // pouso normal — poeira/detritos sob as patas
    HardLanding,      // queda de altura — poeira forte + estilhaços
    ObstacleStrike,   // colisão em voo — folhas/galhos (árvore) ou lascas (rocha)
    WaterEntry,       // corpo entrando na água — splash
    WaterSkim,        // barriga raspando a lâmina d'água — spray contínuo
    DashBurst,        // dash terrestre — rajada de poeira na largada
    WingBoost,        // batida forte de asas em voo — sopro de ar deslocado
}

/// <summary>
/// Um impacto acontecido no mundo. Carrega tudo que um VFX pode querer saber —
/// inclusive o `surface` atingido, gancho para no futuro escolher o efeito pelo
/// material/tag do que foi atingido (folhas numa árvore, lascas numa rocha).
/// </summary>
public struct ImpactEvent
{
    public ImpactKind kind;
    public Vector3 position;
    public Vector3 normal;
    public Vector3 velocity;
    public float strength01;   // 0..1 — intensidade relativa (volume/tamanho do efeito)
    public float scale;        // tamanho do ator (dragão filhote ≠ adulto)
    public Collider surface;   // o que foi atingido (pode ser null)
}

/// <summary>
/// ROTEADOR de efeitos de impacto — o ponto único por onde passam poeira de
/// pouso, folhas de colisão, splash e spray d'água.
///
/// Quem causa o impacto não conhece VFX nenhum: só publica um ImpactEvent
///   ImpactEffects.Emit(new ImpactEvent { kind = ImpactKind.Landing, ... });   // one-shot
///   ImpactEffects.Skim(new ImpactEvent { kind = ImpactKind.WaterSkim, ... }); // contínuo
///
/// PARA ADICIONAR UM VFX NOVO (o caminho preferido — sem tocar em código):
///   arraste o prefab de partícula na tabela `effects` do Inspector, no
///   GameObject "Impact Effects". Sem prefab, cai no placeholder procedural do
///   ImpactVFX; sem placeholder, o impacto simplesmente não desenha nada.
///
/// Som pode entrar pelo mesmo caminho (campo `sound` da entrada) quando o áudio
/// do projeto chegar — ver DragonSounds.
/// </summary>
public class ImpactEffects : MonoBehaviour
{
    /// <summary>Uma linha da tabela: o que tocar quando um ImpactKind acontece.</summary>
    [Serializable]
    public class Entry
    {
        public ImpactKind kind;
        [Tooltip("Prefab de partícula. Vazio = placeholder procedural (ImpactVFX)")]
        public GameObject prefab;
        [Tooltip("Som do impacto (opcional) — volume acompanha a intensidade")]
        public AudioClip sound;
        [Tooltip("Intervalo mínimo entre disparos deste efeito (s) — anti-metralhadora")]
        public float minInterval = 0.12f;
        [Tooltip("Intensidade mínima (0..1) para o efeito aparecer")]
        [Range(0f, 1f)] public float minStrength = 0f;
        [Tooltip("Tamanho e volume acompanham a intensidade do impacto")]
        public bool scaleWithStrength = true;
        [Tooltip("Segundos até o efeito instanciado se auto-destruir")]
        public float lifetime = 3f;

        [NonSerialized] public float lastTime = -99f;
    }

    [Tooltip("Um efeito por tipo de impacto. Tipos ausentes usam o padrão procedural.")]
    [SerializeField] List<Entry> effects = new();

    /// <summary>Efeito contínuo vivo (spray d'água): reaproveitado entre frames.</summary>
    class Continuous
    {
        public GameObject go;
        public ParticleSystem[] systems;
        public float[] baseRates;
        public int lastFrame;
        public bool emitting;
    }

    readonly Dictionary<ImpactKind, Continuous> live = new();
    readonly Dictionary<ImpactKind, Entry> byKind = new();

    static ImpactEffects instance;
    static bool quitting;

    /// <summary>Instância global — criada sob demanda, sem setup manual na cena.</summary>
    public static ImpactEffects Instance
    {
        get
        {
            if (quitting) return null;
            if (instance == null)
            {
                instance = FindFirstObjectByType<ImpactEffects>();
                if (instance == null)
                    instance = new GameObject("Impact Effects").AddComponent<ImpactEffects>();
            }
            return instance;
        }
    }

    /// <summary>Impacto pontual (pouso, colisão, splash).</summary>
    public static void Emit(in ImpactEvent e)
    {
        var i = Instance;
        if (i != null) i.PlayOneShot(e);
    }

    /// <summary>Impacto CONTÍNUO (spray raspando a água): chame a cada frame
    /// enquanto durar — o efeito se apaga sozinho quando as chamadas param.</summary>
    public static void Skim(in ImpactEvent e)
    {
        var i = Instance;
        if (i != null) i.PlayContinuous(e);
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        foreach (var e in effects) byKind[e.kind] = e;
    }

    void OnApplicationQuit() => quitting = true;

    Entry EntryFor(ImpactKind kind)
    {
        if (byKind.TryGetValue(kind, out var e)) return e;
        e = new Entry { kind = kind };          // padrão: procedural, sem espera
        byKind[kind] = e;
        effects.Add(e);
        return e;
    }

    // ------------------------------------------------------------- ONE-SHOT
    void PlayOneShot(in ImpactEvent e)
    {
        var cfg = EntryFor(e.kind);
        if (e.strength01 < cfg.minStrength) return;
        if (Time.time - cfg.lastTime < cfg.minInterval) return;
        cfg.lastTime = Time.time;

        float k = cfg.scaleWithStrength ? Mathf.Clamp01(e.strength01) : 1f;

        if (cfg.prefab != null)
        {
            // convenção: o prefab é autorado apontando +Y (para fora da superfície),
            // então a normal define a rotação — nunca degenera, seja chão ou parede
            var go = Instantiate(cfg.prefab, e.position,
                                 Quaternion.FromToRotation(Vector3.up, e.normal));
            go.transform.localScale *= e.scale * Mathf.Lerp(0.6f, 1.2f, k);
            Destroy(go, cfg.lifetime);
        }
        else ImpactVFX.Spawn(e, k);   // placeholder procedural (pode não existir p/ o tipo)

        if (cfg.sound != null) PlaySound(cfg.sound, e.position, k);
    }

    // ------------------------------------------------------------ CONTÍNUO
    void PlayContinuous(in ImpactEvent e)
    {
        var cfg = EntryFor(e.kind);
        if (!live.TryGetValue(e.kind, out var c) || c.go == null)
        {
            var go = cfg.prefab != null
                ? Instantiate(cfg.prefab, transform)
                : ImpactVFX.CreateContinuous(e.kind, transform, e.scale);
            if (go == null) return;                       // tipo sem visual (ainda)

            c = new Continuous { go = go, systems = go.GetComponentsInChildren<ParticleSystem>(true) };
            c.baseRates = new float[c.systems.Length];
            for (int i = 0; i < c.systems.Length; i++)
                c.baseRates[i] = c.systems[i].emission.rateOverTimeMultiplier;
            live[e.kind] = c;
        }

        c.go.transform.SetPositionAndRotation(e.position,
            Quaternion.LookRotation(SafeForward(e.velocity), Vector3.up));
        c.lastFrame = Time.frameCount;

        float k = cfg.scaleWithStrength ? Mathf.Clamp01(e.strength01) : 1f;
        for (int i = 0; i < c.systems.Length; i++)
        {
            var em = c.systems[i].emission;
            em.rateOverTimeMultiplier = c.baseRates[i] * Mathf.Lerp(0.25f, 1f, k);
            if (!c.emitting) c.systems[i].Play();
        }
        c.emitting = true;
    }

    void LateUpdate()
    {
        // efeito contínuo cujo emissor parou de chamar neste frame: apaga
        // (mantém o objeto vivo para reaproveitar — sem churn de GC)
        foreach (var kv in live)
        {
            var c = kv.Value;
            if (!c.emitting || c.go == null || c.lastFrame >= Time.frameCount - 1) continue;
            foreach (var ps in c.systems)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            c.emitting = false;
        }
    }

    /// <summary>Som espacializado no ponto do impacto (não move este objeto —
    /// os efeitos contínuos são filhos dele).</summary>
    static void PlaySound(AudioClip clip, Vector3 pos, float volume01) =>
        AudioSource.PlayClipAtPoint(clip, pos, Mathf.Lerp(0.4f, 1f, volume01));

    static Vector3 SafeForward(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.forward;
    }
}
