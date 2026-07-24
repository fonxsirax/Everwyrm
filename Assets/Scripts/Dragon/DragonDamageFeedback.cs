using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Feedback de dano — OBSERVER de DragonVitals.OnDamaged. Combina sinais
/// discretos (proposta realista, nada arcade):
///  - micro-shake rotacional na câmera (DragonCamera.AddShake)
///  - pulso de vinheta vermelha + leve dessaturação (Volume HDRP em runtime)
///  - vocalização "Hurt" via DragonSounds (com cooldown)
///  - vinheta residual pulsando devagar com vida crítica
///
/// Dano CONTÍNUO (fome, queimadura — chega todo frame em doses mínimas) é
/// acumulado num balde e só pulsa a vinheta de leve; shake e som ficam
/// reservados para golpes discretos, senão a tela treme sem parar.
/// </summary>
public class DragonDamageFeedback : MonoBehaviour
{
    [Header("Balanceamento")]
    [Tooltip("O game feel de dano (classificação, shake, vinheta, grunhido) vive no " +
             "asset Assets/Scriptables/Resources/Balance/DragonFeedback.asset. Vazio = " +
             "esse padrão; arraste outro DragonFeedbackProfile para variar por espécie.")]
    [SerializeField] DragonFeedbackProfile feedbackProfile;

    /// <summary>O perfil resolvido, sob demanda: o campo acima quando preenchido,
    /// senão o asset padrão. PREGUIÇOSO de propósito — a janela de balanceamento
    /// (Tools > Everwyrm > Balanço do Dragão) lê estes números direto no PREFAB,
    /// fora do Play, onde nenhum Awake rodou.</summary>
    DragonFeedbackProfile cfgCache;
    DragonFeedbackProfile cfg => cfgCache != null ? cfgCache : (cfgCache = Balance.Resolve(feedbackProfile));

    DragonVitals vitals;
    DragonSounds sounds;
    DragonCamera cam;
    Volume volume;
    Vignette vignette;
    ColorAdjustments colorAdj;

    float pulse;                 // envelope 0..1 do pulso atual
    float dotBucket;             // dano contínuo acumulado
    float lastHurtTime = -99f;
    bool dead;

    void Awake()
    {
        vitals = GetComponent<DragonVitals>();
        sounds = GetComponent<DragonSounds>();
    }

    void Start()
    {
        if (vitals == null) { enabled = false; return; }
        vitals.OnDamaged += OnDamaged;
        vitals.OnDeath += OnDeath;
        BuildVolume();
    }

    void OnDestroy()
    {
        if (vitals != null)
        {
            vitals.OnDamaged -= OnDamaged;
            vitals.OnDeath -= OnDeath;
        }
        if (volume != null) Destroy(volume.gameObject);
    }

    void Update()
    {
        if (vignette == null) return;
        pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime / cfg.pulseFade);

        // residual de vida crítica: quase subliminar, "respirando" devagar
        float low01 = dead ? 1f : Mathf.InverseLerp(0.28f, 0.07f, vitals.Health01);
        float breath = 0.8f + 0.2f * Mathf.Sin(Time.time * 2.2f);
        float baseV = dead ? 0.34f : cfg.lowHealthMax * low01 * breath;

        vignette.intensity.value = Mathf.Min(0.45f, baseV + pulse * cfg.pulseMax);
        colorAdj.saturation.value = -cfg.desatMax * Mathf.Clamp01(pulse + low01 * 0.4f);
    }

    void OnDamaged(float amount, Vector3? source)
    {
        if (dead) return;

        if (amount < cfg.discreteHitMin)
        {
            // dano contínuo: acumula e pulsa só a vinheta, de leve
            dotBucket += amount;
            if (dotBucket >= cfg.dotPulseEvery)
            {
                dotBucket = 0f;
                pulse = Mathf.Max(pulse, 0.45f);
            }
            return;
        }

        float k = Mathf.Clamp01(amount / cfg.refDamage);
        pulse = Mathf.Max(pulse, Mathf.Lerp(0.5f, 1f, k));

        if (cam == null && Camera.main != null)
            cam = Camera.main.GetComponent<DragonCamera>();
        cam?.AddShake(cfg.shakePerHit * Mathf.Lerp(0.6f, 1.6f, k));

        if (amount >= cfg.hurtMinDamage && Time.time - lastHurtTime >= cfg.hurtCooldown)
        {
            lastHurtTime = Time.time;
            sounds?.PlaySound("Hurt");   // silencioso até o clip ser atribuído
        }
    }

    void OnDeath() => dead = true;

    /// <summary>Volume global HDRP criado em runtime — nenhum asset de cena
    /// para configurar. Prioridade alta para vencer o volume de ambiente.</summary>
    void BuildVolume()
    {
        var go = new GameObject("Damage FX Volume");
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 60f;

        var profile = volume.profile;   // instância própria de runtime
        vignette = profile.Add<Vignette>();
        vignette.color.Override(cfg.vignetteColor);
        vignette.intensity.Override(0f);
        vignette.smoothness.Override(1f);
        vignette.rounded.Override(false);

        colorAdj = profile.Add<ColorAdjustments>();
        colorAdj.saturation.Override(0f);
    }
}
