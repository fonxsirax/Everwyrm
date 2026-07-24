using System;
using UnityEngine;

/// <summary>
/// Os SEIS atributos evolutivos do dragão (rework jul/2026, split de 3 → 6):
///
///   FORÇA     (Might)    — dano físico, força do Wing Boost.
///   CHAMA     (Ardor)    — tamanho/dano da chama e da queimadura.
///   AGILIDADE (Agility)  — velocidade máxima, aceleração e giro (chão e voo).
///   VIGOR     (Vigor)    — vida, aguenta a fome, e o talento de sobrevivência
///                          (o IV de Vigor também alonga a vida em DragonGrowth).
///   FÔLEGO    (Wind)     — energia total, EFICIÊNCIA de energia (custo), subida
///                          de decolagem e TETO DE VOO.
///   INSTINTO  (Instinct) — faro/dominância, janelas de i-frame (dash/boost/esquiva)
///                          e leitura das correntes de ar (updrafts rendem mais).
///
/// NÃO EXISTE "level up com pontos". Os atributos crescem SOZINHOS com a
/// MATURIDADE: 0 no ovo → `maxAttribute` (10) no auge. Cada atributo tem sua
/// própria CURVA (Agilidade cedo, Força só com a massa, etc.). A Velhice corrói
/// o conquistado (`elderDecline`).
///
/// MATURIDADE = mistura de CRESCIMENTO (tamanho — depende de comer bem) com
/// IDADE pura. Quem se alimenta bem amadurece antes; quem só sobrevive amadurece
/// de todo jeito (por isso os atributos sobem mesmo depois que o corpo para de
/// crescer, no Colossal).
///
/// IDENTIDADE (linhagem) vale por cima da curva:
///  · NATUREZA favorece um atributo e prejudica outro (±natureModifier);
///  · IV (talento bruto 0..maxIV) por atributo, soma até `ivInfluence` × maxAttribute.
///
/// BALANCEAMENTO: todos os números vivem no DragonAttributeProfile
/// (Assets/Scriptables/Resources/Balance/DragonAttributes.asset) — este script só faz
/// a conta. Simule fora do Play em <b>Tools > Everwyrm > Balanço do Dragão</b>, ou abra
/// a ficha em jogo com Tab.
///
/// A ANATOMIA de qual atributo dirige qual sistema está documentada no GDD
/// (seção "Atributos do Dragão").
///
/// Observer: OnChanged (atributos mexeram) / OnTierUp (degrau de maturidade —
/// é o que desbloqueia ataques no DragonAbilities).
/// </summary>
[RequireComponent(typeof(DragonGrowth))]
public class DragonAttributes : MonoBehaviour
{
    /// <summary>Os seis atributos. A ORDEM é contrato de serialização — os índices
    /// 0/1/2 herdaram o lugar dos antigos Velocidade/Poder/Resistência (o sistema de
    /// 3), então NÃO reordene: só acrescente no fim.</summary>
    public enum Attribute { Agility, Might, Vigor, Ardor, Wind, Instinct }
    public const int Count = 6;

    [Header("Balanceamento")]
    [Tooltip("As curvas de maturação e os ganhos por ponto vivem no asset " +
             "Assets/Scriptables/Resources/Balance/DragonAttributes.asset. Vazio = " +
             "esse padrão; arraste outro DragonAttributeProfile para variar por espécie.")]
    [SerializeField] DragonAttributeProfile attributeProfile;

    /// <summary>O perfil resolvido, sob demanda: o campo acima quando preenchido,
    /// senão o asset padrão. PREGUIÇOSO de propósito — a janela de balanceamento
    /// (Tools > Everwyrm > Balanço do Dragão) lê estes números direto no PREFAB,
    /// fora do Play, onde nenhum Awake rodou.</summary>
    DragonAttributeProfile cfgCache;
    DragonAttributeProfile cfg => cfgCache != null ? cfgCache : (cfgCache = Balance.Resolve(attributeProfile));

    /// <summary>Modo balanceamento (opcional): quando ligado, ignora IVs/natureza/idade e
    /// usa um dragão de referência fixo. Resolvido silenciosamente — ausente = normal.
    /// Lido a cada Refresh para responder a ajustes ao vivo no asset.</summary>
    DragonBalanceOverride Ov => Balance.TryDefault<DragonBalanceOverride>();

