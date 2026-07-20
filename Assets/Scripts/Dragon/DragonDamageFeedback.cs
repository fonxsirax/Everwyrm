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
    [Header("Classificação do dano")]
    [SerializeField] float discreteHitMin = 1f;   // abaixo disso é DoT: acumula
    [SerializeField] float dotPulseEvery = 2.5f;  // vida acumulada p/ pulso suave

    [Header("Câmera")]
    [SerializeField] float shakePerHit = 0.35f;   // trauma no dano de referência
    [SerializeField] float refDamage = 15f;       // dano que calibra shake/vinheta

    [Header("Vinheta (HDRP)")]
    [SerializeField] Color vignetteColor = new(0.30f, 0.015f, 0.01f);
    [SerializeField] float pulseMax = 0.28f;      // intensidade no pico do pulso
    [SerializeField] float pulseFade = 0.55f;     // s até o pulso sumir
    [SerializeField] float lowHealthMax = 0.17f;  // residual no limiar da morte
    [SerializeField] float desatMax = 16f;        // dessaturação no pico (0-100)

    [Header("Som")]
    [SerializeField] float hurtCooldown = 1.1f;   // não metralhar o grunhido
    [SerializeField] float hurtMinDamage = 2f;

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
        pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime / pulseFade);

        // residual de vida crítica: quase subliminar, "respirando" devagar
        float low01 = dead ? 1f : Mathf.InverseLerp(0.28f, 0.07f, vitals.Health01);
        float breath = 0.8f + 0.2f * Mathf.Sin(Time.time * 2.2f);
        float baseV = dead ? 0.34f : lowHealthMax * low01 * breath;

        vignette.intensity.value = Mathf.Min(0.45f, baseV + pulse * pulseMax);
        colorAdj.saturation.value = -desatMax * Mathf.Clamp01(pulse + low01 * 0.4f);
    }

    void OnDamaged(float amount, Vector3? source)
    {
        if (dead) return;

        if (amount < discreteHitMin)
        {
            // dano contínuo: acumula e pulsa só a vinheta, de leve
            dotBucket += amount;
            if (dotBucket >= dotPulseEvery)
            {
                dotBucket = 0f;
                pulse = Mathf.Max(pulse, 0.45f);
            }
            return;
        }

        float k = Mathf.Clamp01(amount / refDamage);
        pulse = Mathf.Max(pulse, Mathf.Lerp(0.5f, 1f, k));

        if (cam == null && Camera.main != null)
            cam = Camera.main.GetComponent<DragonCamera>();
        cam?.AddShake(shakePerHit * Mathf.Lerp(0.6f, 1.6f, k));

        if (amount >= hurtMinDamage && Time.time - lastHurtTime >= hurtCooldown)
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
        vignette.color.Override(vignetteColor);
        vignette.intensity.Override(0f);
        vignette.smoothness.Override(1f);
        vignette.rounded.Override(false);

        colorAdj = profile.Add<ColorAdjustments>();
        colorAdj.saturation.Override(0f);
    }
}
