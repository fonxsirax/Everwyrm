using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A BASE da linhagem (GDD v2.0) — guarda os dragões (records) e conduz a possessão do
/// atual no avatar único. É o dono do `currentDragon`: no início possui o primeiro
/// dragão vivo; quando o atual cai (combate ou velhice), possui o próximo vivo — sem
/// nunca recarregar a cena. Quando todos morrem, a linhagem se extingue (placeholder
/// em F0; a reprodução que repõe a Base é feature posterior).
///
/// Setup rápido: Tools > Everwyrm > Base de Dragões (aleatórios).
/// </summary>
public class DragonBase : MonoBehaviour
{
    public static DragonBase Instance { get; private set; }

    [Header("Linhagem")]
    [Tooltip("Os dragões guardados na Base (records). O primeiro vivo é possuído no início.")]
    [SerializeField] List<DragonRecord> dragons = new();
    [Tooltip("O avatar único Unka na cena. Vazio = procurado automaticamente.")]
    [SerializeField] DragonPossession avatar;
    [Tooltip("Onde um dragão recém-possuído aparece. Vazio = na posição atual do " +
             "avatar (a troca acontece no lugar — takeover imediato).")]
    [SerializeField] Transform spawnPoint;

    [Header("Aleatórios")]
    [Tooltip("Sorteia um dragão INÉDITO a cada Play, ignorando a lista acima — o modo " +
             "laboratório: rode, olhe, e guarde os que prestarem com " +
             "Tools > Everwyrm > Salvar Dragão Atual.")]
    [SerializeField] bool randomEveryPlay;
    [Tooltip("Skin usada pelos dragões sorteados (paleta de material e piso do tint). " +
             "Sem ela o aleatório sai com o material atual do avatar.")]
    [SerializeField] DragonSkin fallbackSkin;

    [Header("Balanceamento")]
    [Tooltip("O talento sorteado no nascimento e as chances de mutação vivem no " +
             "asset Assets/Scriptables/Resources/Balance/Lineage.asset. Vazio = " +
             "esse padrão.")]
    [SerializeField] LineageProfile lineageProfile;

    /// <summary>O perfil resolvido, sob demanda: o campo acima quando preenchido,
    /// senão o asset padrão. PREGUIÇOSO de propósito — a janela de balanceamento
    /// (Tools > Everwyrm > Balanço do Dragão) lê estes números direto no PREFAB,
    /// fora do Play, onde nenhum Awake rodou.</summary>
    LineageProfile cfgCache;
    LineageProfile cfg => cfgCache != null ? cfgCache : (cfgCache = Balance.Resolve(lineageProfile));

    int currentIndex = -1;

    public IReadOnlyList<DragonRecord> Dragons => dragons;
    public int CurrentIndex => currentIndex;
    public DragonRecord Current =>
        currentIndex >= 0 && currentIndex < dragons.Count ? dragons[currentIndex] : null;
    public DragonPossession Avatar => avatar;
    public int MaxIV => cfg.maxIV;

    void Awake()
    {
        Instance = this;
        if (avatar == null) avatar = FindFirstObjectByType<DragonPossession>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (avatar == null)
        {
            Debug.LogWarning("DragonBase: nenhum DragonPossession na cena — o avatar Unka " +
                             "precisa do componente (rode Tools > Everwyrm > Base de Dragões, " +
                             "que o instala no prefab).");
            return;
        }
        if (randomEveryPlay) { PossessRandom(); return; }

        int first = NextAlive(-1);
        if (first >= 0) Possess(first);
        else PossessRandom(dragons.Count == 0
            ? "a lista de dragões da Base está VAZIA"
            : "nenhum dragão da Base está vivo");
    }

    /// <summary>Nasce um dragão de genoma sorteado, só para esta sessão. Dois caminhos
    /// chegam aqui e o log distingue:
    ///  · `randomEveryPlay` ligado — é o laboratório de genética, tudo em ordem;
    ///  · Base vazia/morta — ERRO: ela deveria estar povoada, mas o jogo continua em vez
    ///    de travar sem avatar.
    ///
    /// O record vive só em memória (sem AssetDatabase): morre ao sair do Play, a menos
    /// que Tools > Everwyrm > Salvar Dragão Atual o grave em
    /// Assets/Scriptables/Resources/Entities/Dragons.</summary>
    public DragonRecord PossessRandom(string errorReason = null)
    {
        string howToKeep = "Gostou? Tools > Everwyrm > Salvar Dragão Atual grava " +
                           "este genoma em Assets/Scriptables/Resources/Entities/Dragons.";
        if (errorReason != null)
            Debug.LogError($"<b>DragonBase:</b> {errorReason} — improvisando um dragão " +
                           $"ALEATÓRIO para não ficar sem avatar. {howToKeep}");
        else
            Debug.Log($"<b>DragonBase:</b> sorteando um dragão inédito (randomEveryPlay). {howToKeep}");

        var record = DragonRecord.CreateRandom(DragonNames.Random(), fallbackSkin, cfg.maxIV);
        record.name = record.dragonName;
        dragons.Add(record);
        Possess(dragons.Count - 1);
        return record;
    }

