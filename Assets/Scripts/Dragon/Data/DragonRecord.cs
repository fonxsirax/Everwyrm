using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// A ENTIDADE dragão como dado — a "alma" que existe independentemente do avatar na
/// cena. É o pilar do pivô de linhagem (GDD v2.0): a Base guarda vários DragonRecord,
/// e a possessão carrega o `currentDragon` no avatar único Unka.
///
/// Duas camadas, no mesmo espírito do WorldGenState:
///  · IDENTIDADE (fixa): nome, skin, natureza, os SEIS IVs e os TRAÇOS herdáveis —
///    o que torna ESTE dragão único e não muda ao longo da vida.
///  · ESTADO (DragonState, [Serializable]): tudo que muda em runtime — idade,
///    crescimento, vitais, loadout. É o ÚNICO que um save precisa gravar.
///
/// Menu: Create > Everwyrm > Dragon Record
/// </summary>
[CreateAssetMenu(menuName = "Everwyrm/Dragon Record", fileName = "NovoDragao")]
public class DragonRecord : ScriptableObject
{
    /// <summary>Quantos traços um dragão pode carregar (slots de nascimento/herança).</summary>
    public const int MaxTraitSlots = 3;

    [Header("Identidade")]
    public string dragonName = "Sem Nome";
    public DragonSkin skin;
    public DragonNature nature = DragonNature.Balanced;

    [Header("Genoma visual (herdado — morfologia e cor)")]
    public DragonGenes genes = new DragonGenes();

    [Header("Individual Values — talento por atributo (0..maxIV, mutação pode passar)")]
    // FormerlySerializedAs: campos serializam por NOME. A migração 3→6 atributos
    // reaproveita os IVs antigos: Velocidade→Agilidade, Poder→Força,
    // Resistência→Fôlego. Chama e Instinto são novos (0 em dragões antigos). O
    // ivVigor manteve o nome (era o talento de vida longa e agora também é o
    // atributo Vigor). Sem os FormerlySerializedAs, dragões já salvos zerariam.
    [Tooltip("Talento bruto de cada atributo. Bônus fixo aplicado em DragonAttributes.")]
    [FormerlySerializedAs("ivVelocidade")] [FormerlySerializedAs("ivSpeed")] public int ivAgility;
    [FormerlySerializedAs("ivPoder")] [FormerlySerializedAs("ivPower")] public int ivMight;
    [Tooltip("Vigor — vida e sobrevivência: também acelera o crescimento e alonga a vida.")]
    public int ivVigor;
    [Tooltip("Chama — talento de fogo (novo no rework; 0 em dragões antigos).")]
    public int ivArdor;
    [FormerlySerializedAs("ivResistencia")] [FormerlySerializedAs("ivResistance")] public int ivWind;
    [Tooltip("Instinto — faro e reflexos (novo no rework; 0 em dragões antigos).")]
    public int ivInstinct;

    [Header("Traços herdáveis (sorteados no nascimento — ver DragonTrait)")]
    public DragonTrait[] traits = Array.Empty<DragonTrait>();

    [Tooltip("Slots de ataque ALÉM dos 4 padrão (DragonAbilities.MaxSlotCount limita o " +
             "total). O prêmio de mutação preferido — um golpe a mais muda o jogo mais " +
             "que um número que já satura.")]
    public int bonusAttackSlots;
    [Tooltip("1 se este dragão nasceu de uma MUTAÇÃO (slot extra OU talento acima do " +
             "teto) — no máx. 1 por ninhada, é o prêmio raro de criar seletivamente.")]
    public int mutations;

    [Header("Estado (muda em runtime — o que um save grava)")]
    public DragonState state = new DragonState();

    /// <summary>Cria um record novo com IVs, natureza e traços sorteados. Não há pontos
    /// de atributo para distribuir: os atributos vêm da maturidade (DragonAttributes),
    /// e o que este dragão tem de próprio são a natureza, os IVs e os traços.</summary>
    public static DragonRecord CreateRandom(string dragonName, DragonSkin skin, int maxIV = 31)
    {
        var r = CreateInstance<DragonRecord>();
        r.dragonName = dragonName;
        r.skin = skin;
        r.nature = (DragonNature)UnityEngine.Random.Range(0, Enum.GetValues(typeof(DragonNature)).Length);
        r.ivAgility = UnityEngine.Random.Range(0, maxIV + 1);
        r.ivMight = UnityEngine.Random.Range(0, maxIV + 1);
        r.ivVigor = UnityEngine.Random.Range(0, maxIV + 1);
        r.ivArdor = UnityEngine.Random.Range(0, maxIV + 1);
        r.ivWind = UnityEngine.Random.Range(0, maxIV + 1);
        r.ivInstinct = UnityEngine.Random.Range(0, maxIV + 1);
        r.genes = DragonGenes.Random(skin);
        r.traits = RollTraits();
        r.state = new DragonState();
        return r;
    }

