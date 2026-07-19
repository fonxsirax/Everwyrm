using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// O "cérebro social" de um grupo de animais — classe pura, sem GameObject
/// (ticada pelo WildlifeSpawner). Um grupo NÃO é um bando de IAs soltas que
/// nasceram perto: ele compartilha âncora, estado, direção de fuga, alvo de
/// caça e decisões de migração. Os indivíduos (AnimalAgent) leem o estado do
/// grupo e adicionam sua camada pessoal (personalidade, variação, filhotes).
/// </summary>
public class AnimalGroup
{
    public enum GroupState { Calm, Fleeing, Migrating, Hunting, Feeding }

    public AnimalDefinition Def { get; }
    public readonly List<AnimalAgent> Members = new();
    public AnimalAgent Leader { get; private set; }

    public GroupState State { get; private set; } = GroupState.Calm;
    public Vector3 Anchor { get; private set; }       // centro "vivo" do grupo
    public Vector3 Home { get; private set; }         // território / área atual
    public Vector3 FleeFrom { get; private set; }     // origem da ameaça
    public AnimalAgent HuntTarget { get; private set; }
    public Vector3 FeedPos { get; private set; }
    public Carcass FeedCarcass { get; private set; }

    /// <summary>Momento do último uivo do líder — membros respondem em coro.</summary>
    public float HowlAt { get; private set; } = -999f;

    float stateUntil;
    float satiatedUntil;
    float nextMigrationRoll, nextHuntRoll, nextHowl;

    Vector3[] migrationPath;
    int migrationIndex;

    public AnimalGroup(AnimalDefinition def, Vector3 origin)
    {
        Def = def;
        Anchor = Home = origin;
        float now = Time.time;
        nextMigrationRoll = now + Random.Range(20f, 60f);
        nextHuntRoll = now + Random.Range(15f, 45f);
        nextHowl = now + Random.Range(60f, 240f);
        satiatedUntil = now + Random.Range(0f, 120f); // recém-chegados nem sempre caçam já
    }

    public bool IsEmpty => Members.Count == 0;

    public Vector3 Centroid()
    {
        if (Members.Count == 0) return Anchor;
        Vector3 sum = Vector3.zero;
        foreach (var m in Members) sum += m.transform.position;
        return sum / Members.Count;
    }

    // ------------------------------------------------------------- MEMBROS
    public void Register(AnimalAgent a)
    {
        Members.Add(a);
        if (Leader == null || a.Role.kind == AnimalDefinition.RoleKind.Lider)
            if (Leader == null || Leader.Role.kind != AnimalDefinition.RoleKind.Lider)
                Leader = a;
    }

    public void OnMemberDied(AnimalAgent a)
    {
        Members.Remove(a);
        if (Leader == a) PromoteLeader();
        if (HuntTarget == a) HuntTarget = null;
        // ver um companheiro cair assusta o grupo inteiro
        if (State != GroupState.Fleeing && a != null)
            RaiseAlarm(a.transform.position);
    }

    void PromoteLeader()
    {
        Leader = null;
        foreach (var m in Members)
            if (m.Role.kind != AnimalDefinition.RoleKind.Filhote) { Leader = m; break; }
        if (Leader == null && Members.Count > 0) Leader = Members[0];
    }

    /// <summary>
    /// Posição de conforto do membro `index` em relação à âncora — espiral de
    /// ângulo áureo: rebanhos viram "manchas" orgânicas, não círculos militares.
    /// </summary>
    public Vector3 SlotFor(int index)
    {
        if (index <= 0) return Vector3.zero;
        float r = Def.memberSpacing * (0.7f + 0.55f * Mathf.Sqrt(index));
        float a = index * 137.5f * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
    }

    // --------------------------------------------------------------- ALARME
    /// <summary>Um membro entrou em pânico: o grupo INTEIRO foge junto (coesão).</summary>
    public void RaiseAlarm(Vector3 threatPos)
    {
        FleeFrom = threatPos;
        if (State == GroupState.Feeding) FeedCarcass = null;
        State = GroupState.Fleeing;
        stateUntil = Time.time + Random.Range(7f, 11f);
    }

    /// <summary>Ameaça continua por perto? Estende a fuga.</summary>
    public void SustainAlarm(Vector3 threatPos)
    {
        FleeFrom = threatPos;
        stateUntil = Mathf.Max(stateUntil, Time.time + 4f);
    }

