using System;
using UnityEngine;

/// <summary>
/// Crescimento e condição corporal (GDD: Crescimento Visível + Peso).
///
///  TAMANHO — cresce com o tempo, MUITO mais rápido bem alimentado.
///    Filhote → Adulto → Colossal (escala real do modelo).
///
///  CONDIÇÃO CORPORAL (0=esquelético · 0.5=saudável · 1=gordo) — muda DEVAGAR,
///    acompanhando a média de alimentação ao longo de minutos, não uma refeição.
///    Usa as blend shapes do modelo: "Belly Fat" para engordar e as "* Thin"
///    (barriga, peito, pescoço, pernas, cauda, asas...) para definhar.
///
///  PESO — derivado de tamanho³ x condição. Afeta:
///    aceleração terrestre, velocidade máxima, subida no voo, planeio e custo
///    de energia (dragões maiores gastam mais, mas planam melhor — GDD).
///
/// Publica eventos (observer) para o HUD e outros sistemas.
/// </summary>
[RequireComponent(typeof(DragonVitals))]
public class DragonGrowth : MonoBehaviour
{
    /// <summary>Filhote · Adulto · Colossal · Velhice. A ORDEM é contrato de
    /// serialização (enum grava como índice) — só acrescente no fim.</summary>
    public enum LifeStage { Hatchling, Adult, Colossal, Elder }

    [Header("Balanceamento")]
    [Tooltip("O balanceamento de crescimento, peso e expectativa de vida vive no asset " +
             "Assets/Scriptables/Resources/Balance/DragonGrowth.asset. Vazio = esse " +
             "padrão; arraste outro DragonGrowthProfile para variar por espécie.")]
    [SerializeField] DragonGrowthProfile growthProfile;

    /// <summary>O perfil resolvido, sob demanda: o campo acima quando preenchido,
    /// senão o asset padrão. PREGUIÇOSO de propósito — a janela de balanceamento
    /// (Tools > Everwyrm > Balanço do Dragão) lê estes números direto no PREFAB,
    /// fora do Play, onde nenhum Awake rodou.</summary>
    DragonGrowthProfile cfgCache;
    DragonGrowthProfile cfg => cfgCache != null ? cfgCache : (cfgCache = Balance.Resolve(growthProfile));

    /// <summary>Modo balanceamento (opcional): quando ligado, o CORPO consumido é um de
    /// referência fixo, e a Velhice/IV de Vigor podem ser neutralizados — sem tocar na
    /// simulação de verdade (idade/crescimento/condição seguem correndo nos campos
    /// privados; só o que os OUTROS sistemas leem é substituído). Ausente = normal.</summary>
    DragonBalanceOverride Ov => Balance.TryDefault<DragonBalanceOverride>();

    /// <summary>Crescimento EFETIVO (o que os multiplicadores/escala consomem): o de
    /// referência quando o corpo está congelado, senão o real.</summary>
    float EffGrowth { get { var ov = Ov; return ov != null && ov.BodyFrozen ? Mathf.Clamp01(ov.growth01) : growth01; } }
    /// <summary>Condição corporal EFETIVA (idem).</summary>
    float EffCondition { get { var ov = Ov; return ov != null && ov.BodyFrozen ? Mathf.Clamp01(ov.condition01) : condition; } }
    /// <summary>Vigor (IV) EFETIVO: 0 quando o modo neutraliza IVs.</summary>
    int EffIvVigor { get { var ov = Ov; return ov != null && ov.IvsNeutralized ? 0 : ivVigor; } }

    DragonVitals vitals;
    SkinnedMeshRenderer[] renderers;
    DragonShapeRig rig;  // índices do catálogo já resolvidos (DragonBlendShapes)
    DragonSkin skin;     // paleta/overrides autorais da skin atual
    DragonGenes genes;   // genoma visual do record possuído
    int ivVigor;         // vem do record (LoadFrom)
    bool naturalDeathFired;
    float growth01;      // 0 filhote → 1 colossal
    float condition;     // 0 magro → 1 gordo
    float lastEmittedGrowth = -1f, lastEmittedCondition = -1f, lastStarve = -1f;
    LifeStage stage;

