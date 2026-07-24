using UnityEngine;

/// <summary>
/// Game feel de DANO e MORTE: quanto o golpe sacode a câmera, quanto sangra a vinheta,
/// quando o dragão grunhe e quanto tempo leva o dissolve do corpo.
/// Era o corpo de [SerializeField]s do DragonDamageFeedback + DragonDissolve.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/DragonFeedback.asset</b>.
/// </summary>
[BalanceAsset("Balance/DragonFeedback")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Feedback de Dano", fileName = "DragonFeedback")]
public class DragonFeedbackProfile : BalanceProfile
{
    [Header("O que conta como GOLPE (o resto é DoT)")]
    public float discreteHitMin = 1f;    // abaixo disso é DoT: acumula
    public float dotPulseEvery = 2.5f;   // vida acumulada p/ pulso suave

    [Header("Shake")]
    public float shakePerHit = 0.35f;    // trauma no dano de referência
    public float refDamage = 15f;        // dano que calibra shake/vinheta

    [Header("Vinheta")]
    public Color vignetteColor = new(0.30f, 0.015f, 0.01f);
    public float pulseMax = 0.28f;       // intensidade no pico do pulso
    public float pulseFade = 0.55f;      // s até o pulso sumir
    public float lowHealthMax = 0.17f;   // residual no limiar da morte
    public float desatMax = 16f;         // dessaturação no pico (0-100)

    [Header("Som de dor")]
    public float hurtCooldown = 1.1f;    // não metralhar o grunhido
    public float hurtMinDamage = 2f;

    [Header("Dissolve na morte")]
    public float dissolveDuration = 2f;
    [Tooltip("Propriedades de shader tentadas em ordem — a 1ª que existir no material " +
             "é a que anima. Trocar de shader = acrescentar o nome aqui, sem tocar em código.")]
    public string[] dissolveProps = { "_DissolveAmount", "_Dissolve", "_DissolveValue" };
}
