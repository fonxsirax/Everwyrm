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
    [Header("Balanceamento")]
    [Tooltip("O balanceamento de fome/energia/vida vive no asset " +
             "Assets/Scriptables/Resources/Balance/DragonVitals.asset. Vazio = esse " +
             "padrão; arraste outro DragonVitalsProfile para variar por espécie.")]
    [SerializeField] DragonVitalsProfile vitalsProfile;

    /// <summary>O perfil resolvido, sob demanda: o campo acima quando preenchido,
    /// senão o asset padrão. PREGUIÇOSO de propósito — a janela de balanceamento
    /// (Tools > Everwyrm > Balanço do Dragão) lê estes números direto no PREFAB,
    /// fora do Play, onde nenhum Awake rodou.</summary>
    DragonVitalsProfile vitCache;
    DragonVitalsProfile vit => vitCache != null ? vitCache : (vitCache = Balance.Resolve(vitalsProfile));

    DragonController dragon;
    DragonAttributes attrs;                          // opcional
    DragonTraits traitsCache;                        // opcional (adicionado em runtime pelo Controller)
    /// <summary>Getter preguiçoso: o DragonTraits é criado no Awake do Controller, e
    /// a ordem de Awake entre componentes é indefinida — buscar sob demanda evita
    /// cachear null cedo demais.</summary>
    DragonTraits Traits => traitsCache != null ? traitsCache : (traitsCache = GetComponent<DragonTraits>());
    float lastH = -1f, lastE = -1f, lastF = -1f;     // últimos valores emitidos
    float invulnUntil = -99f;                        // i-frames (dash/boost)

    // bases cruas — a janela de balanceamento (Tools > Everwyrm > Balanço do
    // Dragão) simula os máximos por fase da vida a partir daqui
    public float BaseMaxHealth => vit.maxHealth;
    public float BaseMaxEnergy => vit.maxEnergy;
    public float BaseHungerDecay => vit.hungerDecay;

    // máximos efetivos: Vigor aumenta a vida, Fôlego a energia; os traços cobram
    // sua parte (Ossos Ocos/Fôlego de Forja tiram vida; Insone acelera a fome)
    public float MaxHealthEff => vit.maxHealth * (attrs != null ? attrs.MaxHealthMul : 1f)
                                          * (Traits != null ? Traits.HealthMul : 1f);
    public float MaxEnergyEff => vit.maxEnergy * (attrs != null ? attrs.MaxEnergyMul : 1f);
    public float HungerDecayEff => vit.hungerDecay * (attrs != null ? attrs.HungerDecayMul : 1f)
                                              * (Traits != null ? Traits.HungerDecayMul : 1f);
    public float TotalEaten { get; private set; }    // carne comida na vida

    public float Hunger { get; private set; }
    public float Energy { get; private set; }
    public float Health { get; private set; }
    public float Hunger01 => Hunger / vit.maxHunger;
    public float Energy01 => Energy / MaxEnergyEff;
    public float Health01 => Health / MaxHealthEff;

    public bool IsDead { get; private set; }
    public bool IsExhausted => Energy <= 0.5f;
    /// <summary>i-frames do dash/boost: dano é ignorado até aqui.</summary>
    public bool IsInvulnerable => Time.time < invulnUntil;
    public bool IsStarving => Hunger <= 0.5f;
    public bool IsHungerCritical => Hunger < vit.criticalHunger;

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
        Hunger = vit.maxHunger * 0.85f;
        Energy = vit.maxEnergy;
        Health = vit.maxHealth;
    }

    void Start()
    {
        if (Balance.Default<DragonHudProfile>().autoCreateVitalsHud && FindFirstObjectByType<DragonHUD>() == null)
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
        float cap = IsHungerCritical ? vit.criticalEnergyCap * (MaxEnergyEff / vit.maxEnergy)
                                     : MaxEnergyEff;
        bool idleGround = !dragon.IsFlying && dragon.Speed01 < 0.05f;
        float hungerFactor = IsHungerCritical ? 0.4f : 1f; // recuperação lenta com fome
        // Insone repõe energia parado bem mais rápido — dispensa parar para o R
        float idleMul = Traits != null ? Traits.IdleRegenMul : 1f;
        if (dragon.IsResting) Energy += vit.regenResting * vit.regenMultiplier * hungerFactor * dt;
        else if (idleGround) Energy += vit.regenIdle * vit.regenMultiplier * hungerFactor * idleMul * dt;
        Energy = Mathf.Clamp(Energy, 0f, cap);

        // ---- Vida
        if (IsStarving) Damage(vit.starvationDamage * dt);
        else if (dragon.IsResting && Hunger > 60f) Heal(vit.regenRestingWellFed * dt);
        else if (Hunger > 50f) Heal(vit.regenSlow * dt);
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
        Energy = Mathf.Max(0f, Energy - perSecond * vit.drainMultiplier * Time.deltaTime);
        return !IsExhausted;
    }

    /// <summary>Custo instantâneo (decolagem, batida de asas). Só gasta se tiver o suficiente.</summary>
    public bool TrySpend(float amount)
    {
        float cost = amount * vit.drainMultiplier;
        if (IsDead || Energy < cost) return false;
        Energy -= cost;
        return true;
    }

    /// <summary>Comer: recupera fome e um pouco de vida.</summary>
    public void Eat(float food)
    {
        Hunger = Mathf.Min(vit.maxHunger, Hunger + food);
        TotalEaten += food;
        Heal(food * 0.25f);
        EmitStats();
    }

    /// <summary>Invulnerabilidade curta (i-frames do dash/boost) — janelas
    /// sobrepostas mantêm a MAIOR, nunca encurtam.</summary>
    public void GrantIFrames(float seconds) =>
        invulnUntil = Mathf.Max(invulnUntil, Time.time + seconds);

    public void Damage(float amount) => Damage(amount, null);

    public void Damage(float amount, Vector3? source)
    {
        if (IsDead || amount <= 0f || IsInvulnerable) return;
        // Couraça amortece o dano recebido (o esqueleto blindado da linhagem)
        if (Traits != null) amount *= Traits.DamageTakenMul;
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

    // ============================================================ POSSESSÃO
    /// <summary>Restaura fome/energia/vida (frações do state × máximos efetivos).
    /// Os atributos já devem ter sido carregados antes (os máximos dependem deles).</summary>
    public void LoadFrom(DragonRecord record)
    {
        var s = record.state;
        Hunger = Mathf.Clamp01(s.hunger01) * vit.maxHunger;
        Energy = Mathf.Clamp01(s.energy01) * MaxEnergyEff;
        Health = Mathf.Clamp01(s.health01) * MaxHealthEff;
        TotalEaten = s.totalEaten;
        IsDead = s.isDead;
        EmitStats();
    }

    public void WriteTo(DragonState s)
    {
        s.hunger01 = Hunger01;
        s.energy01 = Energy01;
        s.health01 = Health01;
        s.totalEaten = TotalEaten;
        s.isDead = IsDead;
    }

    /// <summary>Volta à vida ao ser possuído (a vida vem do state via LoadFrom).</summary>
    public void Revive()
    {
        IsDead = false;
        if (Health <= 0.5f) Health = MaxHealthEff;
        EmitStats();
    }

    /// <summary>Morte IMPOSTA (velhice): zera a vida e dispara OnDeath como qualquer
    /// morte — o dragão não some, apenas fica com 0 de vida (a Base assume o próximo).</summary>
    public void Kill()
    {
        if (IsDead) return;
        Health = 0f;
        IsDead = true;
        EmitStats();
        OnDeath?.Invoke();
    }
}