    public LifeStage Stage => stage;
    // Growth/Condition/Scale/Weight EFETIVOS: no modo balanceamento reportam o dragão de
    // referência (corpo congelado). A simulação real continua nos campos privados.
    public float Growth01 => EffGrowth;
    public float Condition01 => EffCondition;
    public float AgeSeconds { get; private set; }
    public float Scale => ScaleAt(EffGrowth);
    public float WeightKg => WeightAt(EffGrowth, EffCondition);

    // ---- Limiares de fase, expostos para a ficha e a janela de balanceamento
    public float AdultAt => cfg.adultAt;
    public float ColossalAt => cfg.colossalAt;

    // ---- Consultas PURAS (só campos serializados): a janela de balanceamento
    //      chama isto direto no componente do PREFAB, sem entrar em Play.
    public float ScaleAt(float growth) => Mathf.Lerp(cfg.hatchlingScale, cfg.colossalScale, Mathf.Clamp01(growth));
    public float WeightAt(float growth, float cond) =>
        cfg.adultHealthyWeightKg * Mathf.Pow(ScaleAt(growth), 3f) * (0.65f + 0.7f * Mathf.Clamp01(cond));
    public float LifespanMulFor(int vigor) => 1f + vigor * cfg.vigorLifespanPerPoint;
    public float OldAgeSecondsFor(int vigor) => cfg.oldAgeMinutes * 60f * LifespanMulFor(vigor);
    public float LifespanSecondsFor(int vigor) => cfg.lifespanMinutes * 60f * LifespanMulFor(vigor);

    // ---- Velhice / expectativa de vida (Vigor alonga; a natureza fica com o atributo)
    public float GrowthSpeedMul => 1f + EffIvVigor * cfg.vigorGrowthPerPoint;
    public float LifespanMul => LifespanMulFor(EffIvVigor);
    public float OldAgeSeconds => OldAgeSecondsFor(EffIvVigor);
    public float LifespanSeconds => LifespanSecondsFor(EffIvVigor);
    /// <summary>0 fora da velhice → 1 no fim da vida (ramp linear oldAge→lifespan).
    /// No modo balanceamento com a Velhice desligada, fica sempre 0 — nada se corrói.</summary>
    public float ElderProgress
    {
        get
        {
            var ov = Ov;
            if (ov != null && ov.ElderDisabled) return 0f;
            return Mathf.Clamp01(Mathf.InverseLerp(OldAgeSeconds, LifespanSeconds, AgeSeconds));
        }
    }
    /// <summary>Vitalidade da velhice: 1 no auge, cai até (1-elderPenalty) no fim.</summary>
    public float ElderMul => ElderMulFor(ElderProgress);
    /// <summary>Versão PURA (janela de balanceamento).</summary>
    public float ElderMulFor(float elderProgress01) =>
        Mathf.Lerp(1f, 1f - cfg.elderPenalty, Mathf.Clamp01(elderProgress01));
    public bool IsElder => stage == LifeStage.Elder;

    // ---- Multiplicadores consumidos pelo DragonController
    /// <summary>Escala geral de velocidades (dragão maior anda/voa mais rápido em absoluto).</summary>
    public float SpeedScale => Scale;
    /// <summary>Segundos andando até atingir corrida plena. Gordo/grande demora mais.</summary>
    public float AccelTime => Mathf.Lerp(2.0f, 5.5f, EffCondition) * Mathf.Lerp(0.85f, 1.25f, EffGrowth);
    /// <summary>Velocidade máxima de corrida. Gordo corre menos; ancião perde fôlego.</summary>
    public float RunSpeedMul => RunSpeedMulAt(EffCondition) * ElderMul;
    /// <summary>Versão PURA, só pela condição corporal (janela de balanceamento).</summary>
    public float RunSpeedMulAt(float cond) => Mathf.Lerp(1.08f, 0.82f, Mathf.Clamp01(cond));
    /// <summary>Subida no voo. Gordo sobe muito pior.</summary>
    public float ClimbMul => Mathf.Lerp(1.15f, 0.6f, EffCondition);
    /// <summary>SUSTENTAÇÃO DA BATIDA DE ASA — o quanto o Space levanta o dragão.
    /// Quem manda é o PESO ATUAL (condição corporal), não a fase da vida: um
    /// colossal magro sobe quase os mesmos metros que um filhote magro, e é
    /// engordar que rouba altitude. `liftSizeInfluence` regula o resíduo de
    /// tamanho que ainda pesa. A Velhice tira vigor da batida como tira do resto.</summary>
    public float FlapLiftMul => FlapLiftMulAt(EffGrowth, EffCondition) * ElderMul;
    /// <summary>Versão PURA (sem velhice) para a janela de balanceamento.</summary>
    public float FlapLiftMulAt(float growth, float cond) =>
        Mathf.Lerp(cfg.liftWhenLean, cfg.liftWhenFat, Mathf.Clamp01(cond)) *
        Mathf.Lerp(1f, Mathf.Lerp(1.12f, 0.82f, Mathf.Clamp01(growth)), cfg.liftSizeInfluence);
    /// <summary>Afundamento no planeio. Gordo afunda mais; grande plana melhor (GDD).</summary>
    public float SinkMul => Mathf.Lerp(0.85f, 1.6f, EffCondition) * Mathf.Lerp(1.2f, 0.75f, EffGrowth);
    /// <summary>Custo de energia. Gordo e grande gastam mais.</summary>
    public float EnergyCostMul => Mathf.Lerp(0.9f, 1.4f, EffCondition) * Mathf.Lerp(0.85f, 1.25f, EffGrowth);