    /// <summary>REPRODUÇÃO — um filhote dos dois pais: genoma visual cruzado
    /// (DragonGenes.Breed), IVs herdados normalmente (sem mutação embutida), traços
    /// herdados, natureza de um deles, e UMA rolagem de MUTAÇÃO por ninhada (não por
    /// IV — ver `RollMutation`).
    ///
    /// A reprodução do JOGO (quem cruza, quando, o custo) é feature posterior; o
    /// cruzamento dos DADOS já vive aqui, pronto e testável. Os `*Chance`/`outlierBonus`
    /// default aqui são só o fallback de quem chamar Breed direto (testes/editor);
    /// o jogo em si lê os knobs de balanceamento em DragonBase.</summary>
    public static DragonRecord Breed(string dragonName, DragonRecord a, DragonRecord b,
                                     DragonSkin skin = null, int maxIV = 31,
                                     float traitMutationChance = 0.08f,
                                     float statMutationChance = 0.05f,
                                     float extraSlotMutationChance = 0.02f,
                                     int outlierBonus = 4)
    {
        if (a == null || b == null) return CreateRandom(dragonName, skin, maxIV);

        var r = CreateInstance<DragonRecord>();
        r.dragonName = dragonName;
        r.skin = skin != null ? skin : a.skin;
        r.nature = UnityEngine.Random.value < traitMutationChance
            ? (DragonNature)UnityEngine.Random.Range(0, Enum.GetValues(typeof(DragonNature)).Length)
            : (UnityEngine.Random.value < 0.5f ? a.nature : b.nature);

        r.genes = DragonGenes.Breed(a.genes, b.genes, r.skin, traitMutationChance);

        r.ivAgility = BreedIV(a.ivAgility, b.ivAgility, maxIV);
        r.ivMight = BreedIV(a.ivMight, b.ivMight, maxIV);
        r.ivVigor = BreedIV(a.ivVigor, b.ivVigor, maxIV);
        r.ivArdor = BreedIV(a.ivArdor, b.ivArdor, maxIV);
        r.ivWind = BreedIV(a.ivWind, b.ivWind, maxIV);
        r.ivInstinct = BreedIV(a.ivInstinct, b.ivInstinct, maxIV);

        RollMutation(r, a, b, statMutationChance, extraSlotMutationChance, outlierBonus);

        r.traits = BreedTraits(a.traits, b.traits, traitMutationChance);
        r.state = new DragonState();
        return r;
    }

    /// <summary>IV do filho: parte do melhor dos pais e desvia — criar seletivamente
    /// compensa, mas nunca garante. SEM mutação embutida (ver RollMutation).</summary>
    static int BreedIV(int a, int b, int maxIV)
    {
        int mid = Mathf.RoundToInt((a + b) * 0.5f);
        int best = Mathf.Max(a, b);
        int seed = UnityEngine.Random.value < 0.5f ? mid : best;
        return Mathf.Clamp(seed + UnityEngine.Random.Range(-4, 5), 0, maxIV);
    }

    // ------------------------------------------------------------ MUTAÇÃO
    /// <summary>UMA rolagem por ninhada — não mais uma por IV (eram 6 chances
    /// independentes; um ninho quase sempre saía com "algum" outlier, e virou número
    /// pra cima sem graça, ainda mais empilhado no rebalanceamento recente de voo).
    /// PREFERE o SLOT DE ATAQUE EXTRA sobre o outlier de stat: um golpe a mais no
    /// loadout é sentido em jogo de um jeito que +4 num atributo que já satura não é.</summary>
    static void RollMutation(DragonRecord r, DragonRecord a, DragonRecord b,
                             float statMutationChance, float extraSlotMutationChance,
                             int outlierBonus)
    {
        float roll = UnityEngine.Random.value;
        if (roll < extraSlotMutationChance)
        {
            r.bonusAttackSlots = 1;
            r.mutations = 1;
        }
        else if (roll < extraSlotMutationChance + statMutationChance)
        {
            // outlier: UM atributo aleatório parte do melhor pai e ULTRAPASSA o
            // teto normal — o prodígio que recompensa criar por muitas gerações.
            int add = UnityEngine.Random.Range(1, outlierBonus + 1);
            switch (UnityEngine.Random.Range(0, 6))
            {
                case 0: r.ivAgility = Mathf.Max(a.ivAgility, b.ivAgility) + add; break;
                case 1: r.ivMight = Mathf.Max(a.ivMight, b.ivMight) + add; break;
                case 2: r.ivVigor = Mathf.Max(a.ivVigor, b.ivVigor) + add; break;
                case 3: r.ivArdor = Mathf.Max(a.ivArdor, b.ivArdor) + add; break;
                case 4: r.ivWind = Mathf.Max(a.ivWind, b.ivWind) + add; break;
                default: r.ivInstinct = Mathf.Max(a.ivInstinct, b.ivInstinct) + add; break;
            }
            r.mutations = 1;
        }
    }