    /// <summary>IVs zerados reutilizáveis para o modo balanceamento (nunca mutar).</summary>
    static readonly int[] ZeroIvs = new int[Count];

    DragonGrowth growth;

    // ---- Identidade (vem do DragonRecord via LoadFrom)
    DragonNature nature = DragonNature.Balanced;
    readonly int[] ivs = new int[Count];   // talento bruto por atributo (0..maxIV)

    AttributeSnapshot snap;
    float peakMaturity;          // maturidade máxima já atingida (Tier nunca regride)
    float lastEmitted = -1f;

    /// <summary>0 no ovo → 1 no auge · volta a cair na Velhice.</summary>
    public float Maturity01 => snap.maturity01;
    /// <summary>Degrau de maturidade 1..maxTier — só serve para desbloquear ataques.</summary>
    public int Tier { get; private set; } = 1;
    /// <summary>Maturidade máxima já atingida — o Tier nunca regride, nem na Velhice.</summary>
    public float PeakMaturity01 => peakMaturity;
    /// <summary>Quantos degraus de maturidade existem no total (o que
    /// DragonAttackData.unlockLevel compara). HUD/ficha usam para desenhar o progresso.</summary>
    public int MaxTier => cfg.maxTier;
    /// <summary>0..1 dentro do degrau ATUAL — quanto falta para o próximo desbloqueio.
    /// 1 quando já está no degrau máximo (nada mais a desbloquear por maturidade).</summary>
    public float TierProgress01
    {
        get
        {
            if (cfg.maxTier <= 1 || Tier >= cfg.maxTier) return 1f;
            float span = 1f / (cfg.maxTier - 1);
            float from = (Tier - 1) * span, to = Tier * span;
            return Mathf.Clamp01((peakMaturity - from) / Mathf.Max(0.0001f, to - from));
        }
    }
    /// <summary>Foto completa dos atributos e derivados neste instante.</summary>
    public AttributeSnapshot Current => snap;

    // ---- Valores brutos por atributo (0..maxAttribute)
    public float Get(Attribute a) => snap.Get(a);
    public float Might => snap.might;
    public float Ardor => snap.ardor;
    public float Agility => snap.agility;
    public float Vigor => snap.vigor;
    public float Wind => snap.wind;
    public float Instinct => snap.instinct;
    public float MaxAttribute => cfg.maxAttribute;

    // ---- Identidade exposta (HUD/ficha/balanço)
    public DragonNature Nature => nature;
    public int MaxIV => cfg.maxIV;
    public int Iv(Attribute a) => ivs[(int)a];

    // ---- Multiplicadores consumidos pelos outros sistemas. Os NOMES herdados
    //      (SpeedMul/DamageMul/...) apontam para os novos atributos, então os
    //      consumidores antigos não mudaram — só a fonte por trás mudou.
    public float SpeedMul => snap.speedMul;            // Agilidade
    public float AccelMul => snap.accelMul;            // Agilidade + Vigor
    public float TurnMul => snap.turnMul;              // Agilidade (NOVO)
    public float DamageMul => snap.damageMul;          // Força — DragonAbilities
    public float BoostMul => snap.boostMul;            // Força (NOVO) — Wing Boost
    public float FlameSizeMul => snap.flameMul;        // Chama — DragonAbilities (isFire)
    public float DominanceRadius => snap.dominanceRadius;  // Instinto — faro / minimapa
    public float MaxHealthMul => snap.healthMul;       // Vigor
    public float MaxEnergyMul => snap.energyMul;       // Fôlego
    /// <summary>Fator do CUSTO de energia (Fôlego): &lt;1 gasta menos. Multiplica os
    /// custos em DragonController/Flight/Abilities.</summary>
    public float EnergyCostMul => snap.energyCostMul;  // Fôlego (NOVO)
    public float HungerDecayMul => snap.hungerDecayMul; // Vigor
    /// <summary>Fôlego estica a fase de subida contínua da decolagem (DragonFlight).</summary>
    public float TakeoffClimbMul => snap.takeoffClimbMul; // Fôlego
    /// <summary>Teto de voo (altura Y do mundo, em m). Acima dele o ar rarefeito
    /// não sustenta (DragonFlight).</summary>
    public float MaxAltitude => snap.maxAltitude;      // Fôlego
    /// <summary>Estica as janelas de i-frame (dash/boost/esquiva) — Instinto (NOVO).</summary>
    public float IFrameMul => snap.iframeMul;          // Instinto (NOVO)
    /// <summary>Quanto das correntes de ar o dragão aproveita — Instinto (NOVO).</summary>
    public float WindReadMul => snap.windReadMul;      // Instinto (NOVO)