    // ----------------------------------------------------------------- TICK
    public void Tick(float dt)
    {
        if (IsEmpty) return;
        float now = Time.time;

        // âncora segue o líder (suavizada) — o grupo "flui" atrás dele
        Vector3 target = Leader != null ? Leader.transform.position : Centroid();
        Anchor = Vector3.Lerp(Anchor, target, 1f - Mathf.Exp(-1.6f * dt));

        switch (State)
        {
            case GroupState.Fleeing:
                if (now >= stateUntil) State = GroupState.Calm;
                break;

            case GroupState.Migrating:
                TickMigration();
                break;

            case GroupState.Hunting:
                TickHunt();
                break;

            case GroupState.Feeding:
                if (now >= stateUntil || FeedCarcass == null || FeedCarcass.IsEmpty)
                {
                    State = GroupState.Calm;
                    satiatedUntil = now + Random.Range(300f, 600f);
                }
                break;

            case GroupState.Calm:
                // migração espontânea (GDD: rebanhos cruzando o vale, alcateias patrulhando)
                if (now >= nextMigrationRoll)
                {
                    nextMigrationRoll = now + 60f;
                    if (!Def.territorial &&
                        Random.value < Def.migrationChancePerMinute)
                        StartMigration();
                }
                // predadores avaliam caçar
                if (Def.predator && now >= nextHuntRoll)
                {
                    nextHuntRoll = now + 30f;
                    if (now >= satiatedUntil &&
                        Random.value < Def.huntChancePerMinute * 0.5f)
                        TryStartHunt();
                }
                break;
        }

        // uivo — só espécies com o clipe (lobos). Também uivam migrando (patrulha).
        if (Leader != null && !string.IsNullOrEmpty(Leader.Role.anims.howl) &&
            now >= nextHowl &&
            (State == GroupState.Calm || State == GroupState.Migrating))
        {
            nextHowl = now + Random.Range(180f, 420f);
            HowlAt = now;
            Leader.OrderHowl();
        }
    }

    // -------------------------------------------------------------- MIGRAÇÃO
    void StartMigration()
    {
        float dist = Random.Range(Def.migrationDistance.x, Def.migrationDistance.y);
        Vector2 dir2 = Random.insideUnitCircle.normalized;
        Vector3 dir = new(dir2.x, 0f, dir2.y);

        // 3 pontos com jitter: a rota parece natural, não uma régua
        int points = 3;
        migrationPath = new Vector3[points];
        for (int i = 0; i < points; i++)
        {
            float t = (i + 1) / (float)points;
            Vector3 p = Anchor + dir * (dist * t);
            Vector2 j = Random.insideUnitCircle * dist * 0.12f;
            p.x += j.x; p.z += j.y;
            migrationPath[i] = p;
        }
        migrationIndex = 0;
        State = GroupState.Migrating;
    }

    void TickMigration()
    {
        if (Leader == null) { State = GroupState.Calm; return; }
        Vector3 wp = migrationPath[migrationIndex];
        Vector3 d = Leader.transform.position - wp; d.y = 0f;
        if (d.sqrMagnitude < 15f * 15f)
        {
            migrationIndex++;
            if (migrationIndex >= migrationPath.Length)
            {
                State = GroupState.Calm;
                Home = Leader.transform.position;   // nômades: novo lar ao chegar
            }
        }
    }

    /// <summary>Waypoint atual da migração (o líder caminha até ele).</summary>
    public Vector3 MigrationTarget =>
        migrationPath != null && migrationIndex < migrationPath.Length
            ? migrationPath[migrationIndex] : Anchor;

    // ----------------------------------------------------------------- CAÇA
    void TryStartHunt()
    {
        if (Def.preySpecies == null || Def.preySpecies.Length == 0) return;

        AnimalAgent best = null;
        float bestSqr = 150f * 150f;
        foreach (var a in AnimalAgent.All)
        {
            if (a.IsDead || a.Group == this) continue;
            bool isPrey = false;
            foreach (var s in Def.preySpecies)
                if (a.Def.speciesName == s) { isPrey = true; break; }
            if (!isPrey) continue;
            float sqr = (a.transform.position - Anchor).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = a; }
        }
        if (best == null) return;

        HuntTarget = best;
        State = GroupState.Hunting;
        stateUntil = Time.time + 45f;   // desiste de perseguições longas
    }

    void TickHunt()
    {
        if (HuntTarget == null || HuntTarget.IsDead || Time.time >= stateUntil)
        {
            // presa abatida? o grupo se alimenta nela
            if (HuntTarget != null && HuntTarget.IsDead && HuntTarget.Corpse != null)
                StartFeeding(HuntTarget.Corpse);
            else
                State = GroupState.Calm;
            HuntTarget = null;
            return;
        }
        // presa escapou longe demais
        if ((HuntTarget.transform.position - Anchor).sqrMagnitude > 250f * 250f)
        {
            HuntTarget = null;
            State = GroupState.Calm;
        }
    }

    public void StartFeeding(Carcass carcass)
    {
        if (carcass == null) { State = GroupState.Calm; return; }
        FeedCarcass = carcass;
        FeedPos = carcass.transform.position;
        State = GroupState.Feeding;
        stateUntil = Time.time + Random.Range(25f, 45f);
    }

    /// <summary>Predador informa que a presa caiu — todos convergem para comer.</summary>
    public void NotifyKill(AnimalAgent prey)
    {
        if (prey != null && prey.Corpse != null) StartFeeding(prey.Corpse);
        else State = GroupState.Calm;
        HuntTarget = null;
    }
}
