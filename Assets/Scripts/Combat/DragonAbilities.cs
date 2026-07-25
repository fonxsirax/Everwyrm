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
    DragonAim aimCache;           // modo mira — criado pelo DragonController (pode faltar no Awake)
    DragonAim Aim => aimCache != null ? aimCache : (aimCache = GetComponent<DragonAim>());

    // ---- MIRA DE HABILIDADE (aimBeforeFire): a tecla ABRE a mira e o clique confirma
    int aimCastSlot = -1;         // slot aguardando confirmação do disparo (-1 = nenhum)
    int fireCastLayer = -1;       // layer mascarada 'Fire Cast' (Unka Fire Mask); -1 = ausente
    Coroutine fireMaskRoutine;    // controla o peso da layer mascarada durante o gesto
    /// <summary>Uma habilidade de projétil está com a MIRA aberta, esperando o clique?
    /// O <see cref="DragonController"/> consulta isto para NÃO deixar os botões do mouse
    /// virarem golpe/sopro no chão enquanto o jogador mira.</summary>
    public bool AimCasting => aimCastSlot >= 0;
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
        // índice da layer mascarada 'Fire Cast' (Unka Fire Mask). -1 até o Animator ser
        // regenerado (Tools > Everwyrm > Regenerar Animator do Dragão) — aí o cuspe cai
        // na layer base como antes, sem quebrar.
        fireCastLayer = anim != null ? anim.GetLayerIndex("Fire Cast") : -1;

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
        // fecha a mira de habilidade se algo modal a invalidar (ficha aberta / morte)
        if (AimCasting && (DragonStatsMenu.IsOpen || dragon.IsDead)) EndAimCast();
        if (DragonStatsMenu.IsOpen || dragon.IsDead) return;

        // com a mira de habilidade aberta, o mouse é confirmar/cancelar — as teclas
        // 1-4 ficam inertes até o disparo sair (ou o jogador cancelar).
        if (AimCasting) { UpdateAimCast(); return; }

        // bindings centralizados no DragonInput (preparo p/ gamepad futuro).
        // Só até ActiveSlotCount — o slot de mutação (5) só responde a quem o tem.
        for (int i = 0; i < ActiveSlotCount; i++)
            if (DragonInput.AbilityDown(i)) { TryUse(i); break; }
    }

    // ============================================================== EXECUÇÃO
    /// <summary>Aciona o slot. Projétil com <c>aimBeforeFire</c> ABRE o modo mira e
    /// espera o clique (ver <see cref="UpdateAimCast"/>); todo o resto dispara na hora,
    /// à frente ou no alvo da mira (tecla E) se ela já estiver ligada.</summary>
    public void TryUse(int slot)
    {
        if (slot >= ActiveSlotCount) return;   // slot de mutação sem o bônus: nada
        var a = GetSlot(slot);
        if (a == null) return;

        if (a.usesProjectile && a.aimBeforeFire)
        {
            // só ABRE a mira se o disparo for viável (cooldown/energia/estado) — nada é
            // gasto aqui; a energia/cooldown só entram na confirmação (Execute).
            if (CanFire(a)) BeginAimCast(slot);
            return;
        }

        Execute(slot, null);
    }

    /// <summary>Viabilidade do disparo SEM gastar nada — compartilhada entre disparar na
    /// hora e ABRIR a mira, para o jogador não entrar em mira de uma habilidade impossível.</summary>
    bool CanFire(DragonAttackData a)
    {
        if (a == null) return false;
        if (dragon.ActionsLocked || dragon.IsResting || dragon.IsSwimming) return false;
        if (Time.time < CooldownEnd(a)) return false;
        if (dragon.IsFlying && !a.usableInFlight) return false;
        float costMul = (growth != null ? growth.EnergyCostMul : 1f)
                      * (attrs != null ? attrs.EnergyCostMul : 1f);
        return vitals == null || vitals.Energy >= a.energyCost * costMul;
    }

    /// <summary>Dispara o ataque DE FATO: gasta energia, arma o cooldown, toca a animação
    /// e agenda o efeito. <paramref name="aimOverride"/> = ponto do mundo travado no clique
    /// da mira (null = comportamento antigo: à frente, ou no alvo da mira se ligada).</summary>
    void Execute(int slot, Vector3? aimOverride)
    {
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

        // GESTO do disparo. Cuspe MASCARADO (Unka Fire Mask): toca na layer 'Fire Cast'
        // só no pescoço/cabeça, POR CIMA do voo/locomoção e SEM travar o dragão — ele
        // dispara sem parar de voar nem perder o controle. Demais ataques: estado de
        // corpo inteiro na layer base, que trava a ação como antes (as saídas do Animator
        // devolvem para Locomotion ou Fly conforme o bool "Flying").
        string maskState = fireCastLayer >= 0 ? FireMaskState(a.fireMaskCast) : null;
        if (maskState != null)
        {
            anim.Play(maskState, fireCastLayer, 0f);   // reinicia o gesto (o peso sobe do 0)
            PlayFireMask(a.animationLock);
        }
        else
        {
            anim.CrossFadeInFixedTime(StateName(a.animation), 0.1f, 0);
            dragon.LockActions(a.animationLock);
        }

        StartCoroutine(FireAfterCast(a, aimOverride));
        OnAttackFired?.Invoke(slot, a);
    }

    /// <summary>Estado na layer 'Fire Cast' para cada gesto mascarado (null = layer base).</summary>
    static string FireMaskState(DragonFireMaskCast m) => m switch
    {
        DragonFireMaskCast.FireBall => "UFireBall Mask",
        DragonFireMaskCast.FireBreath => "UFireBreathMask",
        _ => null
    };

    // Sobe o peso da layer mascarada, segura durante o gesto e desce — a layer descansa
    // em 0 (invisível). Interrompe um gesto anterior p/ os pesos não brigarem.
    void PlayFireMask(float hold)
    {
        if (fireMaskRoutine != null) StopCoroutine(fireMaskRoutine);
        fireMaskRoutine = StartCoroutine(FireMaskWeight(hold));
    }

    IEnumerator FireMaskWeight(float hold)
    {
        yield return BlendFireLayer(1f, 0.06f);
        yield return new WaitForSeconds(Mathf.Max(0.1f, hold));
        yield return BlendFireLayer(0f, 0.2f);
        fireMaskRoutine = null;
    }

    IEnumerator BlendFireLayer(float target, float time)
    {
        float start = anim.GetLayerWeight(fireCastLayer);
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            anim.SetLayerWeight(fireCastLayer, Mathf.Lerp(start, target, t / time));
            yield return null;
        }
        anim.SetLayerWeight(fireCastLayer, target);
    }

    // ------------------------------------------------- modo mira de habilidade
    void BeginAimCast(int slot)
    {
        aimCastSlot = slot;
        if (Aim != null) Aim.SetAimActive(true);   // abre retícula + câmera de ombro
    }

    /// <summary>Roda a cada frame enquanto a mira de habilidade está aberta: clique
    /// esquerdo confirma, direito cancela, e qualquer invalidação (mira fechada por fora,
    /// travou, começou a nadar) cancela sem custo.</summary>
    void UpdateAimCast()
    {
        var a = GetSlot(aimCastSlot);
        // mira fechada por fora (tecla E, nado, despossessão) ou estado inválido → cancela
        if (a == null || Aim == null || !Aim.Active
            || dragon.ActionsLocked || dragon.IsResting || dragon.IsSwimming)
        { EndAimCast(); return; }

        // ESQUERDO confirma: FIXA o alvo agora e dispara exatamente nessa direção
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 target = Aim.AimTargetPoint;   // retícula (ou alvo do soft-lock)
            int slot = aimCastSlot;
            EndAimCast();                          // encerra a mira ANTES do disparo
            DragonInput.Clear(DragonInput.Act.Melee);   // o mesmo clique não vira golpe no chão
            Execute(slot, target);
            return;
        }

        // DIREITO cancela: sai da mira sem gastar energia nem entrar em cooldown
        if (Input.GetMouseButtonDown(1))
        {
            EndAimCast();
            DragonInput.Clear(DragonInput.Act.Fire);    // o mesmo clique não vira sopro no chão
        }
    }

    void EndAimCast()
    {
        if (aimCastSlot < 0) return;
        aimCastSlot = -1;
        if (Aim != null && Aim.Active) Aim.SetAimActive(false);
    }

    IEnumerator FireAfterCast(DragonAttackData a, Vector3? aimOverride)
    {
        if (a.castTime > 0f) yield return new WaitForSeconds(a.castTime);
        if (dragon.IsDead) yield break;

        float scale = dragon.BodyScale;                       // crescimento × atributos
        float dmgMul = attrs != null ? attrs.DamageMul : 1f;  // Poder
        float radiusMul = a.isFire && attrs != null ? attrs.FlameSizeMul : 1f;
        float damage = a.baseDamage * dmgMul * scale;

        // MODO MIRA: o sopro/projétil e o melee apontam para o alvo da retícula
        // (ponto sob o cursor, ou o animal agarrado no SoftLock). Fora da mira, tudo
        // sai pra FRENTE do corpo como sempre. Área (Incinerate) fica centrada no dragão.
        bool aiming = Aim != null && Aim.Active;

        if (a.usesProjectile)
        {
            Vector3 mouth = transform.position
                          + transform.forward * (2.4f * scale)
                          + Vector3.up * (1.6f * scale);
            // direção: ponto TRAVADO no clique da mira (aimBeforeFire) > mira livre (E) > frente
            Vector3 dir;
            if (aimOverride.HasValue)
            {
                Vector3 d = aimOverride.Value - mouth;
                dir = d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;
            }
            else dir = aiming ? Aim.AimDirectionFrom(mouth) : transform.forward;
            AttackProjectile.Launch(a, mouth, dir, damage,
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
            Vector3 dir = aiming ? Aim.AimDirectionFrom(transform.position) : transform.forward;
            Vector3 p = transform.position + dir * (a.range * 0.5f * scale);
            var target = aiming && Aim.LockedAgent != null
                       ? Aim.LockedAgent
                       : AnimalAgent.FindNearest(p, a.range * scale);
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
    /// <summary>Restaura o BÔNUS DE SLOT (mutação) e o loadout salvo (referências de
    /// ataque) de um DragonRecord, depois reavalia desbloqueios e preenche qualquer
    /// slot ATIVO ainda vazio com algo já desbloqueado. Essa última parte cobre a
    /// ordem de Awake/Start entre componentes NÃO ser garantida: se o bônus de slot
    /// só chegar depois do Start (que já rodou CheckUnlocks com ActiveSlotCount
    /// menor), o 5º slot apareceria vazio até o próximo degrau sem este passe.</summary>
    public void LoadFrom(DragonRecord record)
    {
        bonusSlots = record.bonusAttackSlots;
        EnsureKnown();

        var equipped = record.state.equippedAttacks;
        if (equipped != null && equipped.Length > 0)
        {
            for (int i = 0; i < MaxSlotCount; i++)
            {
                var a = i < equipped.Length ? equipped[i] : null;
                slots[i] = a;
                // referência direta: se o ataque não estava no catálogo (fora de
                // Resources/Attacks), entra por ele mesmo — já vem desbloqueado.
                if (a != null)
                {
                    if (!known.Contains(a)) known.Add(a);
                    if (!unlocked.Contains(a)) unlocked.Add(a);
                }
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
        // grava o loadout VIVO do dragão como referências de ataque (o "ataque atual"
        // é o que está nos slots agora), null em slot vazio.
        var equipped = new DragonAttackData[MaxSlotCount];
        for (int i = 0; i < MaxSlotCount; i++)
            equipped[i] = slots[i];
        s.equippedAttacks = equipped;
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