    // ---- Observer
    public event Action<DragonAttributes> OnChanged;
    /// <summary>Subiu um degrau de maturidade — o DragonAbilities destrava ataques.</summary>
    public event Action<int> OnTierUp;

    void Awake()
    {
        growth = GetComponent<DragonGrowth>();
        Rebuild();
    }

    // A maturidade depende da IDADE, que corre todo frame — o observer do
    // crescimento pararia de avisar assim que o corpo satura no Colossal.
    void Update() => Refresh(announce: true);

    /// <summary>Recalcula do zero SEM anunciar degrau: um dragão recém-possuído já
    /// nasce maduro, e disparar OnTierUp aqui "desbloquearia" na cara do jogador
    /// ataques que este dragão sempre teve.</summary>
    void Rebuild()
    {
        peakMaturity = 0f;
        Tier = 1;
        Refresh(announce: false);
        OnChanged?.Invoke(this);
    }

    void Refresh(bool announce)
    {
        if (growth == null) return;

        // Modo balanceamento: um dragão de REFERÊNCIA no lugar da linhagem deste dragão.
        // Cada eixo respeita sua própria flag, então dá para religar um por um.
        var ov = Ov;
        DragonNature n = ov != null && ov.NatureNeutralized ? DragonNature.Balanced : nature;
        int[] iv = ov != null && ov.IvsNeutralized ? ZeroIvs : ivs;
        float m = ov != null && ov.MaturityFrozen
            ? Mathf.Clamp01(ov.maturity01)
            : MaturityFor(growth.Growth01, AgeProgress01, growth.ElderProgress);
        snap = SnapshotAt(m, n, iv);

        // o Tier é MONOTÔNICO: envelhecer enfraquece o corpo, mas ninguém
        // desaprende um golpe já destravado
        if (m > peakMaturity) peakMaturity = m;
        int tier = Mathf.Clamp(1 + Mathf.FloorToInt(peakMaturity * (cfg.maxTier - 1) + 0.0001f), 1, cfg.maxTier);
        bool tierUp = tier > Tier;
        Tier = tier;

        if (!announce) { lastEmitted = m; return; }

        if (tierUp || Mathf.Abs(m - lastEmitted) > 0.002f)
        {
            lastEmitted = m;
            OnChanged?.Invoke(this);
        }
        if (tierUp) OnTierUp?.Invoke(Tier);
    }

    /// <summary>0 = recém-chocado · 1 = na porta da Velhice.</summary>
    float AgeProgress01 =>
        Mathf.Clamp01(growth.AgeSeconds / Mathf.Max(1f, growth.OldAgeSeconds));

    // ======================================================== CÁLCULO PURO
    /// <summary>Maturidade a partir dos três sinais do corpo. PURA — a janela de
    /// balanceamento chama isto com valores hipotéticos.</summary>
    public float MaturityFor(float growth01, float ageProgress01, float elderProgress01)
    {
        float raw = Mathf.Clamp01(cfg.growthWeight * Mathf.Clamp01(growth01) +
                                  (1f - cfg.growthWeight) * Mathf.Clamp01(ageProgress01));
        float m = Mathf.Clamp01(cfg.maturityCurve.Evaluate(raw));
        return m * (1f - cfg.elderDecline * Mathf.Clamp01(elderProgress01));
    }

