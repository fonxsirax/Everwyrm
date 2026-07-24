using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Habilidades desbloqueáveis do dragão — teclas 1/2/3/4 disparam os ataques
/// EQUIPADOS nos 4 slots. O jogo pode ter quantos ataques quiser (assets
/// DragonAttackData em Resources/Entities/Attacks); os slots são só o loadout atual.
///
/// Fluxo de um ataque: tecla → checagens (voo/cooldown/energia/lock) →
/// CrossFade da animação + trava de ações → após castTime o efeito dispara
/// (projétil, área ou melee) → cooldown.
///
/// Desbloqueio: assina DragonAttributes.OnTierUp — o dragão destrava golpes ao
/// AMADURECER (crescer e envelhecer), não por gastar pontos. Ataques com
/// unlockLevel alcançado entram sozinhos no primeiro slot livre.
/// Observer: OnLoadoutChanged / OnAttackUnlocked / OnAttackFired para o HUD.
/// </summary>
[RequireComponent(typeof(DragonController))]
public class DragonAbilities : MonoBehaviour
{
    /// <summary>Teto RÍGIDO de slots (dimensiona os arrays) — 4 padrão + 1 de mutação
    /// (DragonRecord.bonusAttackSlots). Acrescentar mais slots de mutação no futuro
    /// exige subir este número também.</summary>
    public const int MaxSlotCount = 5;

    /// <summary>Onde o catálogo de ataques vive, relativo a uma pasta Resources.
    /// Um asset novo aqui = um golpe novo no jogo, sem tocar em código.</summary>
    public const string AttacksFolder = "Entities/Attacks";

    [Header("Balanceamento")]
    [Tooltip("Os slots e o loadout inicial vivem no asset " +
             "Assets/Scriptables/Resources/Balance/DragonCombat.asset. Vazio = esse " +
             "padrão; arraste outro DragonCombatProfile para variar por espécie.")]
    [SerializeField] DragonCombatProfile combatProfile;

    /// <summary>O perfil resolvido, sob demanda: o campo acima quando preenchido,
    /// senão o asset padrão. PREGUIÇOSO de propósito — a janela de balanceamento
    /// (Tools > Everwyrm > Balanço do Dragão) lê estes números direto no PREFAB,
    /// fora do Play, onde nenhum Awake rodou.</summary>
    DragonCombatProfile cfgCache;
    DragonCombatProfile cfg => cfgCache != null ? cfgCache : (cfgCache = Balance.Resolve(combatProfile));

    /// <summary>O loadout ATUAL — muda em jogo (desbloqueio, Equip, possessão), então
    /// é estado de runtime, não dado: nasce de `cfg.startingSlots` e vive aqui.</summary>
    readonly DragonAttackData[] slots = new DragonAttackData[MaxSlotCount];
    readonly List<DragonAttackData> known = new();     // todos os ataques do jogo
    readonly List<DragonAttackData> unlocked = new();
    readonly Dictionary<DragonAttackData, float> cooldownUntil = new();
    int bonusSlots;   // de DragonRecord.bonusAttackSlots (mutação — ver LoadFrom)

    /// <summary>Slots realmente ativos AGORA: base + bônus de mutação, sempre travado
    /// no teto rígido. HUD e input consultam isto, não MaxSlotCount.</summary>
    public int ActiveSlotCount => Mathf.Clamp(cfg.baseSlotCount + bonusSlots, 1, MaxSlotCount);

    DragonController dragon;
    DragonVitals vitals;          // opcional (padrão do projeto)
    DragonAttributes attrs;       // opcional
    DragonGrowth growth;          // opcional
    DragonTraits traitsCache;     // opcional (adicionado em runtime pelo Controller)
    DragonTraits Traits => traitsCache != null ? traitsCache : (traitsCache = GetComponent<DragonTraits>());
    Animator anim;

    // ---- Observer
    public event Action OnLoadoutChanged;
    public event Action<DragonAttackData> OnAttackUnlocked;
    public event Action<int, DragonAttackData> OnAttackFired;

    public IReadOnlyList<DragonAttackData> Unlocked => unlocked;
    public IReadOnlyList<DragonAttackData> Known => known;

    void Awake()
    {
        for (int i = 0; i < cfg.startingSlots.Length && i < MaxSlotCount; i++)
            slots[i] = cfg.startingSlots[i];
        dragon = GetComponent<DragonController>();
        vitals = GetComponent<DragonVitals>();
        attrs = GetComponent<DragonAttributes>();
        growth = GetComponent<DragonGrowth>();
        anim = GetComponent<Animator>();
    }

    void OnEnable()
    {
        if (attrs != null) attrs.OnTierUp += OnTierUp;
    }

    void OnDisable()
    {
        if (attrs != null) attrs.OnTierUp -= OnTierUp;
    }

    void Start()
    {
        EnsureKnown();

        // slots pré-preenchidos no Inspector contam como desbloqueados
        foreach (var s in slots)
            if (s != null && !unlocked.Contains(s)) unlocked.Add(s);

        CheckUnlocks(attrs != null ? attrs.Tier : 1, announce: false);

        if (Balance.Default<DragonHudProfile>().autoCreateAttackHud && FindFirstObjectByType<DragonAttackHUD>() == null)
            new GameObject("Attack HUD").AddComponent<DragonAttackHUD>().Bind(this, dragon, vitals);
    }

