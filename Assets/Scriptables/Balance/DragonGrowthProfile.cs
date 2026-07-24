using UnityEngine;

/// <summary>
/// Crescimento, condição corporal, peso, sustentação por peso e expectativa de vida.
/// Era o corpo de [SerializeField]s do DragonGrowth.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/DragonGrowth.asset</b>.
/// </summary>
[BalanceAsset("Balance/DragonGrowth")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Crescimento do Dragão", fileName = "DragonGrowth")]
public class DragonGrowthProfile : BalanceProfile
{
    [Header("Tamanho")]
    public float hatchlingScale = 0.45f;
    public float colossalScale = 1.7f;
    public float fullGrowthMinutes = 25f;  // filhote→colossal sempre alimentado
    [Range(0f, 1f)] public float startGrowth = 0f;
    public float adultAt = 0.4f;
    public float colossalAt = 0.85f;
    public float mealGrowthBonus = 0.0012f; // por ponto de nutrição

    [Header("Condição Corporal — taxas em KG (sutil)")]
    [Range(0f, 1f)] public float startCondition = 0.5f;
    public float gainKgPerMinute = 1f;          // satisfeito (fome > 65%), sem comer
    public float lossKgPerMinute = 1f;          // fome abaixo de 50%
    public float starvingLossKgPerMinute = 3f;  // fome abaixo de 20% (~1 kg/20 s)
    public float mealWeightNudgeKg = 2f;        // kg ganhos na hora por refeição

    [Header("Peso")]
    public float adultHealthyWeightKg = 950f;

    [Header("Voo — sustentação por PESO (batida de asa)")]
    [Tooltip("Subida por batida com o corpo ESQUELÉTICO (condição 0). O peso é o " +
             "que manda no quanto o Space sobe — não a fase da vida.\n" +
             "NERF jul/2026: faixa Lean↔Fat estreitada (era 1.3↔0.5, ~2.6×) — o " +
             "impulso de voo não deve variar muito de dragão pra dragão.")]
    public float liftWhenLean = 1.1f;
    [Tooltip("Subida por batida com o corpo GORDO (condição 1): carregar banha custa altitude.")]
    public float liftWhenFat = 0.85f;
    [Tooltip("Quanto o TAMANHO ainda influi (0 = filhote e colossal sobem os mesmos metros). " +
             "NERF jul/2026: reduzido (era 0.25) — mesma lógica de variar pouco.")]
    [Range(0f, 1f)] public float liftSizeInfluence = 0.15f;

    [Header("Velhice e morte natural (linhagem)")]
    [Tooltip("Idade (min) em que o dragão entra na Velhice — declínio gradual.")]
    public float oldAgeMinutes = 40f;
    [Tooltip("Idade (min) da morte natural. O dragão NÃO some: fica com 0 de vida e a " +
             "Base possui o próximo (sem recarregar a cena).")]
    public float lifespanMinutes = 55f;
    [Tooltip("Perda de agilidade/velocidade no auge da Velhice (0.25 = -25%).")]
    [Range(0f, 0.9f)] public float elderPenalty = 0.25f;

    [Header("Vigor (IV) — sobrevivência")]
    [Tooltip("Cada ponto de Vigor acelera o crescimento.")]
    public float vigorGrowthPerPoint = 0.02f;
    [Tooltip("Cada ponto de Vigor alonga a expectativa de vida.")]
    public float vigorLifespanPerPoint = 0.03f;

    [Header("Definhamento (starving)")]
    [Tooltip("Abaixo desta fome (0..1) o corpo começa a definhar visualmente.")]
    [Range(0f, 1f)] public float starveBelowHunger = 0.25f;
}
