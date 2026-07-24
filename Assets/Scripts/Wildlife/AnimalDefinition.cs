using System;
using UnityEngine;

/// <summary>
/// Definição de uma população animal — 100% data-driven (GDD: um único sistema de
/// IA baseado em parâmetros reutilizáveis). Cada espécie/população é um asset em
/// `Assets/Scriptables/Resources/Wildlife/` carregado pelo WildlifeSpawner.
///
/// Nada de scripts por espécie: personalidade, social, biomas, spawn, migração e
/// até os NOMES dos clipes de animação (detectados do asset Forest Animals 2.0
/// pelo WildlifeSetup) vivem aqui. Animais novos = criar outro asset.
/// </summary>
[CreateAssetMenu(menuName = "Everwyrm/Animal Definition", fileName = "NovoAnimal")]
public class AnimalDefinition : ScriptableObject
{
    /// <summary>Organização social (GDD Wildlife): como o grupo nasce e se move.</summary>
    public enum SocialModel { Solitario, Rebanho, Alcateia, Hibrido }

    /// <summary>Papel de um indivíduo dentro do grupo.</summary>
    /// <summary>Líder · Membro · Filhote. A ORDEM é contrato de serialização (enum
    /// grava como índice nos assets de fauna) — só acrescente no fim.</summary>
    public enum RoleKind { Leader, Member, Young }

    /// <summary>Períodos de atividade — pronto para o futuro ciclo dia/noite.</summary>
    [Flags]
    public enum ActivityPeriod
    {
        Madrugada = 1, Dia = 2, Entardecer = 4, Noite = 8,
        Sempre = Madrugada | Dia | Entardecer | Noite
    }

    // ------------------------------------------------------------------ DADOS
    [Serializable]
    public class BiomeAffinity
    {
        public InfiniteTerrain.Biome biome = InfiniteTerrain.Biome.Floresta;
        [Range(0f, 1f)] public float minWeight = 0.3f;  // peso mínimo do bioma no ponto
        [Range(0f, 1f)] public float affinity = 1f;     // multiplicador de frequência
    }

    /// <summary>Sequência start→loop(s)→end (padrão do asset: Lie_start/loop/end...).</summary>
    [Serializable]
    public class AnimSequence
    {
        public string start;      // vazio = sem intro
        public string[] loops;    // 1+ variações de loop
        public string end;        // vazio = sem saída
        public bool IsValid => loops != null && loops.Length > 0;
    }

    /// <summary>
    /// Nomes dos ESTADOS do Animator (== nomes curtos dos clipes do asset).
    /// Auto-preenchido pelo WildlifeSetup a partir dos clipes reais de cada
    /// variante — espécies diferentes têm vocabulários diferentes (lobo tem
    /// Howl e Crouch, lebre tem Hide, urso levanta em duas patas...).
    /// Campos vazios = comportamento indisponível para a espécie.
    /// </summary>
    [Serializable]
    public class AnimalAnimSet
    {
        public string[] idles;         // Idle_1..N — variedade de "olhar em volta"
        public string[] extras;        // Scratching, Defecate... raridades charmosas
        public AnimSequence graze;     // EatDrink_start / Eat_loop* / EatDrink_end
        public string grazeWalk;       // Eat_walk_IP — pastar andando (cervos)
        public AnimSequence drink;     // EatDrink_start / Drink_loop / EatDrink_end
        public AnimSequence dig;       // Digging_start/loop/end (raposa, lobo)
        public string digWalk;         // Dig_walk_IP — javali fuçando o chão
        public AnimSequence lie;       // Lie_start/loop/end — descansar deitado
        public AnimSequence lieBelly;  // Lie_belly_* (canídeos)
        public AnimSequence sit;       // Sit(ting)_start/loop/end
        public AnimSequence sleep;     // Lie_sleep_start/loop/end
        public AnimSequence hide;      // Hide_* — lebre se escondendo
        public AnimSequence stand;     // Stand_* — urso em duas patas (intimidação)
        public AnimSequence stalkIdle; // Crouch_Idle_* — espreita parada
        public string stalkWalk;       // Crouch_F_IP — aproximação em tocaia
        public string howl;            // Howl — só o lobo
        public string eatCarcass;      // Eat_tear — predador comendo a presa
        public string jumpPlace;       // Jump_place_IP — filhotes brincando
        public string[] attacks;       // golpes parados
        public string chargeAttack;    // Attack_Run_IP — investida
        public string[] deaths;        // Death_L / Death_R
        public string[] hits;          // Hit_F / Hit_B / Hit_M
        public string runFast;         // RunFast_F_IP — sprint de predador
    }