    /// <summary>Cria um filhote de dois pais usando os knobs de balanceamento desta
    /// Base (acima) e o guarda na lista — pronto para quando a reprodução em jogo
    /// (ninho/ovo, feature posterior) chamar isto. NÃO possui o filhote sozinho.</summary>
    public DragonRecord BreedNew(string dragonName, DragonRecord parentA, DragonRecord parentB)
    {
        var child = DragonRecord.Breed(dragonName, parentA, parentB, fallbackSkin, cfg.maxIV,
            cfg.traitMutationChance, cfg.statMutationChance, cfg.extraSlotMutationChance, cfg.ivOutlierBonus);
        child.name = child.dragonName;
        dragons.Add(child);
        return child;
    }

    /// <summary>Possui o dragão do índice: o avatar carrega a skin e o estado dele.</summary>
    public void Possess(int index)
    {
        if (avatar == null || index < 0 || index >= dragons.Count || dragons[index] == null) return;
        currentIndex = index;
        Vector3 pos = spawnPoint != null ? spawnPoint.position : avatar.transform.position;
        Quaternion rot = spawnPoint != null ? spawnPoint.rotation : avatar.transform.rotation;
        avatar.Bind(dragons[index], pos, rot);
        var d = dragons[index];
        string traits = d.traits != null && d.traits.Length > 0
            ? string.Join(", ", System.Array.ConvertAll(d.traits, DragonTraitInfo.Name))
            : "nenhum";
        Debug.Log($"<b>Possuindo {d.dragonName}</b> — {DragonNatureTable.Describe(d.nature)} · " +
                  $"IV For{d.ivMight}/Cha{d.ivArdor}/Agi{d.ivAgility}/Vig{d.ivVigor}/Fôl{d.ivWind}/Ins{d.ivInstinct}" +
                  (d.mutations > 0 ? $" · {d.mutations} mutação(ões)" : "") +
                  $" · traços: {traits}\n" +
                  Describe(d.genes));
    }

    /// <summary>Resumo legível do genoma para o Console — com dragões sorteados a cada
    /// Play, é por aqui que se julga se vale guardar antes de olhar o modelo.</summary>
    static string Describe(DragonGenes g)
    {
        if (g == null) return "genoma: —";
        string head = Variant(DragonBlendShapes.Heads, g.headVariant, "base");
        string horn = Variant(DragonBlendShapes.Horns, g.hornVariant, "sem chifres");
        return $"genoma: cabeça {head} · {horn} · espinhos " +
               $"{g.armSpikes:0}/{g.legsSpikes:0}/{g.neckSpikes:0}/{g.bodySpikes:0} " +
               $"(braço/perna/pescoço/corpo) · dedos de asa {g.wingFingers:0} · " +
               $"material #{g.bodyMaterial} · tint {ColorUtility.ToHtmlStringRGB(g.tint)} · " +
               $"hue das asas {g.wingHue:0.00}";
    }

    /// <summary>Nome da variante exclusiva (1..N), ou `none` para 0 — e para qualquer
    /// índice fora da tabela, que um asset editado à mão pode trazer.</summary>
    static string Variant(string[] table, int oneBased, string none) =>
        oneBased > 0 && oneBased <= table.Length ? table[oneBased - 1] : none;

    /// <summary>Possui o próximo dragão VIVO (chamado quando o atual cai).</summary>
    public void PossessNext()
    {
        int next = NextAlive(currentIndex);
        if (next >= 0) Possess(next);
        else OnLineageExtinct();
    }

    /// <summary>A possessão avisa que o dragão atual caiu (morte/velhice).</summary>
    public void OnCurrentDown() => PossessNext();

    /// <summary>Próximo índice com dragão vivo depois de `from` (circular). -1 = nenhum.</summary>
    int NextAlive(int from)
    {
        int n = dragons.Count;
        for (int step = 1; step <= n; step++)
        {
            int i = ((from + step) % n + n) % n;
            if (dragons[i] != null && dragons[i].IsAlive) return i;
        }
        return -1;
    }

    void OnLineageExtinct()
    {
        // F0: placeholder. A reprodução/genética que repõe a Base é feature posterior.
        Debug.LogWarning("<b>Linhagem extinta.</b> Todos os dragões da Base morreram — " +
                         "recomeçando (placeholder até a reprodução).");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

#if UNITY_EDITOR
    /// <summary>Preenche a Base a partir do setup editor (Tools > Everwyrm > Base de Dragões).</summary>
    public void EditorConfigure(List<DragonRecord> records, DragonPossession possession,
                                Transform spawn, DragonSkin skin = null)
    {
        dragons = records;
        avatar = possession;
        spawnPoint = spawn;
        if (skin != null) fallbackSkin = skin;
    }
#endif
}
