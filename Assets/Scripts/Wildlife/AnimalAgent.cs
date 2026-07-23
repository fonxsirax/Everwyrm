using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A IA universal da fauna — UM script para TODAS as espécies (GDD: um único
/// sistema de IA parametrizável). Quem decide "quem" o animal é, é o
/// AnimalDefinition: coragem, curiosidade, pastar, cavar, tocaia, uivo...
///
/// Camadas de decisão:
///  1. GRUPO (AnimalGroup) — fuga coletiva, migração, caça, alimentação;
///  2. PESSOAL — vigília, pastar, beber, descansar, dormir, brincar, extras;
///  3. AMEAÇA — dragão (tamanho importa!) e predadores reais (lobos → cervos).
///
/// Movimento cinemático sobre o terreno procedural (HeightAt), sem NavMesh —
/// mesmo padrão do resto do projeto. Animação: estado "Locomotion" (blend tree
/// Turn × Speed) + CrossFade direto para os DEMAIS clipes do asset por nome.
/// </summary>
public class AnimalAgent : MonoBehaviour
{
    // Registro global (padrão Carcass.All): minimapa, ataques do dragão, presas.
    public static readonly List<AnimalAgent> All = new();

    static readonly int P_Speed = Animator.StringToHash("Speed");
    static readonly int P_Turn = Animator.StringToHash("Turn");

    public AnimalDefinition Def { get; private set; }
    public AnimalDefinition.AnimalRole Role { get; private set; }
    public AnimalGroup Group { get; private set; }
    public AnimalAgent Mother { get; internal set; }   // filhotes nunca andam sós
    public Carcass Corpse { get; private set; }
    public bool IsDead => behaviour == B.Dead;
    public bool IsYoung => Role != null && Role.kind == AnimalDefinition.RoleKind.Filhote;
    public bool IsLeader => Group != null && Group.Leader == this;

    enum B
    {
        Settle, Follow, Wander, Graze, Drink, Dig, Rest, Sleep, Hide, Play,
        Alert, Investigate, Flee, Attack, Stalk, Chase, Feed, Migrate, Howl, Dead
    }

    B behaviour = B.Settle;
    float behaviourUntil;

    Animator anim;
    readonly Dictionary<string, float> clipLen = new();
    int slotIndex;
    float hp;

    // ---- personalidade individual: nenhum animal é idêntico ao vizinho
    float braveryMul, curiosityMul, tempoMul;

    // ---- pooling: escala original do prefab (Init multiplica, nunca acumula)
    Vector3 baseScale;
    bool baseScaleKnown;

    // ---- movimento
    Vector3 pos;
    float yaw, yawVel, speed, moveSpeed;
    Vector3 moveTarget;
    bool hasMoveTarget;
    float arriveRadius = 1.4f;
    string gaitOverride;              // Crouch_F_IP / Eat_walk_IP / Dig_walk_IP
    Vector3 groundNormal = Vector3.up;
    float nextNormalSample, nextGroundSample;
    float groundY;

    // ---- pensamento em fatias (perto pensa rápido, longe pensa devagar)
    float nextThink;

    // ---- performer de sequências start→loop→end
    AnimalDefinition.AnimSequence seq;
    int seqPhase;                     // 0 nada · 1 start · 2 loop · 3 end
    float seqPhaseEnd, seqLoopUntil;

    // ---- estados auxiliares
    Vector3 threatPos;
    float lastHitAt = -99f, nextMelee, diedAt;
    float myHowlAt = -1f;             // resposta em coro ao uivo do líder
    bool respondedHowl;
    float stalkPauseUntil, nextStalkRoll;   // tocaia: avanço ↔ congelar
    AnimalAgent playmate;                   // brincadeira entre membros do grupo

    bool inLocomotion;

    // ================================================================ SETUP
    public void Init(AnimalDefinition def, AnimalDefinition.AnimalRole role,
                     AnimalGroup group, int slot, float scale)
    {
        Def = def;
        Role = role;
        Group = group;
        slotIndex = slot;
        hp = def.health * (IsYoung ? 0.45f : 1f);

        // ---- reset completo: com o pool, este corpo pode já ter vivido outra vida
        Corpse = null; Mother = null; playmate = null;
        seq = null; seqPhase = 0;
        gaitOverride = null; currentGait = null; currentState = null;
        inLocomotion = false; posture = Posture.Stand;
        hasMoveTarget = false; hasFaceTarget = false;
        speed = 0f; yawVel = 0f;
        lastHitAt = -99f; nextMelee = 0f; diedAt = 0f;
        myHowlAt = -1f; respondedHowl = false;
        stalkPauseUntil = 0f; nextStalkRoll = 0f;
        groundNormal = Vector3.up;
        clipLen.Clear();

        if (!baseScaleKnown) { baseScale = transform.localScale; baseScaleKnown = true; }
        transform.localScale = baseScale * scale;
        pos = transform.position;
        groundY = pos.y;
        yaw = prevYaw = transform.eulerAngles.y;

        // jitter de personalidade — dois cervos nunca são o mesmo cervo
        braveryMul = Random.Range(0.75f, 1.3f);
        curiosityMul = Random.Range(0.7f, 1.4f);
        tempoMul = Random.Range(0.85f, 1.15f);

        anim = GetComponent<Animator>();
        anim.enabled = true;                 // cadáver reciclado desligou o Animator
        anim.runtimeAnimatorController = role.controller;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        anim.Rebind();                       // limpa a pose/estado da vida anterior

        if (role.controller != null)
            foreach (var c in role.controller.animationClips)
            {
                if (c == null) continue;
                string n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                clipLen[n] = c.length;
            }

        ApplyVisualVariation();

        group.Register(this);
        nextThink = Time.time + Random.value * 0.4f;   // desfaz sincronização
        behaviour = B.Settle;
        behaviourUntil = Time.time + Random.Range(1f, 4f);
    }

    /// <summary>
    /// Nenhum bando é igual ao outro: além do prefab de cor (c1..cN) sorteado
    /// pelo spawner, cada indivíduo ganha um tom sutil próprio via
    /// MaterialPropertyBlock — zero materiais novos instanciados.
    /// </summary>
    void ApplyVisualVariation()
    {
        var mpb = new MaterialPropertyBlock();
        float h = Random.Range(-0.03f, 0.03f);
        float v = Random.Range(0.88f, 1.06f);
        Color tint = Color.HSVToRGB(Mathf.Repeat(0.08f + h, 1f), 0.12f, 1f);
        tint = Color.Lerp(Color.white, tint, 0.5f) * v;
        tint.a = 1f;
        mpb.SetColor("_BaseColor", tint);
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>())
            r.SetPropertyBlock(mpb);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <summary>Remoção limpa pelo spawner (fora do raio). Corpo e carcaça juntos.</summary>
    public void Despawn()
    {
        PrepareRelease();
        WildlifePool.Release(gameObject);
    }

    /// <summary>
    /// Ninguém pode continuar apontando para um corpo que voltou ao pool —
    /// na próxima vida ele será OUTRO animal (talvez de outra espécie).
    /// </summary>
    void PrepareRelease()
    {
        if (Corpse != null) { Destroy(Corpse.gameObject); Corpse = null; }
        if (Group != null)
            foreach (var m in Group.Members)
            {
                if (m == this) continue;
                if (m.Mother == this) m.Mother = null;
                if (m.playmate == this) m.playmate = null;
            }
    }

    // ================================================================ UPDATE
    void Update()
    {
        if (Def == null) return;
        float dt = Time.deltaTime;

        if (behaviour == B.Dead) { DeadTick(dt); return; }

        float now = Time.time;
        if (now >= nextThink)
        {
            float d = DistToPlayer();
            nextThink = now + (d < 120f ? 0.3f : d < 250f ? 0.7f : 1.4f) *
                        Random.Range(0.85f, 1.15f);
            Think();
        }

        TickPerformer(now);
        MoveTick(dt);
        UpdateAnimator(dt);
    }

    float DistToPlayer()
    {
        var p = WildlifeSpawner.Dragon;
        return p != null ? Vector3.Distance(pos, p.position) : 999f;
    }

    // ================================================================= THINK
    void Think()
    {
        // 1) ameaças têm prioridade sobre tudo
        if (EvaluateThreat()) return;

        // 2) ordens do grupo
        switch (Group.State)
        {
            case AnimalGroup.GroupState.Fleeing:
                if (behaviour == B.Flee) FleeStep(Group.FleeFrom);
                else if (behaviour != B.Attack) EnterFlee(Group.FleeFrom);
                return;

            case AnimalGroup.GroupState.Migrating:
                MigrateThink();
                return;

            case AnimalGroup.GroupState.Hunting:
                if (!IsYoung) { HuntThink(); return; }
                break;

            case AnimalGroup.GroupState.Feeding:
                if (!IsYoung && Group.FeedCarcass != null) { FeedThink(); return; }
                break;
        }

        // 3) coro de uivos — membros respondem ao líder com atraso natural
        HowlThink();

        // 4) vida pessoal
        PersonalThink();
    }

    /// <summary>
    /// Quão perceptível o dragão é para a fauna: fração do raio nominal.
    /// No chão ~1/3; voando ainda menos (uma sombra lá no alto não é ameaça);
    /// em STEALTH (Shift, passo de caçada) quase nada — dá pra tocaiar de perto.
    /// Filhote é pequeno e discreto; um colossal é impossível de não notar.
    /// </summary>
    static float DragonPerceptionMul
    {
        get
        {
            float mul = WildlifeSpawner.DragonFlying ? 0.22f
                      : WildlifeSpawner.DragonStealth ? 0.12f
                      : 0.35f;
            return mul * Mathf.Lerp(0.85f, 1f, WildlifeSpawner.DragonGrowth01);
        }
    }

    // ------------------------------------------------------------- AMEAÇAS
    /// <summary>Dragão e predadores. true = a ameaça tomou a decisão do frame.</summary>
    bool EvaluateThreat()
    {
        float now = Time.time;
        bool found = false;
        Vector3 tPos = default;
        float tGrowth = 0.5f;   // "tamanho" da ameaça 0..1
        float tDist = float.MaxValue;
        float tMul = 1f;        // fração do raio de percepção (dragão < predadores)

        // ---- o dragão do jogador
        var dragon = WildlifeSpawner.Dragon;
        if (dragon != null)
        {
            float perception = DragonPerceptionMul;
            float dist = Vector3.Distance(pos, dragon.position);
            if (dist < Def.sightRange * 1.3f * perception)
            {
                float growth = WildlifeSpawner.DragonGrowth01;
                float altitude = dragon.position.y - pos.y;
                bool ignorable = growth < Def.ignoreBelowGrowth && dist > Def.fleeDistance * 0.3f;

                // sobrevoo alto não assusta — pousar perto, sim
                bool highOverhead = WildlifeSpawner.DragonFlying && altitude > 32f;
                if (!ignorable && !highOverhead)
                {
                    found = true; tPos = dragon.position; tGrowth = growth;
                    tDist = dist; tMul = perception;
                }
            }
        }

        // ---- predadores de verdade (lobo assusta cervo, raposa assusta lebre)
        if (!Def.predator)
            foreach (var a in All)
            {
                if (a == this || a.IsDead || a.Group == Group || !a.Def.predator) continue;
                if (!PreysOnMe(a.Def)) continue;
                float dist = Vector3.Distance(pos, a.pos);
                // tocaia é furtiva: só percebe mais perto (vigilância ajuda)
                float detect = a.behaviour == B.Stalk
                    ? Def.fleeDistance * (0.35f + 0.45f * Def.alertness)
                    : Def.fleeDistance * 0.95f;
                if (dist < detect && dist < tDist)
                {
                    found = true; tPos = a.pos; tGrowth = 0.35f;
                    tDist = dist; tMul = 1f;
                }
            }

        if (!found)
        {
            // ameaça sumiu — sai de estados reativos
            if (behaviour == B.Alert || behaviour == B.Investigate || behaviour == B.Attack)
                if (now >= behaviourUntil) EnterSettle(1f);
            return false;
        }

        threatPos = tPos;

        // ---- distância efetiva de fuga: coragem × tamanho da ameaça × percepção
        float sizeFactor = Mathf.Lerp(0.55f, 1.5f, tGrowth);
        float brave = Mathf.Lerp(1.45f, 0.55f, Mathf.Clamp01(Def.bravery * braveryMul));
        float fleeDist = Def.fleeDistance * sizeFactor * brave * tMul;
        bool panic = tGrowth >= Def.panicAboveGrowth;
        if (panic) fleeDist = Def.fleeDistance * 2f * tMul;

        if (behaviour == B.Attack)
        {
            AttackTick(tDist);
            return true;
        }

        if (tDist < fleeDist)
        {
            // atacar em vez de fugir? (alce provocado, javali imprevisível, ursa com filhotes)
            bool canAttack = Def.attackRange > 0f && !IsYoung && !panic &&
                             tDist < Def.attackRange * 1.4f;
            float attackRoll = Def.aggression * (1f - tGrowth * 0.7f) +
                               Def.unpredictability * (Random.value - 0.35f);
            if (canAttack && Random.value < attackRoll)
            {
                EnterAttack();
                return true;
            }

            Group.RaiseAlarm(tPos);
            EnterFlee(tPos);
            return true;
        }

        // viu de longe: alerta / curiosidade / indiferença (alce nem levanta a cabeça)
        if (tDist < Def.sightRange * tMul && behaviour != B.Flee)
        {
            bool indifferent = Def.bravery * braveryMul > 0.9f && !panic;
            if (indifferent) return false;

            if (behaviour != B.Alert && behaviour != B.Investigate)
            {
                if (Random.value < Def.curiosity * curiosityMul * 0.6f && tGrowth < 0.6f)
                    EnterInvestigate();
                else
                    EnterAlert();
            }
            else if (behaviour == B.Investigate)
                InvestigateTick(tDist, fleeDist);
            return true;
        }

        return false;
    }

    bool PreysOnMe(AnimalDefinition other)
    {
        if (other.preySpecies == null) return false;
        foreach (var s in other.preySpecies)
            if (s == Def.speciesName) return true;
        return false;
    }

    void EnterAlert()
    {
        behaviour = B.Alert;
        behaviourUntil = Time.time + Random.Range(4f, 8f);
        StopMoving();
        CancelPerformance();
        FaceTowards(threatPos);

        // urso levanta em duas patas para intimidar — momento assinatura
        if (Role.anims.stand.IsValid && Random.value < 0.6f)
            Perform(Role.anims.stand, Random.Range(4f, 7f));
        else if (Role.anims.idles != null && Role.anims.idles.Length > 0)
            PlayState(Pick(Role.anims.idles));
    }

    void EnterInvestigate()
    {
        behaviour = B.Investigate;
        behaviourUntil = Time.time + Random.Range(8f, 14f);
        CancelPerformance();
        MoveTo(threatPos, Def.walkSpeed * 0.85f);
    }

    void InvestigateTick(float dist, float fleeDist)
    {
        // aproxima com cautela até a borda do conforto, então para e encara
        if (dist <= fleeDist * 1.2f || Time.time >= behaviourUntil)
        {
            StopMoving();
            FaceTowards(threatPos);
            if (Time.time >= behaviourUntil) EnterSettle(2f);
        }
        else
            MoveTo(threatPos, Def.walkSpeed * 0.85f);
    }

    // --------------------------------------------------------------- FUGA
    void EnterFlee(Vector3 from)
    {
        behaviour = B.Flee;
        behaviourUntil = Time.time + Random.Range(6f, 10f);
        CancelPerformance();
        gaitOverride = null;
        FleeStep(from);
    }

    void FleeStep(Vector3 from)
    {
        Vector3 away;
        if (IsYoung && Mother != null && !Mother.IsDead)
        {
            // filhote corre PARA A MÃE, não para longe — família de verdade
            away = (Mother.pos - pos).normalized;
            MoveTo(Mother.pos + away * 2f, Def.runSpeed * Role.speedMul);
            return;
        }
        away = pos - from; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
        away.Normalize();
        // jitter para o rebanho não virar uma linha reta de clones
        away = Quaternion.Euler(0f, Random.Range(-22f, 22f), 0f) * away;
        MoveTo(pos + away * 40f, Def.runSpeed * Role.speedMul);
    }

    void FleeThinkTick()
    {
        var dragon = WildlifeSpawner.Dragon;
        float dDragon = dragon != null ? Vector3.Distance(pos, dragon.position) : 999f;
        if (dDragon < Def.fleeDistance * 1.6f * DragonPerceptionMul)
        {
            Group.SustainAlarm(dragon.position);
            FleeStep(dragon.position);
            behaviourUntil = Time.time + 4f;
            return;
        }

        if (Time.time >= behaviourUntil)
        {
            // lebre: some no mato e congela — Hide_start/loop/end
            if (Role.anims.hide.IsValid && Random.value < 0.55f)
            {
                behaviour = B.Hide;
                behaviourUntil = Time.time + Random.Range(6f, 14f);
                StopMoving();
                Perform(Role.anims.hide, Random.Range(5f, 12f));
            }
            else EnterSettle(2f);
        }
        else
            FleeStep(Group.State == AnimalGroup.GroupState.Fleeing ? Group.FleeFrom : threatPos);
    }

    // -------------------------------------------------------------- ATAQUE
    void EnterAttack()
    {
        behaviour = B.Attack;
        behaviourUntil = Time.time + Random.Range(8f, 13f);
        CancelPerformance();
        gaitOverride = null;
    }

    void AttackTick(float dist)
    {
        var dragon = WildlifeSpawner.Dragon;
        if (dragon == null || Time.time >= behaviourUntil ||
            dist > Def.attackRange * 2.2f || WildlifeSpawner.DragonFlying)
        {
            // javali é imprevisível: depois da investida pode simplesmente fugir
            if (Random.value < Def.unpredictability) { Group.RaiseAlarm(threatPos); EnterFlee(threatPos); }
            else EnterSettle(1.5f);
            return;
        }

        float melee = Def.meleeRange * transform.localScale.y;
        if (dist <= melee + 1.2f)
        {
            StopMoving();
            FaceTowards(dragon.position);
            if (Time.time >= nextMelee)
            {
                nextMelee = Time.time + Random.Range(1.6f, 2.4f);
                if (Role.anims.attacks != null && Role.anims.attacks.Length > 0)
                    PerformOnce(Pick(Role.anims.attacks));
                if (dist <= melee + 1.6f)
                    WildlifeSpawner.DamageDragon(Def.attackDamage, transform.position);
            }
        }
        else
        {
            // investida com a animação própria quando existir (Attack_Run_IP)
            gaitOverride = !string.IsNullOrEmpty(Role.anims.chargeAttack) &&
                           dist < Def.attackRange * 0.6f
                ? Role.anims.chargeAttack : null;
            MoveTo(dragon.position, Def.runSpeed);
        }
    }

    // ---------------------------------------------------------------- CAÇA
    void HuntThink()
    {
        var prey = Group.HuntTarget;
        if (prey == null || prey.IsDead) { EnterSettle(1f); return; }

        float dist = Vector3.Distance(pos, prey.pos);

        // tocaia: raposa/lobo abaixados (Crouch_F_IP) até a distância do bote,
        // alternando avanço e congelamento (Crouch_Idle) como um felino
        bool canStalk = !string.IsNullOrEmpty(Role.anims.stalkWalk);
        if (canStalk && dist > 17f && behaviour != B.Chase)
        {
            behaviour = B.Stalk;
            float now = Time.time;

            if (now < stalkPauseUntil)
            {
                StopMoving();
                if (!Performing && Role.anims.stalkIdle.IsValid)
                    Perform(Role.anims.stalkIdle, stalkPauseUntil - now);
                return;
            }
            if (now >= nextStalkRoll)
            {
                nextStalkRoll = now + Random.Range(2.5f, 5f);
                if (Random.value < 0.35f)
                {
                    stalkPauseUntil = now + Random.Range(1.2f, 2.8f);
                    StopMoving();
                    if (Role.anims.stalkIdle.IsValid)
                        Perform(Role.anims.stalkIdle, stalkPauseUntil - now);
                    return;
                }
            }

            gaitOverride = Role.anims.stalkWalk;
            MoveTo(prey.pos, Def.walkSpeed * 0.9f);
            return;
        }

        behaviour = B.Chase;
        gaitOverride = null;
        CancelPerformance();   // se congelou na tocaia, o bote não pode esperar o end
        MoveTo(prey.pos, Def.runSpeed * 1.08f);   // sprint (RunFast entra no blend)

        float melee = Def.meleeRange * transform.localScale.y;
        if (dist <= melee + 0.8f)
        {
            if (Role.anims.attacks != null && Role.anims.attacks.Length > 0)
                PerformOnce(Pick(Role.anims.attacks));
            prey.Kill(pos);
            Group.NotifyKill(prey);
        }
    }

    void FeedThink()
    {
        var c = Group.FeedCarcass;
        if (c == null || c.IsEmpty) { EnterSettle(1f); return; }

        float dist = Vector3.Distance(pos, Group.FeedPos);
        if (dist > 3.2f)
        {
            behaviour = B.Feed;
            gaitOverride = null;
            MoveTo(Group.FeedPos + (pos - Group.FeedPos).normalized * 1.8f, Def.trotSpeed);
            return;
        }

        StopMoving();
        FaceTowards(Group.FeedPos);
        if (behaviour != B.Feed || !Performing)
        {
            behaviour = B.Feed;
            string eat = !string.IsNullOrEmpty(Role.anims.eatCarcass)
                ? Role.anims.eatCarcass
                : Role.anims.graze.IsValid ? Role.anims.graze.loops[0] : null;
            if (eat != null)
            {
                var s = new AnimalDefinition.AnimSequence { loops = new[] { eat } };
                Perform(s, Random.Range(6f, 10f));
            }
            c.Consume();   // predadores gastam a carcaça — o dragão pode roubar o resto!
        }
    }

    // ------------------------------------------------------------ MIGRAÇÃO
    void MigrateThink()
    {
        behaviour = B.Migrate;
        gaitOverride = null;
        CancelPerformanceSoft();

        if (IsLeader)
        {
            MoveTo(Group.MigrationTarget, Def.trotSpeed * 0.9f * tempoMul);
            return;
        }
        FollowSlot(Def.trotSpeed);
    }

    // ------------------------------------------------------ COESÃO DE GRUPO
    /// <summary>
    /// Segue a posição de conforto no grupo. Ficou para trás → acelera até
    /// alcançar; chegou → retoma o passo normal. Filhotes colam na mãe.
    /// </summary>
    void FollowSlot(float baseSpeed)
    {
        Vector3 slot;
        if (IsYoung && Mother != null && !Mother.IsDead)
            slot = Mother.pos + (pos - Mother.pos).normalized * 1.7f;
        else
            slot = Group.Anchor + Group.SlotFor(slotIndex);

        float dist = Vector3.Distance(pos, slot);
        float sp = baseSpeed;
        if (dist > Def.catchUpDistance * 2f) sp = Def.runSpeed;          // muito atrás: corre
        else if (dist > Def.catchUpDistance) sp = Mathf.Max(sp, Def.trotSpeed); // atrás: troteia
        else if (dist < Def.memberSpacing * 0.6f) { StopMoving(); return; }

        MoveTo(slot, sp * Role.speedMul);
    }

    // ------------------------------------------------------- VIDA PESSOAL
    void PersonalThink()
    {
        float now = Time.time;

        // fuga/esconderijo em curso vêm antes de qualquer coesão
        if (behaviour == B.Flee) { FleeThinkTick(); return; }
        if (behaviour == B.Hide)
        {
            if (now >= behaviourUntil) EnterSettle(1f);
            return;
        }

        // longe do bando? primeiro volta pra perto (coesão contínua)
        if (!IsLeader)
        {
            Vector3 slot = IsYoung && Mother != null && !Mother.IsDead
                ? Mother.pos : Group.Anchor + Group.SlotFor(slotIndex);
            if (Vector3.Distance(pos, slot) > Def.catchUpDistance)
            {
                behaviour = B.Follow;
                CancelPerformanceSoft();
                gaitOverride = null;
                FollowSlot(Def.walkSpeed);
                return;
            }
        }

        // comportamento atual ainda vale?
        if (now < behaviourUntil &&
            behaviour != B.Follow && behaviour != B.Migrate && behaviour != B.Feed &&
            behaviour != B.Chase && behaviour != B.Stalk)
        {
            if (behaviour == B.Wander || behaviour == B.Play)
                if (!hasMoveTarget) NudgeWander();

            // chegou à margem: abaixa e bebe (EatDrink_start → Drink_loop → end)
            if (behaviour == B.Drink && !hasMoveTarget && !Performing &&
                Role.anims.drink.IsValid)
                Perform(Role.anims.drink,
                        Mathf.Min(behaviourUntil - now, Random.Range(6f, 11f)));

            // vigilância: quem pasta levanta a cabeça de tempos em tempos e
            // varre o horizonte — espécies mais alertas fazem isso mais vezes
            if (behaviour == B.Graze && Random.value < Def.alertness * 0.11f)
            {
                gaitOverride = null;
                StopMoving();
                CancelPerformanceSoft();   // EatDrink_end = a cabeça subindo
                behaviour = B.Settle;
                behaviourUntil = now + Random.Range(1.5f, 3.5f);
            }
            return;
        }

        PickCalmBehaviour();
    }

    void NudgeWander()
    {
        if (behaviour == B.Play)
        {
            var buddy = IsYoung && Mother != null && !Mother.IsDead ? Mother : playmate;
            if (buddy != null && !buddy.IsDead)
            {
                // orbita o parceiro em arcos curtos — e dá pulinhos
                Vector2 c = Random.insideUnitCircle.normalized * Random.Range(2.5f, 4.5f);
                MoveTo(buddy.pos + new Vector3(c.x, 0f, c.y), Def.runSpeed * 0.75f * Role.speedMul);
                if (!string.IsNullOrEmpty(Role.anims.jumpPlace) && Random.value < 0.3f)
                    PerformOnce(Role.anims.jumpPlace);
                return;
            }
        }
        Vector2 o = Random.insideUnitCircle * Def.memberSpacing * 1.6f;
        Vector3 baseP = IsLeader ? Group.Home : Group.Anchor + Group.SlotFor(slotIndex);
        // líderes territoriais rondam o território; nômades derivam
        if (IsLeader && !Def.territorial)
            baseP = pos + new Vector3(Random.Range(-14f, 14f), 0f, Random.Range(-14f, 14f));
        MoveTo(baseP + new Vector3(o.x, 0f, o.y), Def.walkSpeed * tempoMul);
    }

    /// <summary>Sorteio ponderado pela personalidade — o coração do "cada espécie É diferente".</summary>
    void PickCalmBehaviour()
    {
        var a = Role.anims;

        bool sleepy = (Def.activity & WildlifeClock.Current) == 0;   // fora do período ativo

        // filhotes: brincar ou seguir a mãe
        if (IsYoung && Random.value < Def.playfulness * 0.65f)
        {
            StartPlay(Mother);
            return;
        }

        float wGraze = (a.graze.IsValid || !string.IsNullOrEmpty(a.grazeWalk)) ? Def.grazing * 1.3f : 0f;
        float wDig = (!string.IsNullOrEmpty(a.digWalk) || a.dig.IsValid) ? Def.grazing * 0.7f : 0f;
        float wDrink = a.drink.IsValid ? Def.waterAffinity * 0.5f : 0f;
        float wRest = (a.lie.IsValid || a.sit.IsValid || a.lieBelly.IsValid) ? Def.restfulness : 0f;
        float wSleep = a.sleep.IsValid ? Def.restfulness * (sleepy ? 1.6f : 0.25f) : 0f;
        float wIdle = 0.55f + Def.alertness * 0.6f;
        float wWander = 0.7f;
        float wExtra = (a.extras != null && a.extras.Length > 0) ? 0.12f : 0f;
        // adultos sociais e brincalhões (lobos, raposas) também brincam entre si
        float wPlay = (!IsYoung && Def.playfulness > 0.55f && Group.Members.Count > 1)
            ? Def.playfulness * 0.25f : 0f;

        // o líder é o sentinela do bando: vigia mais, deita/dorme menos
        if (IsLeader) { wIdle *= 1.6f; wRest *= 0.5f; wSleep *= 0.5f; }

        float total = wGraze + wDig + wDrink + wRest + wSleep + wIdle + wWander + wExtra + wPlay;
        float r = Random.value * total;

        if ((r -= wGraze) < 0f) { StartGraze(); return; }
        if ((r -= wDig) < 0f) { StartDig(); return; }
        if ((r -= wDrink) < 0f) { StartDrink(); return; }
        if ((r -= wRest) < 0f) { StartRest(); return; }
        if ((r -= wSleep) < 0f) { StartSleep(); return; }
        if ((r -= wIdle) < 0f) { StartIdle(); return; }
        if ((r -= wExtra) < 0f && wExtra > 0f) { StartExtra(); return; }
        if ((r -= wPlay) < 0f && wPlay > 0f) { StartPlay(PickPlaymate()); return; }
        StartWander();
    }

    void StartPlay(AnimalAgent buddy)
    {
        behaviour = B.Play;
        behaviourUntil = Time.time + Random.Range(5f, 10f);
        CancelPerformanceSoft();
        playmate = buddy;
        NudgeWander();
    }

    AnimalAgent PickPlaymate()
    {
        AnimalAgent best = null;
        float bestSqr = float.MaxValue;
        foreach (var m in Group.Members)
        {
            if (m == this || m.IsDead) continue;
            float sqr = (m.pos - pos).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = m; }
        }
        return best;
    }

    void StartIdle()
    {
        behaviour = B.Settle;
        behaviourUntil = Time.time + Random.Range(4f, 9f);
        StopMoving();
        // vigilância: espécies alertas trocam de idle com frequência (olhando em volta)
        if (Role.anims.idles != null && Role.anims.idles.Length > 0)
            PlayState(Pick(Role.anims.idles));
        else EnsureLocomotion();
    }

    void StartWander()
    {
        behaviour = B.Wander;
        behaviourUntil = Time.time + Random.Range(6f, 12f);
        gaitOverride = null;
        CancelPerformanceSoft();
        NudgeWander();
    }

    void StartGraze()
    {
        behaviour = B.Graze;
        behaviourUntil = Time.time + Random.Range(10f, 22f);
        var a = Role.anims;
        // cervos pastam ANDANDO (Eat_walk_IP) metade do tempo — rebanho vivo
        if (!string.IsNullOrEmpty(a.grazeWalk) && Random.value < 0.45f)
        {
            gaitOverride = a.grazeWalk;
            Vector2 o = Random.insideUnitCircle * 7f;
            MoveTo(pos + new Vector3(o.x, 0f, o.y), Def.walkSpeed * 0.55f);
        }
        else if (a.graze.IsValid)
        {
            StopMoving();
            Perform(a.graze, Random.Range(8f, 18f));
        }
    }

    void StartDig()
    {
        behaviour = B.Dig;
        behaviourUntil = Time.time + Random.Range(8f, 16f);
        var a = Role.anims;
        if (!string.IsNullOrEmpty(a.digWalk) && Random.value < 0.6f)
        {
            // javali fuçando o chão enquanto avança
            gaitOverride = a.digWalk;
            Vector2 o = Random.insideUnitCircle * 6f;
            MoveTo(pos + new Vector3(o.x, 0f, o.y), Def.walkSpeed * 0.45f);
        }
        else if (a.dig.IsValid)
        {
            StopMoving();
            Perform(a.dig, Random.Range(6f, 12f));
        }
    }

    void StartDrink()
    {
        // só bebe se achar margem de lago por perto — senão pasta
        if (!FindWaterEdge(out Vector3 edge)) { StartGraze(); return; }
        behaviour = B.Drink;
        behaviourUntil = Time.time + Random.Range(14f, 24f);
        gaitOverride = null;
        CancelPerformanceSoft();
        MoveTo(edge, Def.walkSpeed);
    }

    void StartRest()
    {
        behaviour = B.Rest;
        behaviourUntil = Time.time + Random.Range(12f, 26f);
        StopMoving();
        var a = Role.anims;
        var options = new List<AnimalDefinition.AnimSequence>(3);
        if (a.lie.IsValid) options.Add(a.lie);
        if (a.lieBelly.IsValid) options.Add(a.lieBelly);
        if (a.sit.IsValid) options.Add(a.sit);
        if (options.Count > 0)
            Perform(options[Random.Range(0, options.Count)], Random.Range(10f, 22f));
    }

    void StartSleep()
    {
        behaviour = B.Sleep;
        behaviourUntil = Time.time + Random.Range(20f, 40f);
        StopMoving();
        if (Role.anims.sleep.IsValid)
            Perform(Role.anims.sleep, Random.Range(18f, 35f));
    }

    void StartExtra()
    {
        behaviour = B.Settle;
        behaviourUntil = Time.time + 5f;
        StopMoving();
        PerformOnce(Pick(Role.anims.extras));   // Scratching etc. — os detalhes que vendem vida
    }

    // ---------------------------------------------------------------- UIVO
    public void OrderHowl()
    {
        if (IsDead || string.IsNullOrEmpty(Role.anims.howl)) return;
        behaviour = B.Howl;
        behaviourUntil = Time.time + Len(Role.anims.howl) + 0.5f;
        StopMoving();
        PerformOnce(Role.anims.howl);
        respondedHowl = true;
    }

    void HowlThink()
    {
        if (string.IsNullOrEmpty(Role.anims.howl) || IsLeader) return;
        float since = Time.time - Group.HowlAt;
        if (since < 0f || since > 8f) { respondedHowl = false; return; }
        if (respondedHowl) return;
        if (myHowlAt < 0f && Random.value < 0.8f)
            myHowlAt = Group.HowlAt + Random.Range(1f, 3.5f);   // coro defasado
        if (myHowlAt > 0f && Time.time >= myHowlAt)
        {
            myHowlAt = -1f;
            OrderHowl();
        }
    }

    // -------------------------------------------------------- DANO & MORTE
    /// <summary>Dano vindo do dragão (ou de qualquer futuro sistema de combate).</summary>
    public void TakeHit(float damage, Vector3 from)
    {
        if (IsDead) return;
        hp -= damage;
        lastHitAt = Time.time;

        if (hp <= 0f) { Kill(from); return; }

        if (Role.anims.hits != null && Role.anims.hits.Length > 0)
            PerformOnce(PickHit(from));

        // ferido: revida (javali/urso/alce) ou dispara em pânico
        threatPos = from;
        if (Def.attackRange > 0f && !IsYoung &&
            Random.value < Def.aggression + Def.unpredictability * 0.5f)
            EnterAttack();
        else
        {
            Group.RaiseAlarm(from);
            EnterFlee(from);
        }
    }

    /// <summary>Reação de dano pela DIREÇÃO do golpe (Hit_F/B/M) quando os clipes existem.</summary>
    string PickHit(Vector3 from)
    {
        var hits = Role.anims.hits;
        Vector3 local = Quaternion.Euler(0f, -yaw, 0f) * (from - pos);
        string want = local.z > Mathf.Abs(local.x) ? "Hit_F"
                    : local.z < -Mathf.Abs(local.x) ? "Hit_B" : "Hit_M";
        foreach (var h in hits) if (h == want) return h;
        return Pick(hits);
    }

    /// <summary>Morte — vira uma Carcass de verdade (o mesmo sistema que o dragão come).</summary>
    public void Kill(Vector3 from)
    {
        if (IsDead) return;
        hp = 0f;
        behaviour = B.Dead;
        diedAt = Time.time;
        StopMoving();
        CancelPerformance();
        Group.OnMemberDied(this);

        if (Role.anims.deaths != null && Role.anims.deaths.Length > 0)
            PlayState(Pick(Role.anims.deaths), 0.2f);
    }

    void DeadTick(float dt)
    {
        float since = Time.time - diedAt;

        // o corpo cai, então nasce a carcaça (invisível — o corpo É o visual)
        if (Corpse == null && since > 1.6f && since < 5f)
            Corpse = Carcass.Spawn(pos, Def.nutrition * (IsYoung ? 0.4f : 1f), buildVisual: false);

        if (since > 6f && anim != null && anim.enabled) anim.enabled = false;

        // carcaça consumida (pelo dragão ou pelos lobos) → o corpo some devagar
        bool gone = since > 5f && (Corpse == null || Corpse.IsEmpty);
        if (gone || since > 300f)
        {
            pos.y -= 0.25f * dt;
            transform.position = pos;
            if (transform.position.y < groundY - 1.2f)
            {
                PrepareRelease();
                WildlifePool.Release(gameObject);
            }
        }
    }

    // ------------------------------------------------------------ SUSTOS
    /// <summary>Barulho no mundo (dragão pousou perto): todos olham/investigam.</summary>
    public void Startle(Vector3 sourcePos)
    {
        if (IsDead || behaviour == B.Flee || behaviour == B.Attack) return;
        threatPos = sourcePos;
        if (Random.value < Def.curiosity * curiosityMul) EnterInvestigate();
        else EnterAlert();
    }

    void EnterSettle(float seconds)
    {
        behaviour = B.Settle;
        behaviourUntil = Time.time + seconds;
        gaitOverride = null;
        StopMoving();
        CancelPerformanceSoft();
    }

    // ============================================================ MOVIMENTO
    void MoveTo(Vector3 target, float atSpeed)
    {
        moveTarget = target;
        moveSpeed = atSpeed;
        hasMoveTarget = true;
        EnsureLocomotion();
    }

    void StopMoving() => hasMoveTarget = false;

    float faceYaw;
    bool hasFaceTarget;

    /// <summary>Vira (suavemente, no MoveTick) para encarar um ponto.</summary>
    void FaceTowards(Vector3 p)
    {
        Vector3 d = p - pos;
        if (d.sqrMagnitude < 0.01f) return;
        faceYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        hasFaceTarget = true;
    }

    void MoveTick(float dt)
    {
        float desired = 0f;
        if (hasMoveTarget && !Performing)
        {
            Vector3 to = moveTarget - pos; to.y = 0f;
            float dist = to.magnitude;
            if (dist <= arriveRadius) hasMoveTarget = false;
            else
            {
                float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                wantYaw = SteerAround(wantYaw);
                // giro com aceleração/desaceleração — nada de virada "de robô"
                yaw = Mathf.SmoothDampAngle(yaw, wantYaw, ref yawVel, 0.22f, Def.turnSpeed, dt);
                desired = Mathf.Min(moveSpeed, Mathf.Max(0.7f, dist * 1.1f));
                // não sai correndo de costas: alinha primeiro
                float misalign = Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw));
                if (misalign > 70f) desired = Mathf.Min(desired, Def.walkSpeed);
            }
        }

        // parado mas com algo para encarar: gira no lugar (Turn_L/R entram no blend)
        if (!hasMoveTarget && hasFaceTarget)
        {
            yaw = Mathf.SmoothDampAngle(yaw, faceYaw, ref yawVel, 0.3f, Def.turnSpeed * 0.7f, dt);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, faceYaw)) < 3f) hasFaceTarget = false;
        }
        if (hasMoveTarget) hasFaceTarget = false;

        float accel = Def.runSpeed * (desired < speed ? 2.4f : 1.3f);
        speed = Mathf.MoveTowards(speed, desired, accel * dt);

        if (speed > 0.02f)
        {
            pos += Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * (speed * dt);
            if (Time.time >= nextGroundSample)
            {
                nextGroundSample = Time.time + (DistToPlayer() > 220f ? 0.12f : 0f);
                groundY = GroundHeight(pos.x, pos.z);
            }
            pos.y = groundY;
        }

        // alinhamento suave ao relevo (nada de animal "fincado" na encosta)
        if (Time.time >= nextNormalSample)
        {
            nextNormalSample = Time.time + 0.25f;
            groundNormal = SampleNormal();
        }
        Quaternion target = Quaternion.FromToRotation(Vector3.up, groundNormal) *
                            Quaternion.Euler(0f, yaw, 0f);
        transform.SetPositionAndRotation(pos,
            Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-8f * dt)));
    }

    /// <summary>Desvia de água e de encostas íngremes com sondas em leque.</summary>
    float SteerAround(float wantYaw)
    {
        var world = InfiniteTerrain.Instance;
        if (world == null) return wantYaw;

        float probe = 2.5f + speed * 0.7f;
        float bestYaw = wantYaw, bestScore = float.MinValue;
        for (int i = 0; i < 5; i++)
        {
            float off = (i == 0) ? 0f : (i == 1) ? 35f : (i == 2) ? -35f : (i == 3) ? 80f : -80f;
            float y = wantYaw + off;
            Vector3 p = pos + Quaternion.Euler(0f, y, 0f) * Vector3.forward * probe;
            float h = world.HeightAt(p.x, p.z);

            float score = -Mathf.Abs(off) * 0.02f;
            if (world.HasLakes)
            {
                float limit = behaviour == B.Drink ? world.WaterLevel - 0.15f
                                                   : world.WaterLevel + 0.3f;
                if (h < limit) score -= 100f;
            }
            float slope = Mathf.Abs(h - pos.y) / probe;
            if (slope > 0.85f) score -= 40f * slope;

            if (score > bestScore) { bestScore = score; bestYaw = y; }
        }
        return bestYaw;
    }

    Vector3 SampleNormal()
    {
        var world = InfiniteTerrain.Instance;
        if (world == null) return Vector3.up;
        const float e = 1.2f;
        float hx = world.HeightAt(pos.x + e, pos.z) - world.HeightAt(pos.x - e, pos.z);
        float hz = world.HeightAt(pos.x, pos.z + e) - world.HeightAt(pos.x, pos.z - e);
        Vector3 n = new(-hx / (2f * e), 1f, -hz / (2f * e));
        // só uma fração da inclinação real — visual, sem exagero
        return Vector3.Slerp(Vector3.up, n.normalized, 0.55f);
    }

    float GroundHeight(float x, float z)
    {
        var world = InfiniteTerrain.Instance;
        if (world != null) return world.HeightAt(x, z);
        return Physics.Raycast(new Vector3(x, pos.y + 60f, z), Vector3.down,
                               out RaycastHit hit, 200f)
            ? hit.point.y : pos.y;
    }

    /// <summary>Procura a margem de um lago próximo (para beber).</summary>
    bool FindWaterEdge(out Vector3 edge)
    {
        edge = default;
        var world = InfiniteTerrain.Instance;
        if (world == null || !world.HasLakes) return false;

        for (int a = 0; a < 10; a++)
        {
            float ang = a * 36f + Random.Range(-12f, 12f);
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            Vector3 prev = pos;
            for (float r = 6f; r <= 54f; r += 6f)
            {
                Vector3 p = pos + dir * r;
                if (world.HeightAt(p.x, p.z) < world.WaterLevel - 0.35f)
                {
                    edge = prev;   // último ponto seco antes da água
                    return true;
                }
                prev = p;
            }
        }
        return false;
    }

    // ============================================================= ANIMAÇÃO
    bool Performing => seqPhase != 0;
    string currentGait;    // evita re-CrossFade para o mesmo estado a cada tick
    string currentState;   // último estado tocado (evita reiniciar o mesmo loop)
    Posture posture = Posture.Stand;

    /// <summary>Postura aproximada de um estado, deduzida do nome do clipe.</summary>
    enum Posture { Stand, Low, Ground }

    static Posture PostureOf(string state)
    {
        if (string.IsNullOrEmpty(state)) return Posture.Stand;
        if (state.StartsWith("Lie") || state.StartsWith("Sit") || state.StartsWith("Sleep"))
            return Posture.Ground;
        if (state.StartsWith("Crouch") || state.StartsWith("Hide") || state.StartsWith("Digging"))
            return Posture.Low;
        return Posture.Stand;
    }

    /// <summary>
    /// Fade proporcional à mudança de postura: em pé↔em pé é rápido; sair de
    /// deitado para de pé precisa de tempo para o corpo inteiro se reorganizar.
    /// </summary>
    float FadeFor(string state)
    {
        int delta = Mathf.Abs((int)PostureOf(state) - (int)posture);
        return delta == 0 ? 0.25f : delta == 1 ? 0.45f : 0.65f;
    }

    void PlayState(string state, float fade = -1f, float timeOffset = 0f)
    {
        if (string.IsNullOrEmpty(state) || anim == null) return;
        if (fade < 0f) fade = FadeFor(state);
        anim.CrossFadeInFixedTime(state, fade, 0, timeOffset);
        posture = PostureOf(state);
        currentState = state;
        inLocomotion = state == "Locomotion";
        currentGait = inLocomotion ? state : null;
    }

    void EnsureLocomotion()
    {
        if (Performing) return;
        string want = gaitOverride ?? "Locomotion";
        if (currentGait == want) return;
        anim.CrossFadeInFixedTime(want, Mathf.Max(0.3f, FadeFor(want)), 0);
        posture = PostureOf(want);
        currentState = want;
        inLocomotion = gaitOverride == null;
        currentGait = want;
    }

    void Perform(AnimalDefinition.AnimSequence s, float loopSeconds)
    {
        if (s == null || !s.IsValid) return;
        seq = s;
        seqLoopUntil = Time.time + loopSeconds;
        if (!string.IsNullOrEmpty(s.start))
        {
            seqPhase = 1;
            // a troca para o loop começa ANTES do clipe acabar: o blend cobre a
            // emenda em vez de misturar com a pose congelada do último frame
            seqPhaseEnd = Time.time + Mathf.Max(0.15f, Len(s.start) - 0.25f);
            PlayState(s.start);
        }
        else
        {
            seqPhase = 2;
            string l = Pick(s.loops);
            seqPhaseEnd = Time.time + Mathf.Min(loopSeconds, Random.Range(4f, 8f));
            // offset aleatório no loop: o rebanho não mastiga em uníssono
            PlayState(l, -1f, Random.value * Len(l));
        }
    }

    void PerformOnce(string clip)
    {
        if (string.IsNullOrEmpty(clip)) return;
        seq = null;
        seqPhase = 3;
        seqPhaseEnd = Time.time + Mathf.Max(0.15f, Len(clip) - 0.2f);
        PlayState(clip, 0.15f);
    }

    void TickPerformer(float now)
    {
        if (seqPhase == 0 || now < seqPhaseEnd) return;

        switch (seqPhase)
        {
            case 1:   // intro acabou → loop
                seqPhase = 2;
                PlayState(Pick(seq.loops));
                seqPhaseEnd = now + Mathf.Min(seqLoopUntil - now, Random.Range(4f, 8f));
                if (seqPhaseEnd <= now) seqPhaseEnd = now + 3f;
                break;

            case 2:
                if (now >= seqLoopUntil)
                {
                    if (seq != null && !string.IsNullOrEmpty(seq.end))
                    {
                        seqPhase = 3;
                        seqPhaseEnd = now + Mathf.Max(0.15f, Len(seq.end) - 0.25f);
                        PlayState(seq.end);
                    }
                    else FinishPerformance();
                }
                else
                {
                    // troca entre variações de loop (Lie_loop_1 ↔ Lie_loop_2) —
                    // só se for OUTRA variação: re-CrossFade para o mesmo estado
                    // reinicia o clipe do zero (pop visível no meio do loop)
                    string next = Pick(seq.loops);
                    if (next != currentState) PlayState(next, 0.4f);
                    seqPhaseEnd = now + Mathf.Min(seqLoopUntil - now, Random.Range(4f, 8f));
                    if (seqPhaseEnd <= now) seqPhaseEnd = now + 3f;
                }
                break;

            case 3:
                FinishPerformance();
                break;
        }
    }

    void FinishPerformance()
    {
        seqPhase = 0;
        seq = null;
        EnsureLocomotion();
    }

    /// <summary>Interrompe sequência com saída limpa (sem "pular" de deitado pra correndo).</summary>
    void CancelPerformanceSoft()
    {
        if (seqPhase == 2 && seq != null && !string.IsNullOrEmpty(seq.end))
        {
            seqPhase = 3;
            seqPhaseEnd = Time.time + Mathf.Max(0.15f, Len(seq.end) * 0.7f - 0.2f);
            PlayState(seq.end, 0.15f);
            return;
        }
        CancelPerformance();
    }

    void CancelPerformance()
    {
        if (seqPhase == 0) return;   // nada tocando — não invalida a locomoção atual
        seqPhase = 0;
        seq = null;
        inLocomotion = false;   // força re-crossfade
        currentGait = null;
    }

    void UpdateAnimator(float dt)
    {
        if (anim == null || !anim.enabled) return;
        if (!Performing && gaitOverride == null && !inLocomotion && behaviour != B.Dead)
            EnsureLocomotion();

        anim.SetFloat(P_Speed, SpeedParam(speed), 0.14f, dt);

        float turnRate = Mathf.DeltaAngle(prevYaw, yaw) / Mathf.Max(dt, 0.0001f);
        anim.SetFloat(P_Turn, Mathf.Clamp(turnRate / Def.turnSpeed, -1f, 1f), 0.2f, dt);
        prevYaw = yaw;
    }

    float prevYaw;

    /// <summary>Velocidade real (m/s) → eixo do blend tree (0 idle · 1 walk · 2 trot · 3 run · 4 sprint).</summary>
    float SpeedParam(float s)
    {
        var d = Def;
        if (s <= 0.05f) return 0f;
        if (s <= d.walkSpeed) return Mathf.Lerp(0.4f, 1f, s / d.walkSpeed);
        if (s <= d.trotSpeed) return 1f + Mathf.InverseLerp(d.walkSpeed, d.trotSpeed, s);
        if (s <= d.runSpeed) return 2f + Mathf.InverseLerp(d.trotSpeed, d.runSpeed, s);
        bool sprint = !string.IsNullOrEmpty(Role.anims.runFast);
        return sprint
            ? 3f + Mathf.Clamp01((s - d.runSpeed) / Mathf.Max(0.1f, d.runSpeed * 0.35f))
            : 3f;
    }

    float Len(string clip) => clipLen.TryGetValue(clip, out float l) ? l : 1.5f;

    static string Pick(string[] options) => options[Random.Range(0, options.Length)];

    // ============================================================= CONSULTAS
    /// <summary>Animal vivo mais próximo dentro do alcance (padrão Carcass.FindNearest).</summary>
    public static AnimalAgent FindNearest(Vector3 position, float range)
    {
        AnimalAgent best = null;
        float bestSqr = range * range;
        foreach (var a in All)
        {
            if (a.IsDead) continue;
            float sqr = (a.pos - position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = a; }
        }
        return best;
    }

    /// <summary>Golpe do dragão: fere o animal vivo mais próximo do ponto de impacto.</summary>
    public static bool DamageNearest(Vector3 position, float range, float damage, Transform source)
    {
        var a = FindNearest(position, range);
        if (a == null) return false;
        a.TakeHit(damage, source != null ? source.position : position);
        return true;
    }
}