    // ------------------------------------------------------------- TRAÇOS
    /// <summary>Sorteia traços de nascimento: chance decrescente por slot, sem
    /// repetir. A maioria dos dragões nasce com 1; poucos com 2-3.</summary>
    static DragonTrait[] RollTraits()
    {
        var pool = new List<DragonTrait>(DragonTraitInfo.Rollable);
        var chosen = new List<DragonTrait>();
        // ~65% ganham o 1º slot, ~35% o 2º (dado o 1º), ~18% o 3º — cauda curta
        float[] slotChance = { 0.65f, 0.35f, 0.18f };
        for (int i = 0; i < MaxTraitSlots && pool.Count > 0; i++)
        {
            if (UnityEngine.Random.value > slotChance[i]) break;
            int idx = UnityEngine.Random.Range(0, pool.Count);
            chosen.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return chosen.ToArray();
    }

    /// <summary>Herança de traços: cada traço dos pais tem chance de passar; sobra
    /// uma fresta para mutação (um traço novo). Nunca passa do teto de slots nem
    /// repete.</summary>
    static DragonTrait[] BreedTraits(DragonTrait[] a, DragonTrait[] b, float mutationChance)
    {
        var chosen = new List<DragonTrait>();

        void TryAdd(DragonTrait t)
        {
            if (t == DragonTrait.None || chosen.Count >= MaxTraitSlots || chosen.Contains(t)) return;
            chosen.Add(t);
        }

        // cada traço herdado com ~55% (embaralhado pela ordem dos pais)
        foreach (var t in Combine(a, b))
            if (UnityEngine.Random.value < 0.55f) TryAdd(t);

        // mutação: chance de um traço inteiramente novo
        if (UnityEngine.Random.value < mutationChance && chosen.Count < MaxTraitSlots)
            TryAdd(DragonTraitInfo.Rollable[UnityEngine.Random.Range(0, DragonTraitInfo.Rollable.Length)]);

        return chosen.ToArray();
    }

    static IEnumerable<DragonTrait> Combine(DragonTrait[] a, DragonTrait[] b)
    {
        if (a != null) foreach (var t in a) yield return t;
        if (b != null) foreach (var t in b) yield return t;
    }

    public bool IsAlive => !state.isDead;
}

/// <summary>
/// O estado mutável de um dragão. Defaults iguais ao começo de vida dos componentes
/// (DragonVitals.Awake, DragonGrowth), então um record recém-criado nasce coerente
/// sem precisar do avatar. Serializável isolado para o save modular futuro.
/// </summary>
[Serializable]
public class DragonState
{
    public int version = 1;

    // ---- Crescimento / idade (DragonGrowth)
    public float ageSeconds;
    public float growth01;                 // 0 filhote → 1 colossal
    public float condition01 = 0.5f;       // 0 magro → 1 gordo

    // ---- Atributos: NADA a gravar. São DERIVADOS da maturidade (crescimento +
    //      idade), que já vive nos campos acima — não há nível nem pontos a persistir.

    // ---- Vitais (DragonVitals) — frações 0..1 dos máximos efetivos
    public float hunger01 = 0.85f;
    public float energy01 = 1f;
    public float health01 = 1f;
    public float totalEaten;

    // ---- Ciclo de vida
    public bool isDead;
    public bool isDeadOfOldAge;

    // ---- Combate (DragonAbilities) — loadout dos 4 slots por nome de ataque
    public string[] equippedAttackNames;

    public string ToJson() => JsonUtility.ToJson(this);
    public static DragonState FromJson(string json) => JsonUtility.FromJson<DragonState>(json);
}
