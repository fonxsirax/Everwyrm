using UnityEngine;

/// <summary>
/// Fome, Energia e Vida — as três necessidades do GDD como DADO.
/// Era o corpo de [SerializeField]s do DragonVitals.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/DragonVitals.asset</b>.
/// </summary>
[BalanceAsset("Balance/DragonVitals")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Vitais do Dragão", fileName = "DragonVitals")]
public class DragonVitalsProfile : BalanceProfile
{
    [Header("Fome")]
    public float maxHunger = 100f;
    public float hungerDecay = 0.09f;      // ~18 min até zerar
    public float criticalHunger = 25f;
    public float starvationDamage = 2f;    // vida/s com fome zerada

    [Header("Energia")]
    public float maxEnergy = 100f;
    public float regenIdle = 3f;           // parado no chão
    public float regenResting = 10f;       // descansando (R)
    public float criticalEnergyCap = 60f;  // teto de energia com fome crítica
    public float regenMultiplier = 5f;     // recuperação global de energia
    public float drainMultiplier = 0.1f;   // consumo global de energia

    [Header("Vida")]
    public float maxHealth = 100f;
    public float regenSlow = 0.35f;        // fome > 50
    public float regenRestingWellFed = 2.5f;
}
