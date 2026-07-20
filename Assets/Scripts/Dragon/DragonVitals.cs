using System;
using UnityEngine;

/// <summary>
/// As três necessidades do GDD: Fome, Energia e Vida.
///  - Fome cai constantemente. Crítica: menos stamina e recuperação lenta. Zerada: perde vida.
///  - Energia é consumida por voo/corrida (drenada pelo DragonController) e recuperada
///    descansando (tecla R) ou parado.
///  - Vida recupera devagar; rápido descansando bem alimentado.
///
/// Observer: publica OnStatsChanged / OnDeath — o HUD (e qualquer sistema) assina
/// os eventos em vez de fazer polling.
/// </summary>
[RequireComponent(typeof(DragonController))]
public class DragonVitals : MonoBehaviour
{
    [Header("Fome")]
    [SerializeField] float maxHunger = 100f;
    [SerializeField] float hungerDecay = 0.09f;      // ~18 min até zerar
    [SerializeField] float criticalHunger = 25f;
    [SerializeField] float starvationDamage = 2f;    // vida/s com fome zerada

    [Header("Energia")]
    [SerializeField] float maxEnergy = 100f;
    [SerializeField] float regenIdle = 3f;           // parado no chão
    [SerializeField] float regenResting = 10f;       // descansando (R)
    [SerializeField] float criticalEnergyCap = 60f;  // teto de energia com fome crítica
    [SerializeField] float regenMultiplier = 5f;     // recuperação global de energia
    [SerializeField] float drainMultiplier = 0.1f;   // consumo global de energia

    [Header("Vida")]
    [SerializeField] float maxHealth = 100f;
    [SerializeField] float regenSlow = 0.35f;        // fome > 50
    [SerializeField] float regenRestingWellFed = 2.5f;

    [Header("HUD")]
    [SerializeField] bool autoCreateHud = true;

    DragonController dragon;
    DragonAttributes attrs;                          // opcional
    float lastH = -1f, lastE = -1f, lastF = -1f;     // últimos valores emitidos

    // máximos efetivos (Resistência aumenta vida e energia total)
    public float MaxHealthEff => maxHealth * (attrs != null ? attrs.MaxHealthMul : 1f);
    public float MaxEnergyEff => maxEnergy * (attrs != null ? attrs.MaxEnergyMul : 1f);
    public float HungerDecayEff => hungerDecay * (attrs != null ? attrs.HungerDecayMul : 1f);
    public float TotalEaten { get; private set; }    // carne comida na vida

    public float Hunger { get; private set; }
    public float Energy { get; private set; }
    public float Health { get; private set; }
    public float Hunger01 => Hunger / maxHunger;
    public float Energy01 => Energy / MaxEnergyEff;
    public float Health01 => Health / MaxHealthEff;

    public bool IsDead { get; private set; }
    public bool IsExhausted => Energy <= 0.5f;
    public bool IsStarving => Hunger <= 0.5f;
    public bool IsHungerCritical => Hunger < criticalHunger;

    // ---- Observer
    public event Action<DragonVitals> OnStatsChanged;
    public event Action OnDeath;
    /// <summary>Dano sofrido: (quantidade, origem no mundo se conhecida).
    /// Dispara TODO dano, inclusive DoT por frame — assinantes que reagem
    /// (shake, som) devem filtrar/acumular (ver DragonDamageFeedback).</summary>
    public event Action<float, Vector3?> OnDamaged;

    void Awake()
    {
        dragon = GetComponent<DragonController>();
        attrs = GetComponent<DragonAttributes>();
        Hunger = maxHunger * 0.85f;
        Energy = maxEnergy;
        Health = maxHealth;
    }

    void Start()
    {
        if (autoCreateHud && FindFirstObjectByType<DragonHUD>() == null)
            new GameObject("Dragon HUD").AddComponent<DragonHUD>().Bind(this, dragon);
        EmitStats(); // estado inicial para os assinantes
    }

    void Update()
    {
        if (IsDead) return;
        float dt = Time.deltaTime;

        // ---- Fome (Resistência segura a fome)
        Hunger = Mathf.Max(0f, Hunger - HungerDecayEff * dt);

        // ---- Energia (drenos vêm do DragonController via Drain/TrySpend)
        float cap = IsHungerCritical ? criticalEnergyCap * (MaxEnergyEff / maxEnergy)
                                     : MaxEnergyEff;
        bool idleGround = !dragon.IsFlying && dragon.Speed01 < 0.05f;
        float hungerFactor = IsHungerCritical ? 0.4f : 1f; // recuperação lenta com fome
        if (dragon.IsResting) Energy += regenResting * regenMultiplier * hungerFactor * dt;
        else if (idleGround) Energy += regenIdle * regenMultiplier * hungerFactor * dt;
        Energy = Mathf.Clamp(Energy, 0f, cap);

        // ---- Vida
        if (IsStarving) Damage(starvationDamage * dt);
        else if (dragon.IsResting && Hunger > 60f) Heal(regenRestingWellFed * dt);
        else if (Hunger > 50f) Heal(regenSlow * dt);
    }

    void LateUpdate()
    {
        // emite uma única vez por frame, e só se algo mudou de forma perceptível
        if (Mathf.Abs(Health - lastH) > 0.15f ||
            Mathf.Abs(Energy - lastE) > 0.15f ||
            Mathf.Abs(Hunger - lastF) > 0.15f)
            EmitStats();
    }

    void EmitStats()
    {
        lastH = Health; lastE = Energy; lastF = Hunger;
        OnStatsChanged?.Invoke(this);
    }

    /// <summary>Dreno contínuo de energia (custo/segundo). Retorna false se exausto.</summary>
    public bool Drain(float perSecond)
    {
        if (IsDead) return false;
        Energy = Mathf.Max(0f, Energy - perSecond * drainMultiplier * Time.deltaTime);
        return !IsExhausted;
    }

    /// <summary>Custo instantâneo (decolagem, batida de asas). Só gasta se tiver o suficiente.</summary>
    public bool TrySpend(float amount)
    {
        float cost = amount * drainMultiplier;
        if (IsDead || Energy < cost) return false;
        Energy -= cost;
        return true;
    }

    /// <summary>Comer: recupera fome e um pouco de vida.</summary>
    public void Eat(float food)
    {
        Hunger = Mathf.Min(maxHunger, Hunger + food);
        TotalEaten += food;
        Heal(food * 0.25f);
        EmitStats();
    }

    public void Damage(float amount) => Damage(amount, null);

    public void Damage(float amount, Vector3? source)
    {
        if (IsDead || amount <= 0f) return;
        Health = Mathf.Max(0f, Health - amount);
        OnDamaged?.Invoke(amount, source);
        if (Health <= 0f)
        {
            IsDead = true;
            EmitStats();
            OnDeath?.Invoke();
        }
        else dragon.OnDamaged(amount); // reação de dano (animação)
    }

    public void Heal(float amount) => Health = Mathf.Min(MaxHealthEff, Health + amount);
}
