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
/// BALANCEAMENTO: tudo é [SerializeField]. Simule fora do Play em
/// <b>Tools > Everwyrm > Balanço do Dragão</b>, ou abra a ficha em jogo com Tab.
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

    [Header("Escala dos atributos")]
    [Tooltip("Valor de um atributo no auge da vida. A escala inteira do balanceamento.")]
    [SerializeField] float maxAttribute = 10f;

    [Header("Maturidade (o motor dos atributos)")]
    [Tooltip("Peso do CRESCIMENTO (tamanho, depende de comer bem) contra a IDADE pura. " +
             "1 = só o tamanho manda; 0 = só a idade.")]
    [SerializeField, Range(0f, 1f)] float growthWeight = 0.65f;
    [Tooltip("Molda a maturidade crua (X) na efetiva (Y). Reta = amadurecer acompanha " +
             "o crescimento; curva em S = infância longa e explosão na adolescência.")]
    [SerializeField] AnimationCurve maturityCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("Quanto a Velhice corrói os atributos no último suspiro (0.25 = -25%).")]
    [SerializeField, Range(0f, 0.9f)] float elderDecline = 0.25f;

    [Header("Curvas por atributo (X = maturidade · Y = fração do máximo)")]
    [Tooltip("Agilidade amadurece CEDO — o filhote já é ligeiro.")]
    [SerializeField] AnimationCurve agilityCurve = Smooth(0f, 0.15f, 0.4f, 0.7f, 1f, 1f);
    [Tooltip("Força vem com a MASSA — quase toda no fim.")]
    [SerializeField] AnimationCurve mightCurve = Smooth(0f, 0f, 0.4f, 0.22f, 1f, 1f);
    [Tooltip("Vigor sobe parelho com a vida inteira.")]
    [SerializeField] AnimationCurve vigorCurve = Smooth(0f, 0.1f, 0.4f, 0.45f, 1f, 1f);
    [Tooltip("Chama desperta na adolescência — nada no filhote, forte no adulto.")]
    [SerializeField] AnimationCurve ardorCurve = Smooth(0f, 0f, 0.4f, 0.3f, 1f, 1f);
    [Tooltip("Fôlego (energia/teto) acompanha a vida.")]
    [SerializeField] AnimationCurve windCurve = Smooth(0f, 0.05f, 0.4f, 0.45f, 1f, 1f);
    [Tooltip("Instinto é quase inato — o filhote já fareja, e refina com a idade.")]
    [SerializeField] AnimationCurve instinctCurve = Smooth(0f, 0.2f, 0.4f, 0.6f, 1f, 1f);

    [Header("Identidade (linhagem)")]
    [Tooltip("Quanto a natureza favorece/prejudica um atributo (0.1 = ±10%, estilo Pokémon).")]
    [SerializeField, Range(0f, 0.5f)] float natureModifier = 0.1f;
    [Tooltip("IV máximo sorteado no nascimento (o teto do talento bruto de cada atributo).")]
    [SerializeField] int maxIV = 31;
    [Tooltip("Quanto o IV cheio vale em fração de maxAttribute (0.25 = +2.5 pontos).")]
    [SerializeField, Range(0f, 1f)] float ivInfluence = 0.25f;

    [Header("Ganhos por PONTO — Agilidade")]
    [SerializeField] float speedGain = 0.04f;        // +4% vel. máxima por ponto
    [SerializeField] float accelAgilityGain = 0.05f; // aceleração (Agilidade)
    [SerializeField] float turnGain = 0.03f;         // giro (chão e voo)

    [Header("Ganhos por PONTO — Força")]
    [SerializeField] float damageGain = 0.09f;
    [SerializeField] float boostGain = 0.05f;        // força do Wing Boost

    [Header("Ganhos por PONTO — Vigor")]
    [SerializeField] float healthGain = 0.10f;
    [SerializeField] float accelVigorGain = 0.02f;   // aceleração (o empurrão do corpo)
    [SerializeField] float hungerResistGain = 0.03f; // fome cai mais devagar

    [Header("Ganhos por PONTO — Chama")]
    [SerializeField] float flameGain = 0.06f;

    [Header("Ganhos por PONTO — Fôlego")]
    [SerializeField] float energyGain = 0.09f;            // duração do voo
    [Tooltip("Reduz o CUSTO de energia de tudo (voar, correr, atacar) — o dragão " +
             "de longa distância. Saturado em 0.5× de custo.")]
    [SerializeField] float energyEfficiencyGain = 0.03f;
    [SerializeField] float takeoffClimbGain = 0.06f;     // duração da subida de decolagem

    [Header("Ganhos por PONTO — Instinto")]
    [SerializeField] float dominanceBase = 30f;      // raio base do faro (m)
    [SerializeField] float dominanceGain = 12f;
    [Tooltip("Estica as janelas de invulnerabilidade (dash/boost/esquiva) — o dragão " +
             "que atravessa o golpe.")]
    [SerializeField] float iframeGain = 0.05f;
    [Tooltip("Lê melhor as correntes de ar: updrafts/térmicas rendem mais sustentação.")]
    [SerializeField] float windReadGain = 0.05f;

    [Header("Teto de voo (Fôlego)")]
    [Tooltip("Teto com 0 de Fôlego (m) — o filhote voa baixo.")]
    [SerializeField] float ceilingBase = 150f;
    [SerializeField] float ceilingGain = 15f;
    [Tooltip("Pontos de Fôlego até o teto cheio (satura aqui).")]
    [SerializeField] float ceilingCap = 10f;

    [Header("Degraus de maturidade (desbloqueio de ataques)")]
    [Tooltip("Em quantos degraus a maturidade é fatiada. É o número que o " +
             "DragonAttackData.unlockLevel compara — não é um 'nível' de RPG.")]
    [SerializeField] int maxTier = 10;

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
    public int MaxTier => maxTier;
    /// <summary>0..1 dentro do degrau ATUAL — quanto falta para o próximo desbloqueio.
    /// 1 quando já está no degrau máximo (nada mais a desbloquear por maturidade).</summary>
    public float TierProgress01
    {
        get
        {
            if (maxTier <= 1 || Tier >= maxTier) return 1f;
            float span = 1f / (maxTier - 1);
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
    public float MaxAttribute => maxAttribute;

    // ---- Identidade exposta (HUD/ficha/balanço)
    public DragonNature Nature => nature;
    public int MaxIV => maxIV;
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

        float m = MaturityFor(growth.Growth01, AgeProgress01, growth.ElderProgress);
        snap = SnapshotAt(m, nature, ivs);

        // o Tier é MONOTÔNICO: envelhecer enfraquece o corpo, mas ninguém
        // desaprende um golpe já destravado
        if (m > peakMaturity) peakMaturity = m;
        int tier = Mathf.Clamp(1 + Mathf.FloorToInt(peakMaturity * (maxTier - 1) + 0.0001f), 1, maxTier);
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
        float raw = Mathf.Clamp01(growthWeight * Mathf.Clamp01(growth01) +
                                  (1f - growthWeight) * Mathf.Clamp01(ageProgress01));
        float m = Mathf.Clamp01(maturityCurve.Evaluate(raw));
        return m * (1f - elderDecline * Mathf.Clamp01(elderProgress01));
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
            agility = Value(agilityCurve, m, iv, Attribute.Agility, n),
            might = Value(mightCurve, m, iv, Attribute.Might, n),
            vigor = Value(vigorCurve, m, iv, Attribute.Vigor, n),
            ardor = Value(ardorCurve, m, iv, Attribute.Ardor, n),
            wind = Value(windCurve, m, iv, Attribute.Wind, n),
            instinct = Value(instinctCurve, m, iv, Attribute.Instinct, n),
        };

        s.speedMul = 1f + s.agility * speedGain;
        s.accelMul = 1f + s.agility * accelAgilityGain + s.vigor * accelVigorGain;
        s.turnMul = 1f + s.agility * turnGain;
        s.damageMul = 1f + s.might * damageGain;
        s.boostMul = 1f + s.might * boostGain;
        s.flameMul = 1f + s.ardor * flameGain;
        s.dominanceRadius = dominanceBase + s.instinct * dominanceGain;
        s.healthMul = 1f + s.vigor * healthGain;
        s.energyMul = 1f + s.wind * energyGain;
        s.energyCostMul = Mathf.Max(0.5f, 1f - s.wind * energyEfficiencyGain);
        s.hungerDecayMul = Mathf.Max(0.4f, 1f - s.vigor * hungerResistGain);
        s.takeoffClimbMul = 1f + s.wind * takeoffClimbGain;
        s.maxAltitude = ceilingBase + Mathf.Min(s.wind, ceilingCap) * ceilingGain;
        s.iframeMul = 1f + s.instinct * iframeGain;
        s.windReadMul = 1f + s.instinct * windReadGain;
        return s;
    }

    /// <summary>Um atributo: curva de maturação × escala, mais o talento (IV),
    /// tudo modulado pela natureza.</summary>
    float Value(AnimationCurve curve, float maturity01, int[] iv, Attribute a, DragonNature n)
    {
        float baseValue = Mathf.Max(0f, curve.Evaluate(maturity01)) * maxAttribute;
        int talentIv = iv != null && (int)a < iv.Length ? iv[(int)a] : 0;
        float talent = maxIV > 0 ? (float)talentIv / maxIV * maxAttribute * ivInfluence : 0f;
        return (baseValue + talent) * DragonNatureTable.Multiplier(n, a, natureModifier);
    }

    /// <summary>Curva suave por 3 pontos — os defaults das curvas de maturação.</summary>
    static AnimationCurve Smooth(float x0, float y0, float x1, float y1, float x2, float y2)
    {
        var c = new AnimationCurve(new Keyframe(x0, y0), new Keyframe(x1, y1), new Keyframe(x2, y2));
        for (int i = 0; i < 3; i++) c.SmoothTangents(i, 0f);
        return c;
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