    // ---- Agilidade por idade (rework hack and slash): o peso modula a
    //      EXPLOSÃO e o giro, nunca cria espera. Filhote = ágil e furtivo;
    //      colossal = ariete que conserva velocidade.
    /// <summary>Velocidade de giro (chão e voo). Filhote vira no lugar; ancião enrijece.</summary>
    public float TurnAgilityMul => Mathf.Lerp(1.25f, 0.85f, EffGrowth) * ElderMul;
    /// <summary>Aceleração terrestre: filhote arranca; gordo/ancião empurram mais devagar.</summary>
    public float AccelAgilityMul => Mathf.Lerp(1.3f, 0.9f, EffGrowth) * Mathf.Lerp(1.15f, 0.75f, EffCondition) * ElderMul;
    /// <summary>Cooldown do dash (filhote ~0.45 s · adulto 0.6 · colossal 0.75).</summary>
    public float DashCooldownMul => Mathf.Lerp(0.75f, 1.25f, EffGrowth);
    /// <summary>Força do Wing Boost — a batida do colossal é um aríete (o ancião perde vigor).</summary>
    public float BoostMul => Mathf.Lerp(0.7f, 1.15f, EffGrowth) * ElderMul;

    // ---- Observer
    public event Action<DragonGrowth> OnGrowthChanged;   // tamanho/condição/peso
    public event Action<LifeStage> OnStageChanged;
    /// <summary>Morte por VELHICE — dispara uma vez ao cruzar a expectativa de vida.
    /// A possessão trata (o dragão fica com 0 de vida e a Base possui o próximo).</summary>
    public event Action OnNaturalDeath;

    void Awake()
    {
        vitals = GetComponent<DragonVitals>();
        RebuildRig();
        growth01 = cfg.startGrowth;
        condition = cfg.startCondition;
        stage = StageFor(growth01);
        ApplyScale();
        ApplyBlendShapes();
    }