    /// <summary>Popula o catálogo de ataques (idempotente): todo asset em
    /// Resources/Entities/Attacks + os extras do profile de combate. Chamado no Start
    /// e na possessão (LoadFrom).</summary>
    void EnsureKnown()
    {
        if (known.Count > 0) return;
        known.AddRange(Resources.LoadAll<DragonAttackData>(AttacksFolder));
        foreach (var a in cfg.extraAttacks)
            if (a != null && !known.Contains(a)) known.Add(a);
        known.Sort((a, b) => a.unlockLevel.CompareTo(b.unlockLevel));
    }

    void Update()
    {
        if (DragonStatsMenu.IsOpen || dragon.IsDead) return;

        // bindings centralizados no DragonInput (preparo p/ gamepad futuro).
        // Só até ActiveSlotCount — o slot de mutação (5) só responde a quem o tem.
        for (int i = 0; i < ActiveSlotCount; i++)
            if (DragonInput.AbilityDown(i)) { TryUse(i); break; }
    }

    // ============================================================== EXECUÇÃO
    public void TryUse(int slot)
    {
        if (slot >= ActiveSlotCount) return;   // slot de mutação sem o bônus: nada
        var a = GetSlot(slot);
        if (a == null) return;
        if (dragon.ActionsLocked || dragon.IsResting || dragon.IsSwimming) return;
        if (Time.time < CooldownEnd(a)) return;
        if (dragon.IsFlying && !a.usableInFlight) return;

        // custo: peso (growth) × EFICIÊNCIA do Fôlego (attrs)
        float costMul = (growth != null ? growth.EnergyCostMul : 1f)
                      * (attrs != null ? attrs.EnergyCostMul : 1f);
        if (vitals != null && !vitals.TrySpend(a.energyCost * costMul)) return;

        // Fôlego de Forja: o sopro (isFire) ignora o cooldown
        float cdMul = a.isFire && Traits != null ? Traits.FireCooldownMul : 1f;
        cooldownUntil[a] = Time.time + a.cooldown * cdMul;

        // CrossFade direto para o estado (mesmo padrão do AnimalAgent): funciona
        // do chão E do voo — as transições de saída do Animator devolvem para
        // Locomotion ou Fly conforme o bool "Flying".
        anim.CrossFadeInFixedTime(StateName(a.animation), 0.1f, 0);
        dragon.LockActions(a.animationLock);

        StartCoroutine(FireAfterCast(a));
        OnAttackFired?.Invoke(slot, a);
    }

    IEnumerator FireAfterCast(DragonAttackData a)
    {
        if (a.castTime > 0f) yield return new WaitForSeconds(a.castTime);
        if (dragon.IsDead) yield break;

        float scale = dragon.BodyScale;                       // crescimento × atributos
        float dmgMul = attrs != null ? attrs.DamageMul : 1f;  // Poder
        float radiusMul = a.isFire && attrs != null ? attrs.FlameSizeMul : 1f;
        float damage = a.baseDamage * dmgMul * scale;

        if (a.usesProjectile)
        {
            Vector3 mouth = transform.position
                          + transform.forward * (2.4f * scale)
                          + Vector3.up * (1.6f * scale);
            AttackProjectile.Launch(a, mouth, transform.forward, damage,
                                    a.areaRadius * radiusMul * scale, scale, transform);
        }
        else if (a.areaDamage)
        {
            // ex.: Incinerate — combustão ao redor do dragão
            Vector3 center = transform.position;
            float radius = a.areaRadius * radiusMul * scale;

            CombatDamage.DamageArea(center, radius, damage, transform, a);
            if (a.vfxPrefab != null)
                Destroy(Instantiate(a.vfxPrefab, center, Quaternion.identity), 6f);
            else if (a.isFire)
                CombatVFX.Burst(center, radius, CombatVFX.FireColor);

            if (a.spawnsFireArea)
                FireArea.Spawn(center, radius, a.fireAreaDuration,
                               a.burnDamagePerSecond, a.burnDuration, a.vfxPrefab);
        }
        else
        {
            // melee single target — mesmo alcance/padrão do StrikeWildlife
            Vector3 p = transform.position + transform.forward * (a.range * 0.5f * scale);
            var target = AnimalAgent.FindNearest(p, a.range * scale);
            if (target != null)
            {
                target.TakeHit(damage, transform.position);
                if (a.appliesBurn && !target.IsDead)
                    BurningStatus.Apply(target, a.burnDamagePerSecond, a.burnDuration);
            }
        }
    }

    // ============================================================ DESBLOQUEIO
    void OnTierUp(int tier) => CheckUnlocks(tier, announce: true);