    /// <summary>
    /// Um papel do grupo: acopla os prefabs (variações de cor c1..cN) à variante
    /// certa do asset (controller + clipes próprios). Filhotes NUNCA nascem sem
    /// os adultos: o spawner só os cria junto do grupo e amarrados a uma mãe.
    /// </summary>
    [Serializable]
    public class AnimalRole
    {
        public string name;
        public RoleKind kind = RoleKind.Member;
        public GameObject[] prefabs;                    // variações visuais da variante
        public RuntimeAnimatorController controller;
        public AnimalAnimSet anims = new();
        public Vector2Int count = new(1, 1);            // quantos por grupo
        [Range(0f, 1f)] public float chance = 1f;       // chance do papel aparecer
        public Vector2 scaleRange = new(0.95f, 1.05f);  // jitter de tamanho
        public float speedMul = 1f;                     // filhotes são mais lentos
    }

    // -------------------------------------------------------------- IDENTIDADE
    [Header("Identidade")]
    public string speciesName = "Animal";
    [TextArea] public string description;

    [Header("Social")]
    public SocialModel social = SocialModel.Rebanho;
    public Vector2Int groupSize = new(3, 6);
    [Range(0f, 1f)] public float soloChance = 0f;   // Híbrido: chance de vir sozinho
    public float memberSpacing = 4f;                // distância confortável entre indivíduos
    public float catchUpDistance = 10f;             // ficou pra trás além disso → acelera
    public AnimalRole[] roles;

    [Header("Biomas & Spawn")]
    public BiomeAffinity[] biomes;
    public float spawnWeight = 5f;                  // frequência relativa (raridade)
    public int maxActiveGroups = 3;
    public ActivityPeriod activity = ActivityPeriod.Sempre;

    [Header("Movimento (m/s)")]
    public float walkSpeed = 1.3f;
    public float trotSpeed = 3.5f;
    public float runSpeed = 7.5f;
    public float turnSpeed = 180f;                  // graus/s

    [Header("Personalidade (0..1)")]
    [Range(0f, 1f)] public float bravery = 0.3f;          // quanto encara ameaças
    [Range(0f, 1f)] public float aggression = 0.1f;       // disposição a atacar
    [Range(0f, 1f)] public float curiosity = 0.3f;        // investigar em vez de fugir
    [Range(0f, 1f)] public float alertness = 0.5f;        // frequência de vigília
    [Range(0f, 1f)] public float grazing = 0.5f;          // tendência a pastar/comer
    [Range(0f, 1f)] public float restfulness = 0.3f;      // deitar/sentar/dormir
    [Range(0f, 1f)] public float waterAffinity = 0.3f;    // procurar lagos p/ beber
    [Range(0f, 1f)] public float unpredictability = 0f;   // javali: foge OU ataca
    [Range(0f, 1f)] public float playfulness = 0.5f;      // brincadeira dos filhotes

    [Header("Percepção & Ameaça")]
    public float sightRange = 60f;
    public float fleeDistance = 40f;                // base; coragem e tamanho do dragão modulam
    public float attackRange = 0f;                  // 0 = nunca ataca o dragão
    public float meleeRange = 2.6f;
    public float attackDamage = 10f;
    [Range(0f, 1f)] public float ignoreBelowGrowth = 0f;   // dragão menor que isso é ignorado
    [Range(0f, 1f)] public float panicAboveGrowth = 0.75f; // dragão maior que isso: pânico

    [Header("Território & Migração")]
    public bool territorial = false;
    public float territoryRadius = 90f;
    public float migrationChancePerMinute = 0.06f;  // GDD: migrações espontâneas
    public Vector2 migrationDistance = new(250f, 550f);

    [Header("Vida & Cadeia Alimentar")]
    public float health = 50f;
    public float nutrition = 40f;                   // carcaça deixada ao morrer
    public bool predator = false;
    public string[] preySpecies;                    // speciesName das presas
    public float huntChancePerMinute = 0.1f;

    /// <summary>Afinidade da espécie com o bioma dominante no ponto (0 = não vive lá).</summary>
    public float BiomeAffinityAt(float wx, float wz)
    {
        var world = InfiniteTerrain.Instance;
        if (world == null || biomes == null || biomes.Length == 0) return 1f;
        float best = 0f;
        foreach (var b in biomes)
        {
            float w = world.BiomeWeightOf(b.biome, wx, wz);
            if (w >= b.minWeight) best = Mathf.Max(best, b.affinity * w);
        }
        return best;
    }
}

/// <summary>
/// Relógio da fauna — ponte para o futuro ciclo dia/noite. Enquanto ele não
/// existe, é sempre Dia; quando existir, basta plugar um Provider.
/// </summary>
public static class WildlifeClock
{
    public static Func<AnimalDefinition.ActivityPeriod> Provider;
    public static AnimalDefinition.ActivityPeriod Current =>
        Provider?.Invoke() ?? AnimalDefinition.ActivityPeriod.Dia;
}