    void Update()
    {
        if (vitals.IsDead) return;
        float dt = Time.deltaTime;
        AgeSeconds += dt;

        // ---- morte natural: cruzou a expectativa de vida (Vigor a alonga). O dragão
        //      não some — a possessão o deixa com 0 de vida e a Base assume o próximo.
        //      No modo balanceamento com a Velhice desligada, ninguém morre de velho.
        var ovDeath = Ov;
        bool elderOff = ovDeath != null && ovDeath.ElderDisabled;
        if (!naturalDeathFired && !elderOff && AgeSeconds >= LifespanSeconds)
        {
            naturalDeathFired = true;
            OnNaturalDeath?.Invoke();
        }

        // ---- crescimento: só floresce comendo bem (GDD); Vigor acelera
        float fedFactor = vitals.Hunger01 > 0.6f ? 1f
                        : vitals.Hunger01 > 0.25f ? 0.35f
                        : 0.05f;
        // corpo saudável cresce melhor que esquelético ou obeso
        float healthFactor = 1f - Mathf.Abs(condition - 0.5f) * 0.8f;
        growth01 = Mathf.Min(1f, growth01 +
            fedFactor * healthFactor * GrowthSpeedMul * dt / (cfg.fullGrowthMinutes * 60f));

        // ---- condição corporal em KG por minuto (bem sutil):
        //      satisfeito sem comer = engorda devagar · fome < 50% = emagrece
        //      fome < 20% = emagrece mais rápido (pior caso ~1 kg a cada 20 s)
        float kgPerMin = vitals.Hunger01 > 0.65f ? cfg.gainKgPerMinute
                       : vitals.Hunger01 > 0.5f ? 0f
                       : vitals.Hunger01 > 0.2f ? -cfg.lossKgPerMinute
                       : -cfg.starvingLossKgPerMinute;
        condition = Mathf.Clamp01(condition + kgPerMin / KgPerFullCondition / 60f * dt);

        ApplyScale();

        // ---- emitir eventos apenas em mudança perceptível. O definhamento por fome
        //      (starving) muda com a fome, não só com a condição — reaplica os shapes
        //      quando ele muda, senão o corpo não acompanharia a fome caindo.
        float starve = Starve01;
        if (Mathf.Abs(growth01 - lastEmittedGrowth) > 0.002f ||
            Mathf.Abs(condition - lastEmittedCondition) > 0.004f ||
            Mathf.Abs(starve - lastStarve) > 0.02f)
        {
            lastEmittedGrowth = growth01;
            lastEmittedCondition = condition;
            lastStarve = starve;
            ApplyBlendShapes();
            OnGrowthChanged?.Invoke(this);
        }

        var newStage = ComputeStage();
        if (newStage != stage)
        {
            stage = newStage;
            OnStageChanged?.Invoke(stage);
        }
    }

    /// <summary>Fase de vida: idade define a Velhice (sobrepõe o tamanho); abaixo
    /// dela, o tamanho define Filhote/Adulto/Colossal.</summary>
    LifeStage ComputeStage() =>
        AgeSeconds >= OldAgeSeconds ? LifeStage.Elder : StageFor(growth01);

    /// <summary>0 = saciado · 1 = definhando de fome. Ramp abaixo de starveBelowHunger.</summary>
    float Starve01 => vitals == null ? 0f
        : Mathf.Clamp01((cfg.starveBelowHunger - vitals.Hunger01) / Mathf.Max(0.01f, cfg.starveBelowHunger));

    /// <summary>Quantos kg equivalem à faixa toda de condição (magro→gordo) no tamanho atual.</summary>
    float KgPerFullCondition => Mathf.Max(1f, cfg.adultHealthyWeightKg * Mathf.Pow(Scale, 3f) * 0.7f);

    /// <summary>Chamado ao comer: nutrição acelera crescimento e engorda um pouquinho.</summary>
    public void NotifyAte(float nutrition)
    {
        growth01 = Mathf.Min(1f, growth01 + nutrition * cfg.mealGrowthBonus);
        condition = Mathf.Clamp01(condition + cfg.mealWeightNudgeKg / KgPerFullCondition);
    }

    LifeStage StageFor(float g) =>
        g >= cfg.colossalAt ? LifeStage.Colossal :
        g >= cfg.adultAt ? LifeStage.Adult : LifeStage.Hatchling;

    // ============================================================ POSSESSÃO
    /// <summary>Resolve o catálogo de blend shapes contra os renderers atuais. Refeito
    /// a cada troca de skin porque a malha pode ter mudado.</summary>
    void RebuildRig()
    {
        renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        rig = DragonShapeRig.Build(renderers);
    }

    /// <summary>Define a skin ativa e reconstrói o rig. Chamado pela possessão ANTES
    /// do LoadFrom (que traz o genoma).</summary>
    public void SetSkin(DragonSkin s)
    {
        skin = s;
        RebuildRig();
        ApplyIdentityShapes();
        ForceReapply();     // condição por cima
    }

