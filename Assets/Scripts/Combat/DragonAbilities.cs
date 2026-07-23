using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Habilidades desbloqueáveis do dragão — teclas 1/2/3/4 disparam os ataques
/// EQUIPADOS nos 4 slots. O jogo pode ter quantos ataques quiser (assets
/// DragonAttackData em Resources/Attacks); os slots são só o loadout atual.
///
/// Fluxo de um ataque: tecla → checagens (voo/cooldown/energia/lock) →
/// CrossFade da animação + trava de ações → após castTime o efeito dispara
/// (projétil, área ou melee) → cooldown.
///
/// Desbloqueio: assina DragonAttributes.OnLevelUp; ataques com unlockLevel
/// alcançado destravam e entram sozinhos no primeiro slot livre.
/// Observer: OnLoadoutChanged / OnAttackUnlocked / OnAttackFired para o HUD.
/// </summary>
[RequireComponent(typeof(DragonController))]
public class DragonAbilities : MonoBehaviour
{
    public const int SlotCount = 4;

    [Header("Loadout inicial (vazio — tudo vem por desbloqueio)")]
    [SerializeField] DragonAttackData[] slots = new DragonAttackData[SlotCount];

    [Tooltip("Ataques extras além dos carregados de Resources/Attacks.")]
    [SerializeField] List<DragonAttackData> extraAttacks = new();

    [SerializeField] bool autoCreateHud = true;

    readonly List<DragonAttackData> known = new();     // todos os ataques do jogo
    readonly List<DragonAttackData> unlocked = new();
    readonly Dictionary<DragonAttackData, float> cooldownUntil = new();

    DragonController dragon;
    DragonVitals vitals;          // opcional (padrão do projeto)
    DragonAttributes attrs;       // opcional
    DragonGrowth growth;          // opcional
    Animator anim;

    // ---- Observer
    public event Action OnLoadoutChanged;
    public event Action<DragonAttackData> OnAttackUnlocked;
    public event Action<int, DragonAttackData> OnAttackFired;

    public IReadOnlyList<DragonAttackData> Unlocked => unlocked;
    public IReadOnlyList<DragonAttackData> Known => known;

    void Awake()
    {
        dragon = GetComponent<DragonController>();
        vitals = GetComponent<DragonVitals>();
        attrs = GetComponent<DragonAttributes>();
        growth = GetComponent<DragonGrowth>();
        anim = GetComponent<Animator>();
    }

    void OnEnable()
    {
        if (attrs != null) attrs.OnLevelUp += OnLevelUp;
    }

    void OnDisable()
    {
        if (attrs != null) attrs.OnLevelUp -= OnLevelUp;
    }

    void Start()
    {
        // data-driven: todo asset em Resources/Attacks entra no jogo sozinho
        known.AddRange(Resources.LoadAll<DragonAttackData>("Attacks"));
        foreach (var a in extraAttacks)
            if (a != null && !known.Contains(a)) known.Add(a);
        known.Sort((a, b) => a.unlockLevel.CompareTo(b.unlockLevel));

        // slots pré-preenchidos no Inspector contam como desbloqueados
        foreach (var s in slots)
            if (s != null && !unlocked.Contains(s)) unlocked.Add(s);

        CheckUnlocks(attrs != null ? attrs.Level : 1, announce: false);

        if (autoCreateHud && FindFirstObjectByType<DragonAttackHUD>() == null)
            new GameObject("Attack HUD").AddComponent<DragonAttackHUD>().Bind(this, dragon, vitals);
    }

    void Update()
    {
        if (DragonStatsMenu.IsOpen || dragon.IsDead) return;

        // bindings centralizados no DragonInput (preparo p/ gamepad futuro)
        if (DragonInput.AbilityDown(0)) TryUse(0);
        else if (DragonInput.AbilityDown(1)) TryUse(1);
        else if (DragonInput.AbilityDown(2)) TryUse(2);
        else if (DragonInput.AbilityDown(3)) TryUse(3);
    }

    // ============================================================== EXECUÇÃO
    public void TryUse(int slot)
    {
        var a = GetSlot(slot);
        if (a == null) return;
        if (dragon.ActionsLocked || dragon.IsResting || dragon.IsSwimming) return;
        if (Time.time < CooldownEnd(a)) return;
        if (dragon.IsFlying && !a.usableInFlight) return;

        float costMul = growth != null ? growth.EnergyCostMul : 1f;
        if (vitals != null && !vitals.TrySpend(a.energyCost * costMul)) return;

        cooldownUntil[a] = Time.time + a.cooldown;

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
    void OnLevelUp(int level) => CheckUnlocks(level, announce: true);

    void CheckUnlocks(int level, bool announce)
    {
        bool changed = false;
        foreach (var a in known)
        {
            if (unlocked.Contains(a) || a.unlockLevel > level) continue;
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

    /// <summary>Coloca no primeiro slot livre. -1 = todos ocupados.</summary>
    int AutoEquip(DragonAttackData a)
    {
        for (int i = 0; i < SlotCount; i++)
            if (slots[i] == null) { slots[i] = a; return i; }
        return -1;
    }

    /// <summary>Equipa um ataque JÁ desbloqueado num slot (troca o que estiver lá).</summary>
    public bool Equip(DragonAttackData a, int slot)
    {
        if (slot < 0 || slot >= SlotCount) return false;
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
        i >= 0 && i < SlotCount ? slots[i] : null;

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