    /// <summary>Os seis atributos e TODOS os derivados numa maturidade qualquer, com
    /// uma identidade qualquer. PURA: só lê campos serializados, então funciona
    /// direto no componente do PREFAB (é assim que a janela de balanceamento
    /// simula sem entrar em Play). `iv` precisa ter Count entradas (índice = Attribute).</summary>
    public AttributeSnapshot SnapshotAt(float maturity01, DragonNature n, int[] iv)
    {
        float m = Mathf.Clamp01(maturity01);
        var s = new AttributeSnapshot
        {
            maturity01 = m,
            agility = Value(cfg.agilityCurve, m, iv, Attribute.Agility, n),
            might = Value(cfg.mightCurve, m, iv, Attribute.Might, n),
            vigor = Value(cfg.vigorCurve, m, iv, Attribute.Vigor, n),
            ardor = Value(cfg.ardorCurve, m, iv, Attribute.Ardor, n),
            wind = Value(cfg.windCurve, m, iv, Attribute.Wind, n),
            instinct = Value(cfg.instinctCurve, m, iv, Attribute.Instinct, n),
        };

        s.speedMul = 1f + s.agility * cfg.speedGain;
        s.accelMul = 1f + s.agility * cfg.accelAgilityGain + s.vigor * cfg.accelVigorGain;
        s.turnMul = 1f + s.agility * cfg.turnGain;
        s.damageMul = 1f + s.might * cfg.damageGain;
        s.boostMul = 1f + s.might * cfg.boostGain;
        s.flameMul = 1f + s.ardor * cfg.flameGain;
        s.dominanceRadius = cfg.dominanceBase + s.instinct * cfg.dominanceGain;
        s.healthMul = 1f + s.vigor * cfg.healthGain;
        s.energyMul = 1f + s.wind * cfg.energyGain;
        s.energyCostMul = Mathf.Max(0.5f, 1f - s.wind * cfg.energyEfficiencyGain);
        s.hungerDecayMul = Mathf.Max(0.4f, 1f - s.vigor * cfg.hungerResistGain);
        s.takeoffClimbMul = 1f + s.wind * cfg.takeoffClimbGain;
        s.maxAltitude = cfg.ceilingBase + Mathf.Min(s.wind, cfg.ceilingCap) * cfg.ceilingGain;
        s.iframeMul = 1f + s.instinct * cfg.iframeGain;
        s.windReadMul = 1f + s.instinct * cfg.windReadGain;
        return s;
    }

    /// <summary>Um atributo: curva de maturação × escala, mais o talento (IV),
    /// tudo modulado pela natureza.</summary>
    float Value(AnimationCurve curve, float maturity01, int[] iv, Attribute a, DragonNature n)
    {
        float baseValue = Mathf.Max(0f, curve.Evaluate(maturity01)) * cfg.maxAttribute;
        int talentIv = iv != null && (int)a < iv.Length ? iv[(int)a] : 0;
        float talent = cfg.maxIV > 0 ? (float)talentIv / cfg.maxIV * cfg.maxAttribute * cfg.ivInfluence : 0f;
        return (baseValue + talent) * DragonNatureTable.Multiplier(n, a, cfg.natureModifier);
    }


    // ============================================================ POSSESSÃO
    /// <summary>Carrega a IDENTIDADE (natureza/IVs) de um DragonRecord. Não há
    /// progresso de atributo para carregar: ele é derivado do corpo (crescimento e
    /// idade), que o DragonGrowth já guarda no state.</summary>
    public void LoadFrom(DragonRecord record)
    {
        nature = record.nature;
        ivs[(int)Attribute.Agility] = record.ivAgility;
        ivs[(int)Attribute.Might] = record.ivMight;
        ivs[(int)Attribute.Vigor] = record.ivVigor;
        ivs[(int)Attribute.Ardor] = record.ivArdor;
        ivs[(int)Attribute.Wind] = record.ivWind;
        ivs[(int)Attribute.Instinct] = record.ivInstinct;
        Rebuild();
    }
}

/// <summary>
/// Foto dos seis atributos e de tudo que eles multiplicam num instante (ou numa
/// maturidade hipotética). É o pacote que a ficha, o HUD e a janela de balanceamento
/// leem — nenhum sistema precisa refazer as contas.
/// </summary>
public struct AttributeSnapshot
{
    public float maturity01;
    public float might, ardor, agility, vigor, wind, instinct;
    public float speedMul, accelMul, turnMul, damageMul, boostMul, flameMul, dominanceRadius,
                 healthMul, energyMul, energyCostMul, hungerDecayMul, takeoffClimbMul,
                 maxAltitude, iframeMul, windReadMul;

    public float Get(DragonAttributes.Attribute a) => a switch
    {
        DragonAttributes.Attribute.Might => might,
        DragonAttributes.Attribute.Ardor => ardor,
        DragonAttributes.Attribute.Agility => agility,
        DragonAttributes.Attribute.Vigor => vigor,
        DragonAttributes.Attribute.Wind => wind,
        DragonAttributes.Attribute.Instinct => instinct,
        _ => 0f,
    };
}