    void CheckUnlocks(int tier, bool announce)
    {
        bool changed = false;
        foreach (var a in known)
        {
            if (unlocked.Contains(a) || a.unlockLevel > tier) continue;
            unlocked.Add(a);
            int slot = AutoEquip(a);
            changed = true;
            if (announce) OnAttackUnlocked?.Invoke(a);
            Debug.Log(slot >= 0
                ? $"Ataque desbloqueado: {a.attackName} — equipado na tecla {slot + 1}"
                : $"Ataque desbloqueado: {a.attackName} (slots cheios — use Equip)");
        }
        if (changed) OnLoadoutChanged?.Invoke();
    }

    /// <summary>Coloca no primeiro slot ATIVO livre. -1 = todos ocupados (ou sem
    /// slot extra de mutação — o ataque fica desbloqueado, só não equipado).</summary>
    int AutoEquip(DragonAttackData a)
    {
        for (int i = 0; i < ActiveSlotCount; i++)
            if (slots[i] == null) { slots[i] = a; return i; }
        return -1;
    }

    /// <summary>Equipa um ataque JÁ desbloqueado num slot ATIVO (troca o que estiver lá).</summary>
    public bool Equip(DragonAttackData a, int slot)
    {
        if (slot < 0 || slot >= ActiveSlotCount) return false;
        if (a != null && !unlocked.Contains(a)) return false;
        slots[slot] = a;
        OnLoadoutChanged?.Invoke();
        return true;
    }

    /// <summary>Destrava um ataque por código (quests, itens, eventos...).</summary>
    public void Unlock(DragonAttackData a)
    {
        if (a == null || unlocked.Contains(a)) return;
        if (!known.Contains(a)) known.Add(a);
        unlocked.Add(a);
        AutoEquip(a);
        OnAttackUnlocked?.Invoke(a);
        OnLoadoutChanged?.Invoke();
    }

    // ============================================================= CONSULTAS
    public DragonAttackData GetSlot(int i) =>
        i >= 0 && i < MaxSlotCount ? slots[i] : null;

    float CooldownEnd(DragonAttackData a) =>
        cooldownUntil.TryGetValue(a, out float t) ? t : 0f;

    public float CooldownLeft(DragonAttackData a) =>
        a == null ? 0f : Mathf.Max(0f, CooldownEnd(a) - Time.time);

    /// <summary>0 = pronto · 1 = acabou de usar (fração do cooldown restante).</summary>
    public float CooldownFraction(int slot)
    {
        var a = GetSlot(slot);
        return a == null || a.cooldown <= 0f ? 0f
             : Mathf.Clamp01(CooldownLeft(a) / a.cooldown);
    }

    // ============================================================ POSSESSÃO
    /// <summary>Restaura o BÔNUS DE SLOT (mutação) e o loadout salvo (por nome de
    /// ataque) de um DragonRecord, depois reavalia desbloqueios e preenche qualquer
    /// slot ATIVO ainda vazio com algo já desbloqueado. Essa última parte cobre a
    /// ordem de Awake/Start entre componentes NÃO ser garantida: se o bônus de slot
    /// só chegar depois do Start (que já rodou CheckUnlocks com ActiveSlotCount
    /// menor), o 5º slot apareceria vazio até o próximo degrau sem este passe.</summary>
    public void LoadFrom(DragonRecord record)
    {
        bonusSlots = record.bonusAttackSlots;
        EnsureKnown();

        var names = record.state.equippedAttackNames;
        if (names != null && names.Length > 0)
        {
            for (int i = 0; i < MaxSlotCount; i++)
            {
                string n = i < names.Length ? names[i] : null;
                var a = string.IsNullOrEmpty(n) ? null : known.Find(k => k.attackName == n);
                slots[i] = a;
                if (a != null && !unlocked.Contains(a)) unlocked.Add(a);
            }
        }

        CheckUnlocks(attrs != null ? attrs.Tier : 1, announce: false);
        for (int i = 0; i < ActiveSlotCount; i++)
        {
            if (slots[i] != null) continue;
            var candidate = unlocked.Find(a => Array.IndexOf(slots, a) < 0);
            if (candidate == null) break;
            slots[i] = candidate;
        }

        OnLoadoutChanged?.Invoke();
    }

    public void WriteTo(DragonState s)
    {
        var names = new string[MaxSlotCount];
        for (int i = 0; i < MaxSlotCount; i++)
            names[i] = slots[i] != null ? slots[i].attackName : null;
        s.equippedAttackNames = names;
    }

    /// <summary>Nome do estado no Animator para cada animação do enum.</summary>
    public static string StateName(DragonAttackAnimation a) => a switch
    {
        DragonAttackAnimation.BiteFront => "UAttack Bite Front",
        DragonAttackAnimation.Bite2 => "UAttack Bite 2",
        DragonAttackAnimation.ClawsLeft => "UAttack Claws L",
        DragonAttackAnimation.ClawsRight => "UAttack Claws R",
        DragonAttackAnimation.TailLeft => "UAttack Tail L",
        DragonAttackAnimation.TailRight => "UAttack Tail R",
        DragonAttackAnimation.WingLeft => "UAttack Wing L",
        DragonAttackAnimation.WingRight => "UAttack Wings R",
        DragonAttackAnimation.FireBreath => "Fire Breath",
        DragonAttackAnimation.Roar => "Roar",
        _ => "UAttack Bite Front"
    };
}