    /// <summary>As chaves de IDENTIDADE: genoma primeiro, override autoral da skin
    /// depois. Parte sempre do zero (o default do modelo é 0 em tudo), então trocar de
    /// dragão nunca deixa resíduo do anterior.
    ///
    /// Cabeça e chifres são EXCLUSIVOS — o genoma acende uma variante e as irmãs ficam
    /// em 0, senão duas morphs da mesma parte se somariam e deformariam o crânio.
    ///
    /// Os grupos de identidade e de condição são disjuntos por construção (ver
    /// DragonBlendShapes), então a condição corporal pode reescrever livremente por
    /// cima sem nunca apagar um gene.</summary>
    void ApplyIdentityShapes()
    {
        if (rig == null) return;

        DragonShapeRig.SetAll(rig.Spikes, 0f);
        DragonShapeRig.SetAll(rig.Heads, 0f);
        DragonShapeRig.SetAll(rig.Horns, 0f);
        rig.WingFingers.Set(0f);

        if (genes != null)
        {
            // ordem igual à de DragonBlendShapes.Spikes
            if (rig.Spikes.Length >= 4)
            {
                rig.Spikes[0].Set(genes.armSpikes);
                rig.Spikes[1].Set(genes.legsSpikes);
                rig.Spikes[2].Set(genes.neckSpikes);
                rig.Spikes[3].Set(genes.bodySpikes);
            }
            rig.WingFingers.Set(genes.wingFingers);

            int head = genes.headVariant - 1;   // 0 = cabeça base, sem morph
            if (head >= 0 && head < rig.Heads.Length) rig.Heads[head].Set(genes.headWeight);

            int horn = genes.hornVariant - 1;   // 0 = sem chifres
            if (horn >= 0 && horn < rig.Horns.Length) rig.Horns[horn].Set(genes.hornWeight);
        }

        ApplyAppearanceOverrides();
    }

    /// <summary>Override autoral da skin, por nome EXATO do mesh — a última palavra
    /// sobre o genoma quando a skin quer travar um visual.</summary>
    void ApplyAppearanceOverrides()
    {
        if (skin == null || skin.appearanceKeys == null || skin.appearanceKeys.Count == 0) return;
        foreach (var key in skin.appearanceKeys)
        {
            if (string.IsNullOrEmpty(key.shapeName)) continue;
            foreach (var r in renderers)
            {
                var mesh = r != null ? r.sharedMesh : null;
                if (mesh == null) continue;
                int i = mesh.GetBlendShapeIndex(key.shapeName);
                if (i >= 0) { r.SetBlendShapeWeight(i, key.weight); break; }
            }
        }
    }

    /// <summary>Carrega genoma, idade/crescimento/condição e o Vigor (IV) do record.</summary>
    public void LoadFrom(DragonRecord record)
    {
        ivVigor = record.ivVigor;
        genes = record.genes;
        var s = record.state;
        AgeSeconds = s.ageSeconds;
        growth01 = Mathf.Clamp01(s.growth01);
        condition = Mathf.Clamp01(s.condition01);
        naturalDeathFired = s.isDeadOfOldAge;
        stage = ComputeStage();
        ApplyIdentityShapes();   // genoma deste record
        ForceReapply();          // condição por cima
        OnGrowthChanged?.Invoke(this);
    }

    public void WriteTo(DragonState s)
    {
        s.ageSeconds = AgeSeconds;
        s.growth01 = growth01;
        s.condition01 = condition;
    }

    /// <summary>Reaplica escala e shapes agora (após load/troca de skin), fora do
    /// gatilho de "mudança perceptível" do Update.</summary>
    void ForceReapply()
    {
        lastEmittedGrowth = growth01;
        lastEmittedCondition = condition;
        lastStarve = Starve01;
        ApplyScale();
        ApplyBlendShapes();
    }

    void ApplyScale() => transform.localScale = Vector3.one * Scale;

    /// <summary>Engorda o grupo Thick acima da condição saudável; emagrece o grupo Thin
    /// abaixo dele. A fome extrema (starving) força o definhamento por cima da condição
    /// lenta — o mesh do Unka não tem chave dedicada de inanição, então ela reforça o
    /// grupo Thin.
    ///
    /// Os dois grupos são mutuamente exclusivos por condição (um sempre está em 0), mas
    /// AMBOS são escritos todo passe: só assim o corpo desincha ao emagrecer.</summary>
    void ApplyBlendShapes()
    {
        if (rig == null) return;

        float fatW = Mathf.Clamp01((condition - 0.5f) * 2f) * 100f;
        float thinW = Mathf.Clamp01((0.5f - condition) * 2f) * 100f;
        thinW = Mathf.Max(thinW, Starve01 * 100f);

        DragonShapeRig.SetAll(rig.Thick, fatW);
        DragonShapeRig.SetAll(rig.Thin, thinW);
    }
}
