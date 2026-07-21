using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Activity = AnimalDefinition.ActivityPeriod;
using Biome = InfiniteTerrain.Biome;
using RoleKind = AnimalDefinition.RoleKind;
using Social = AnimalDefinition.SocialModel;

/// <summary>
/// Setup da fauna — Menu: Tools > Everwyrm > Vida Selvagem.
/// Extrai o máximo do asset Forest Animals 2.0 (RedDeer3D) sem tocar nele:
///
///  1. Para cada uma das 21 variantes (macho/fêmea/filhote de 8 espécies),
///     gera um AnimatorController próprio em Assets/Everwyrm/Wildlife com:
///       - estado "Locomotion": blend tree 2D (Turn × Speed) usando
///         Idle/Turn/Walk/Trot/Run/RunFast direcionais do próprio asset;
///       - UM estado plano por clipe restante (Eat, Sleep, Howl, Crouch...)
///         para o AnimalAgent tocar via CrossFade por nome.
///  2. Autodetecta o "vocabulário" de clipes da variante (lobo uiva, lebre se
///     esconde, urso levanta, javali fuça...) e grava no AnimalAnimSet.
///  3. Cria as AnimalDefinition (as espécies) em
///     Assets/Scriptables/Resources/Wildlife — o WildlifeSpawner as carrega
///     sozinho em runtime; nenhuma cena precisa ser editada.
/// </summary>
public static class WildlifeSetup
{
    const string AssetRoot = "Assets/Red_Deer/Wild_Animals";
    const string OutControllers = "Assets/Everwyrm/Wildlife";
    const string OutDefs = "Assets/Scriptables/Resources/Wildlife";

    /// <summary>Sentinela do EverwyrmAutoSetup: se faltar, o setup roda de novo.
    /// Aponta sempre para a definição MAIS RECENTE — mudou o pacote de espécies,
    /// mude a sentinela (atenção: SetupAll sobrescreve tuning manual das defs).</summary>
    public const string MarkerAsset = OutDefs + "/LoboDoDeserto.asset";

    // As 21 variantes do pacote (pasta relativa a AssetRoot)
    static readonly string[] Variants =
    {
        "Bears/BearCub", "Bears/BearFemale", "Bears/BearMale",
        "Boars/BoarFemale", "Boars/BoarMale", "Boars/BoarYoung",
        "Deers/DeerCalf", "Deers/DeerDoe", "Deers/DeerStag",
        "Foxes/Fox", "Foxes/FoxCub",
        "Hares/HareMale", "Hares/HareYoung",
        "Moose/MooseBull", "Moose/MooseCalf", "Moose/MooseCow",
        "RedDeers/RedDeer_Calf", "RedDeers/RedDeer_Doe", "RedDeers/RedDeer_Stag",
        "Wolfes/WolfCub", "Wolfes/WolfMale",
    };

    class Variant
    {
        public AnimatorController controller;
        public AnimalDefinition.AnimalAnimSet anims;
        public GameObject[] prefabs;
    }

    static readonly Dictionary<string, Variant> built = new();

    /// <summary>Material-sonda: se ainda estiver em shader Built-in, a conversão precisa rodar.</summary>
    public const string ProbeMaterial =
        AssetRoot + "/Wolfes/WolfMale/Materials/WolfMale_color_1.mat";

    [MenuItem("Tools/Everwyrm/Vida Selvagem — Setup Completo")]
    public static void SetupMenu() => SetupAll(false);

    public static void SetupAll(bool quiet)
    {
        if (!AssetDatabase.IsValidFolder(AssetRoot))
        {
            Debug.LogWarning("WildlifeSetup: pacote Wild_Animals não encontrado.");
            return;
        }

        built.Clear();
        EnsureFolder(OutControllers);
        EnsureFolder(OutDefs);

        ConvertMaterials();
        foreach (var v in Variants) BuildVariant(v);
        CreateDefinitions();

        AssetDatabase.SaveAssets();
        if (!quiet)
            Debug.Log($"<b>Vida selvagem pronta!</b> {built.Count} variantes com Animator próprio, " +
                      "espécies em Assets/Scriptables/Resources/Wildlife. Dê Play e explore — " +
                      "o ecossistema se povoa sozinho.");
    }

    // ============================================================ MATERIAIS
    /// <summary>
    /// Converte os materiais do pacote (Standard/Built-in → magenta em HDRP)
    /// para HDRP/Lit, IN PLACE — mesmos assets/GUIDs, então todos os prefabs
    /// continuam apontando para eles. Mapeamento:
    ///   _MainTex → _BaseColorMap · _BumpMap → _NormalMap · _Color → _BaseColor
    ///   cutout (pelo em cartões) → Alpha Clipping + dupla face.
    /// </summary>
    [MenuItem("Tools/Everwyrm/Vida Selvagem — Converter Materiais HDRP")]
    public static void ConvertMaterials()
    {
        var hdrpLit = Shader.Find("HDRP/Lit");
        if (hdrpLit == null)
        {
            Debug.LogError("WildlifeSetup: shader HDRP/Lit não encontrado — projeto não está em HDRP?");
            return;
        }

        int converted = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { AssetRoot }))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m == null || m.shader == hdrpLit || m.shader.name.StartsWith("HDRP/")) continue;

            // captura ANTES de trocar o shader (os nomes das propriedades mudam)
            var albedo = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            var normal = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            var color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
            bool cutout = m.IsKeywordEnabled("_ALPHATEST_ON") ||
                          (m.HasProperty("_Mode") && Mathf.RoundToInt(m.GetFloat("_Mode")) == 1);

            m.shader = hdrpLit;
            m.SetTexture("_BaseColorMap", albedo);
            m.SetColor("_BaseColor", color);
            if (normal != null) m.SetTexture("_NormalMap", normal);
            m.SetFloat("_Metallic", 0f);        // pelo não é metal (o mask do Standard não é compatível)
            m.SetFloat("_Smoothness", 0.25f);
            if (cutout)
            {
                m.SetFloat("_AlphaCutoffEnable", 1f);
                m.SetFloat("_AlphaCutoff", cutoff);
                m.SetFloat("_DoubleSidedEnable", 1f);   // cartões de pelo visíveis dos dois lados
            }
            ValidateHdrp(m);
            EditorUtility.SetDirty(m);
            converted++;
        }

        if (converted > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"<b>Fauna em HDRP:</b> {converted} materiais convertidos para HDRP/Lit.");
        }
    }

    /// <summary>HDMaterial.ValidateMaterial via reflexão (recalcula keywords/passes do HDRP).</summary>
    static void ValidateHdrp(Material m)
    {
        var t = System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime")
             ?? System.Type.GetType("UnityEditor.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Editor");
        t?.GetMethod("ValidateMaterial", new[] { typeof(Material) })?.Invoke(null, new object[] { m });
    }

    // ========================================================== CONTROLLERS
    static void BuildVariant(string folder)
    {
        string variantName = folder.Substring(folder.IndexOf('/') + 1);
        string animDir = $"{AssetRoot}/{folder}/FBX/Anim";

        // clips do FBX in-place (movimento é do AnimalAgent, não root motion)
        var clips = new Dictionary<string, AnimationClip>();   // nome curto → clip
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { animDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith("_anim_IP.fbx")) continue;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is not AnimationClip c || c.name.StartsWith("__preview")) continue;
                string shortName = Short(c.name);
                if (!clips.ContainsKey(shortName)) clips[shortName] = c;
            }
        }
        if (clips.Count == 0)
        {
            Debug.LogWarning($"WildlifeSetup: nenhum clip em {animDir}");
            return;
        }

        // ---- controller
        string ctrlPath = $"{OutControllers}/{variantName}.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var c2 = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        c2.AddParameter("Speed", AnimatorControllerParameterType.Float);
        c2.AddParameter("Turn", AnimatorControllerParameterType.Float);
        var sm = c2.layers[0].stateMachine;

        // ---- Locomotion: blend 2D (Turn × Speed) com toda a locomoção do asset
        var used = new HashSet<string>();
        var tree = new BlendTree
        {
            name = "LocomotionTree",
            blendType = BlendTreeType.FreeformCartesian2D,
            blendParameter = "Turn",
            blendParameterY = "Speed",
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, c2);

        void Add(string shortName, float x, float y)
        {
            if (!clips.TryGetValue(shortName, out var clip)) return;
            tree.AddChild(clip, new Vector2(x, y));
            used.Add(shortName);
        }

        Add("Idle_1", 0f, 0f);
        Add("Turn_L_IP", -1f, 0f); Add("Turn_R_IP", 1f, 0f);
        Add("Walk_F_IP", 0f, 1f); Add("Walk_L_IP", -1f, 1f); Add("Walk_R_IP", 1f, 1f);
        Add("Trot_F_IP", 0f, 2f); Add("Trot_L_IP", -1f, 2f); Add("Trot_R_IP", 1f, 2f);
        Add("Run_F_IP", 0f, 3f); Add("Run_L_IP", -1f, 3f); Add("Run_R_IP", 1f, 3f);
        Add("RunFast_F_IP", 0f, 4f); Add("RunFast_L_IP", -1f, 4f); Add("RunFast_R_IP", 1f, 4f);

        var loco = sm.AddState("Locomotion");
        loco.motion = tree;
        sm.defaultState = loco;

        // ---- um estado plano por clipe restante (o agente toca por CrossFade)
        foreach (var kv in clips)
        {
            if (used.Contains(kv.Key)) continue;
            var s = sm.AddState(kv.Key);
            s.motion = kv.Value;
        }

        EditorUtility.SetDirty(c2);

        built[folder] = new Variant
        {
            controller = c2,
            anims = DetectAnims(clips.Keys),
            prefabs = FindPrefabs(folder),
        };
    }

    static string Short(string clipName)
    {
        int bar = clipName.LastIndexOf('|');
        return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
    }

    // ------------------------------------------------ VOCABULÁRIO DE CLIPES
    /// <summary>
    /// Mapeia o que a variante SABE fazer a partir dos nomes reais dos clipes —
    /// tolerante às variações de nomenclatura do pacote (Eat_loop1 vs Eat_loop_1,
    /// Sit vs Sitting, Turn180_L vs Turn_L180...).
    /// </summary>
    static AnimalDefinition.AnimalAnimSet DetectAnims(IEnumerable<string> names)
    {
        var all = new List<string>(names);

        string F(params string[] cands)
        {
            foreach (var cand in cands)
                foreach (var n in all)
                    if (string.Equals(n, cand, System.StringComparison.OrdinalIgnoreCase))
                        return n;
            return null;
        }

        string[] M(string pattern)
        {
            var rx = new Regex(pattern, RegexOptions.IgnoreCase);
            var list = new List<string>();
            foreach (var n in all) if (rx.IsMatch(n)) list.Add(n);
            list.Sort();
            return list.ToArray();
        }

        AnimalDefinition.AnimSequence Seq(string start, string[] loops, string end) =>
            new() { start = start, loops = loops, end = end };

        var attacks = new List<string>();
        foreach (var n in M(@"^Attack_")) if (!n.EndsWith("_IP")) attacks.Add(n);

        var extras = new List<string>();
        foreach (var cand in new[] { "Scratching", "Defecate", "Shake", "Sniffing", "Sniff" })
        { var f = F(cand); if (f != null) extras.Add(f); }

        return new AnimalDefinition.AnimalAnimSet
        {
            idles = M(@"^Idle_\d$"),
            extras = extras.ToArray(),
            graze = Seq(F("EatDrink_start"), M(@"^(Eat_?loop_?\d?|EatDig_loop)$"), F("EatDrink_end")),
            grazeWalk = F("Eat_walk_IP"),
            drink = Seq(F("EatDrink_start"), M(@"^Drink_loop$"), F("EatDrink_end")),
            dig = Seq(F("Digging_start"), M(@"^Digging_loop$"), F("Digging_end")),
            digWalk = F("Dig_walk_IP"),
            lie = Seq(F("Lie_start"), M(@"^Lie_loop_?\d$"), F("Lie_end")),
            lieBelly = Seq(F("Lie_belly_start"), M(@"^Lie_belly_loop_?\d$"), F("Lie_belly_end")),
            sit = Seq(F("Sit_start", "Sitting_start"), M(@"^Sit(ting)?_loop_?\d$"), F("Sit_end", "Sitting_end")),
            sleep = Seq(F("Lie_sleep_start"), M(@"^Lie_sleep_loop$"), F("Lie_sleep_end")),
            hide = Seq(F("Hide_start"), M(@"^Hide_loop$"), F("Hide_end")),
            stand = Seq(F("Stand_start"), M(@"^Stand_loop_?\d$"), F("Stand_end")),
            stalkIdle = Seq(F("Crouch_Idle_start"), M(@"^Crouch_Idle_loop_?\d$"), F("Crouch_Idle_end")),
            stalkWalk = F("Crouch_F_IP"),
            howl = F("Howl"),
            eatCarcass = F("Eat_tear"),
            jumpPlace = F("Jump_place_IP", "Jump_Place_IP"),
            attacks = attacks.ToArray(),
            chargeAttack = F("Attack_Run_IP"),
            deaths = M(@"^Death_[LR]$"),
            hits = M(@"^Hit_"),
            runFast = F("RunFast_F_IP"),
        };
    }

    /// <summary>Prefabs LOD (4 níveis) da variante — todas as cores c1..cN.</summary>
    static GameObject[] FindPrefabs(string folder)
    {
        string dir = $"{AssetRoot}/{folder}/Prefabs";
        var lod = new List<GameObject>();
        var any = new List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.Contains("_anim_")) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            if (name.Contains("_LOD")) lod.Add(go);
            else if (!name.Contains("_Low") && !name.Contains("_NoAlpha")) any.Add(go);
        }
        return (lod.Count > 0 ? lod : any).ToArray();
    }

    // ============================================================= ESPÉCIES
    static AnimalDefinition.AnimalRole Role(string folder, RoleKind kind, string name,
        int min, int max, float chance = 1f, float speedMul = 1f,
        float sMin = 0.94f, float sMax = 1.06f)
    {
        if (!built.TryGetValue(folder, out var v))
        {
            Debug.LogWarning($"WildlifeSetup: variante {folder} não construída.");
            return null;
        }
        return new AnimalDefinition.AnimalRole
        {
            name = name,
            kind = kind,
            prefabs = v.prefabs,
            controller = v.controller,
            anims = v.anims,
            count = new Vector2Int(min, max),
            chance = chance,
            speedMul = speedMul,
            scaleRange = new Vector2(sMin, sMax),
        };
    }

    static AnimalDefinition.BiomeAffinity B(Biome b, float minW, float aff) =>
        new() { biome = b, minWeight = minW, affinity = aff };

    static AnimalDefinition NewDef(string file)
    {
        string path = $"{OutDefs}/{file}.asset";
        var d = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(path);
        if (d == null)
        {
            d = ScriptableObject.CreateInstance<AnimalDefinition>();
            AssetDatabase.CreateAsset(d, path);
        }
        return d;
    }

    static void CreateDefinitions()
    {
        // ------------------------------------------------------------ LEBRE
        var d = NewDef("Lebre");
        d.speciesName = "Lebre";
        d.description = "Extremamente comum e assustada. Grandes números, fogem ao menor sinal " +
                        "de perigo e somem no mato (Hide). Filhotes sempre perto dos adultos.";
        d.social = Social.Rebanho;
        d.groupSize = new Vector2Int(4, 8);
        d.memberSpacing = 3f; d.catchUpDistance = 9f;
        d.roles = new[]
        {
            Role("Hares/HareMale", RoleKind.Membro, "Adulta", 2, 6),
            Role("Hares/HareYoung", RoleKind.Filhote, "Filhote", 1, 3, 0.75f, 0.95f, 0.88f, 1f),
        };
        d.biomes = new[]
        {
            B(Biome.Campos, 0.2f, 1f), B(Biome.Floresta, 0.25f, 0.8f),
            B(Biome.Tundra, 0.25f, 0.55f), B(Biome.Deserto, 0.35f, 0.2f),
        };
        d.spawnWeight = 14f; d.maxActiveGroups = 6;
        d.activity = Activity.Madrugada | Activity.Entardecer | Activity.Noite;
        d.walkSpeed = 0.7f; d.trotSpeed = 2.4f; d.runSpeed = 7f; d.turnSpeed = 320f;
        d.bravery = 0.05f; d.aggression = 0f; d.curiosity = 0.15f; d.alertness = 0.9f;
        d.grazing = 0.7f; d.restfulness = 0.35f; d.waterAffinity = 0.15f;
        d.unpredictability = 0f; d.playfulness = 0.8f;
        d.sightRange = 42f; d.fleeDistance = 30f; d.attackRange = 0f;
        d.ignoreBelowGrowth = 0f; d.panicAboveGrowth = 0.5f;
        d.territorial = false; d.migrationChancePerMinute = 0.08f;
        d.migrationDistance = new Vector2(180f, 380f);
        d.health = 14f; d.nutrition = 12f; d.predator = false; d.preySpecies = null;
        EditorUtility.SetDirty(d);

        // ----------------------------------------------------------- RAPOSA
        d = NewDef("Raposa");
        d.speciesName = "Raposa";
        d.description = "Curiosa e ágil: chega perto para observar antes de decidir. Sozinha ou " +
                        "em par; filhotes colados na mãe. Caça lebres em tocaia (Crouch).";
        d.social = Social.Hibrido; d.soloChance = 0.55f;
        d.groupSize = new Vector2Int(1, 2);
        d.memberSpacing = 3.5f; d.catchUpDistance = 10f;
        d.roles = new[]
        {
            Role("Foxes/Fox", RoleKind.Membro, "Adulta", 1, 2),
            Role("Foxes/FoxCub", RoleKind.Filhote, "Filhote", 1, 3, 0.45f, 0.9f, 0.85f, 1f),
        };
        d.biomes = new[]
        {
            B(Biome.Floresta, 0.25f, 1f), B(Biome.Campos, 0.2f, 0.7f),
            B(Biome.Tundra, 0.25f, 0.5f), B(Biome.Deserto, 0.35f, 0.25f),
        };
        d.spawnWeight = 6f; d.maxActiveGroups = 4;
        d.activity = Activity.Entardecer | Activity.Noite | Activity.Madrugada;
        d.walkSpeed = 1f; d.trotSpeed = 3.2f; d.runSpeed = 8.2f; d.turnSpeed = 300f;
        d.bravery = 0.35f; d.aggression = 0.1f; d.curiosity = 0.95f; d.alertness = 0.75f;
        d.grazing = 0.3f; d.restfulness = 0.5f; d.waterAffinity = 0.25f;
        d.unpredictability = 0.15f; d.playfulness = 0.9f;
        d.sightRange = 70f; d.fleeDistance = 24f; d.attackRange = 0f;
        d.meleeRange = 1.6f; d.attackDamage = 4f;
        d.ignoreBelowGrowth = 0f; d.panicAboveGrowth = 0.6f;
        d.migrationChancePerMinute = 0.1f; d.migrationDistance = new Vector2(200f, 450f);
        d.health = 26f; d.nutrition = 20f;
        d.predator = true; d.preySpecies = new[] { "Lebre" }; d.huntChancePerMinute = 0.3f;
        EditorUtility.SetDirty(d);

        // ------------------------------------------------------------ CERVO
        d = NewDef("Cervo");
        d.speciesName = "Cervo";
        d.description = "Tímido, sempre em rebanho: corças pastando (inclusive andando — " +
                        "Eat_walk), um Stag ocasional liderando, filhotes junto das mães. " +
                        "Vigia o horizonte o tempo todo e dispara à menor ameaça.";
        d.social = Social.Rebanho;
        d.groupSize = new Vector2Int(6, 11);
        d.memberSpacing = 4.5f; d.catchUpDistance = 12f;
        d.roles = new[]
        {
            Role("Deers/DeerStag", RoleKind.Lider, "Stag", 1, 1, 0.55f, 1f, 1f, 1.08f),
            Role("Deers/DeerDoe", RoleKind.Membro, "Corça", 4, 8),
            Role("Deers/DeerCalf", RoleKind.Filhote, "Filhote", 1, 3, 0.8f, 0.95f, 0.85f, 1f),
        };
        d.biomes = new[] { B(Biome.Floresta, 0.25f, 1f), B(Biome.Campos, 0.2f, 0.75f) };
        d.spawnWeight = 10f; d.maxActiveGroups = 5;
        d.activity = Activity.Madrugada | Activity.Dia | Activity.Entardecer;
        d.walkSpeed = 1.2f; d.trotSpeed = 3.8f; d.runSpeed = 8.6f; d.turnSpeed = 240f;
        d.bravery = 0.08f; d.aggression = 0.02f; d.curiosity = 0.2f; d.alertness = 0.85f;
        d.grazing = 0.85f; d.restfulness = 0.3f; d.waterAffinity = 0.35f;
        d.playfulness = 0.7f;
        d.sightRange = 75f; d.fleeDistance = 55f; d.attackRange = 0f;
        d.panicAboveGrowth = 0.45f;
        d.migrationChancePerMinute = 0.14f; d.migrationDistance = new Vector2(250f, 550f);
        d.health = 55f; d.nutrition = 45f;
        EditorUtility.SetDirty(d);

        // ------------------------------------------------- CERVO SOLITÁRIO
        d = NewDef("CervoStag");
        d.speciesName = "Cervo";
        d.description = "Stag solitário — bem mais raro e alerta que o rebanho. Defende um " +
                        "pequeno território e pode partir para cima se acuado por um dragão jovem.";
        d.social = Social.Solitario;
        d.groupSize = new Vector2Int(1, 1);
        d.roles = new[] { Role("Deers/DeerStag", RoleKind.Lider, "Stag", 1, 1, 1f, 1f, 1.02f, 1.1f) };
        d.biomes = new[] { B(Biome.Floresta, 0.3f, 0.9f), B(Biome.Campos, 0.25f, 0.6f) };
        d.spawnWeight = 2.2f; d.maxActiveGroups = 3;
        d.activity = Activity.Madrugada | Activity.Dia | Activity.Entardecer;
        d.walkSpeed = 1.25f; d.trotSpeed = 3.8f; d.runSpeed = 8.8f; d.turnSpeed = 230f;
        d.bravery = 0.5f; d.aggression = 0.35f; d.curiosity = 0.3f; d.alertness = 0.8f;
        d.grazing = 0.6f; d.restfulness = 0.3f; d.waterAffinity = 0.35f;
        d.sightRange = 70f; d.fleeDistance = 34f;
        d.attackRange = 12f; d.meleeRange = 2.4f; d.attackDamage = 14f;
        d.ignoreBelowGrowth = 0.12f; d.panicAboveGrowth = 0.6f;
        d.territorial = true; d.territoryRadius = 80f; d.migrationChancePerMinute = 0.02f;
        d.health = 75f; d.nutrition = 55f;
        EditorUtility.SetDirty(d);

        // ------------------------------------------------------- CERVO-REAL
        d = NewDef("CervoReal");
        d.speciesName = "Cervo-Real";
        d.description = "Os grandes rebanhos dos Campos (GDD: grandes herbívoros). Migram longe " +
                        "com frequência — o momento cinematográfico de cruzar um vale inteiro.";
        d.social = Social.Rebanho;
        d.groupSize = new Vector2Int(6, 12);
        d.memberSpacing = 5f; d.catchUpDistance = 14f;
        d.roles = new[]
        {
            Role("RedDeers/RedDeer_Stag", RoleKind.Lider, "Stag", 1, 1, 0.7f, 1f, 1f, 1.08f),
            Role("RedDeers/RedDeer_Doe", RoleKind.Membro, "Corça", 4, 8),
            Role("RedDeers/RedDeer_Calf", RoleKind.Filhote, "Filhote", 2, 4, 0.85f, 0.95f, 0.85f, 1f),
        };
        d.biomes = new[] { B(Biome.Campos, 0.22f, 1f), B(Biome.Floresta, 0.28f, 0.5f) };
        d.spawnWeight = 7f; d.maxActiveGroups = 3;
        d.activity = Activity.Madrugada | Activity.Dia | Activity.Entardecer;
        d.walkSpeed = 1.3f; d.trotSpeed = 4f; d.runSpeed = 9f; d.turnSpeed = 220f;
        d.bravery = 0.15f; d.aggression = 0.05f; d.curiosity = 0.2f; d.alertness = 0.8f;
        d.grazing = 0.85f; d.restfulness = 0.3f; d.waterAffinity = 0.4f; d.playfulness = 0.7f;
        d.sightRange = 80f; d.fleeDistance = 60f; d.panicAboveGrowth = 0.5f;
        d.migrationChancePerMinute = 0.14f; d.migrationDistance = new Vector2(350f, 700f);
        d.health = 85f; d.nutrition = 60f;
        EditorUtility.SetDirty(d);

        // ------------------------------------------------------------- ALCE
        d = NewDef("Alce");
        d.speciesName = "Alce";
        d.description = "Grande, lento e imponente. Ignora dragões pequenos, adora a beira dos " +
                        "lagos (bebe muito) e se torna perigosíssimo quando provocado.";
        d.social = Social.Solitario;
        d.groupSize = new Vector2Int(1, 1);
        d.roles = new[] { Role("Moose/MooseBull", RoleKind.Lider, "Macho", 1, 1, 1f, 1f, 1f, 1.1f) };
        d.biomes = new[]
        {
            B(Biome.Tundra, 0.25f, 1f), B(Biome.Floresta, 0.28f, 0.55f),
            B(Biome.Montanha, 0.3f, 0.3f),
        };
        d.spawnWeight = 2.6f; d.maxActiveGroups = 3;
        d.activity = Activity.Madrugada | Activity.Dia | Activity.Entardecer;
        d.walkSpeed = 1.5f; d.trotSpeed = 3.6f; d.runSpeed = 7.8f; d.turnSpeed = 170f;
        d.bravery = 0.95f; d.aggression = 0.55f; d.curiosity = 0.15f; d.alertness = 0.35f;
        d.grazing = 0.7f; d.restfulness = 0.4f; d.waterAffinity = 0.85f;
        d.sightRange = 55f; d.fleeDistance = 18f;
        d.attackRange = 15f; d.meleeRange = 3.4f; d.attackDamage = 30f;
        d.ignoreBelowGrowth = 0.45f; d.panicAboveGrowth = 0.9f;
        d.migrationChancePerMinute = 0.06f; d.migrationDistance = new Vector2(250f, 500f);
        d.health = 220f; d.nutrition = 110f;
        EditorUtility.SetDirty(d);

        // ---------------------------------------------------- ALCE COM CRIA
        d = NewDef("AlceComCria");
        d.speciesName = "Alce";
        d.description = "Fêmea com o filhote — rara. A mãe é ainda mais agressiva que o macho " +
                        "quando algo se aproxima da cria.";
        d.social = Social.Hibrido; d.soloChance = 0f;
        d.groupSize = new Vector2Int(2, 2);
        d.memberSpacing = 3.5f;
        d.roles = new[]
        {
            Role("Moose/MooseCow", RoleKind.Membro, "Fêmea", 1, 1),
            Role("Moose/MooseCalf", RoleKind.Filhote, "Cria", 1, 1, 1f, 0.9f, 0.9f, 1f),
        };
        d.biomes = new[] { B(Biome.Tundra, 0.25f, 1f), B(Biome.Floresta, 0.3f, 0.5f) };
        d.spawnWeight = 1.2f; d.maxActiveGroups = 2;
        d.activity = Activity.Madrugada | Activity.Dia | Activity.Entardecer;
        d.walkSpeed = 1.5f; d.trotSpeed = 3.6f; d.runSpeed = 7.8f; d.turnSpeed = 175f;
        d.bravery = 0.8f; d.aggression = 0.75f; d.curiosity = 0.1f; d.alertness = 0.6f;
        d.grazing = 0.7f; d.restfulness = 0.35f; d.waterAffinity = 0.8f; d.playfulness = 0.75f;
        d.sightRange = 55f; d.fleeDistance = 22f;
        d.attackRange = 16f; d.meleeRange = 3.2f; d.attackDamage = 26f;
        d.ignoreBelowGrowth = 0.3f; d.panicAboveGrowth = 0.85f;
        d.migrationChancePerMinute = 0.06f;
        d.health = 190f; d.nutrition = 95f;
        EditorUtility.SetDirty(d);

        // ----------------------------------------------------------- JAVALI
        d = NewDef("Javali");
        d.speciesName = "Javali";
        d.description = "Pequenos grupos fuçando o chão (Dig_walk). Imprevisível: ora foge, " +
                        "ora parte para cima — nunca dá para ter certeza.";
        d.social = Social.Hibrido; d.soloChance = 0.3f;
        d.groupSize = new Vector2Int(3, 7);
        d.memberSpacing = 3.5f; d.catchUpDistance = 10f;
        d.roles = new[]
        {
            Role("Boars/BoarMale", RoleKind.Lider, "Macho", 1, 1, 0.8f, 1f, 1f, 1.08f),
            Role("Boars/BoarFemale", RoleKind.Membro, "Fêmea", 2, 4),
            Role("Boars/BoarYoung", RoleKind.Filhote, "Filhote", 0, 4, 0.7f, 0.9f, 0.85f, 1f),
        };
        d.biomes = new[] { B(Biome.Floresta, 0.25f, 1f), B(Biome.Campos, 0.25f, 0.6f) };
        d.spawnWeight = 6.5f; d.maxActiveGroups = 5;
        d.activity = Activity.Entardecer | Activity.Noite;
        d.walkSpeed = 1.1f; d.trotSpeed = 3.3f; d.runSpeed = 8f; d.turnSpeed = 260f;
        d.bravery = 0.5f; d.aggression = 0.45f; d.curiosity = 0.3f; d.alertness = 0.6f;
        d.grazing = 0.8f; d.restfulness = 0.35f; d.waterAffinity = 0.3f;
        d.unpredictability = 0.85f; d.playfulness = 0.7f;
        d.sightRange = 55f; d.fleeDistance = 28f;
        d.attackRange = 11f; d.meleeRange = 2.2f; d.attackDamage = 16f;
        d.ignoreBelowGrowth = 0.1f; d.panicAboveGrowth = 0.7f;
        d.migrationChancePerMinute = 0.1f; d.migrationDistance = new Vector2(200f, 450f);
        d.health = 80f; d.nutrition = 55f;
        EditorUtility.SetDirty(d);

        // ------------------------------------------------------------- URSO
        d = NewDef("Urso");
        d.speciesName = "Urso";
        d.description = "Predador territorial raro. Vaga sozinho, investiga barulhos, levanta " +
                        "em duas patas para intimidar e decide se ignora ou ataca.";
        d.social = Social.Solitario;
        d.groupSize = new Vector2Int(1, 1);
        d.roles = new[] { Role("Bears/BearMale", RoleKind.Lider, "Macho", 1, 1, 1f, 1f, 1f, 1.1f) };
        d.biomes = new[]
        {
            B(Biome.Floresta, 0.3f, 1f), B(Biome.Montanha, 0.3f, 0.6f),
            B(Biome.Tundra, 0.3f, 0.35f),
        };
        d.spawnWeight = 1.8f; d.maxActiveGroups = 2;
        d.activity = Activity.Dia | Activity.Entardecer;
        d.walkSpeed = 1.25f; d.trotSpeed = 3.4f; d.runSpeed = 8.4f; d.turnSpeed = 200f;
        d.bravery = 0.95f; d.aggression = 0.6f; d.curiosity = 0.75f; d.alertness = 0.5f;
        d.grazing = 0.6f; d.restfulness = 0.5f; d.waterAffinity = 0.6f;
        d.sightRange = 70f; d.fleeDistance = 15f;
        d.attackRange = 18f; d.meleeRange = 3f; d.attackDamage = 34f;
        d.ignoreBelowGrowth = 0.3f; d.panicAboveGrowth = 0.95f;
        d.territorial = true; d.territoryRadius = 140f; d.migrationChancePerMinute = 0f;
        d.health = 240f; d.nutrition = 105f;
        d.predator = true; d.preySpecies = new[] { "Lebre" }; d.huntChancePerMinute = 0.06f;
        EditorUtility.SetDirty(d);

        // ------------------------------------------------ URSA COM FILHOTES
        d = NewDef("UrsaComFilhotes");
        d.speciesName = "Urso";
        d.description = "Raríssima: a fêmea com 1–2 filhotes brincalhões. Aproximar-se deles é " +
                        "a pior ideia do mundo.";
        d.social = Social.Hibrido; d.soloChance = 0f;
        d.groupSize = new Vector2Int(2, 3);
        d.memberSpacing = 3f;
        d.roles = new[]
        {
            Role("Bears/BearFemale", RoleKind.Membro, "Fêmea", 1, 1),
            Role("Bears/BearCub", RoleKind.Filhote, "Filhote", 1, 2, 1f, 0.88f, 0.85f, 1f),
        };
        d.biomes = new[] { B(Biome.Floresta, 0.3f, 1f), B(Biome.Montanha, 0.3f, 0.5f) };
        d.spawnWeight = 0.9f; d.maxActiveGroups = 1;
        d.activity = Activity.Dia | Activity.Entardecer;
        d.walkSpeed = 1.25f; d.trotSpeed = 3.4f; d.runSpeed = 8.2f; d.turnSpeed = 200f;
        d.bravery = 0.95f; d.aggression = 0.9f; d.curiosity = 0.5f; d.alertness = 0.7f;
        d.grazing = 0.6f; d.restfulness = 0.45f; d.waterAffinity = 0.6f; d.playfulness = 0.95f;
        d.sightRange = 70f; d.fleeDistance = 15f;
        d.attackRange = 22f; d.meleeRange = 3f; d.attackDamage = 30f;
        d.ignoreBelowGrowth = 0.15f; d.panicAboveGrowth = 0.95f;
        d.territorial = true; d.territoryRadius = 100f; d.migrationChancePerMinute = 0f;
        d.health = 210f; d.nutrition = 95f;
        EditorUtility.SetDirty(d);

        // ------------------------------------------------------------- LOBO
        d = NewDef("Lobo");
        d.speciesName = "Lobo";
        d.description = "Alcateia organizada: líder, adultos e filhotes. Patrulham territórios " +
                        "enormes (migração frequente), UIVAM em coro e caçam cervos e lebres " +
                        "de verdade — as carcaças ficam no mundo (e podem ser roubadas!).";
        d.social = Social.Alcateia;
        d.groupSize = new Vector2Int(3, 6);
        d.memberSpacing = 4f; d.catchUpDistance = 12f;
        d.roles = new[]
        {
            Role("Wolfes/WolfMale", RoleKind.Lider, "Alfa", 1, 1, 1f, 1f, 1.02f, 1.1f),
            Role("Wolfes/WolfMale", RoleKind.Membro, "Adulto", 2, 4),
            Role("Wolfes/WolfCub", RoleKind.Filhote, "Filhote", 0, 2, 0.55f, 0.9f, 0.85f, 1f),
        };
        d.biomes = new[]
        {
            B(Biome.Tundra, 0.25f, 1f), B(Biome.Floresta, 0.28f, 0.9f),
            B(Biome.Montanha, 0.3f, 0.5f),
        };
        d.spawnWeight = 4.2f; d.maxActiveGroups = 3;
        d.activity = Activity.Entardecer | Activity.Noite | Activity.Madrugada;
        d.walkSpeed = 1.2f; d.trotSpeed = 3.7f; d.runSpeed = 9f; d.turnSpeed = 280f;
        d.bravery = 0.75f; d.aggression = 0.5f; d.curiosity = 0.5f; d.alertness = 0.8f;
        d.grazing = 0.1f; d.restfulness = 0.45f; d.waterAffinity = 0.3f; d.playfulness = 0.9f;
        d.sightRange = 85f; d.fleeDistance = 20f;
        d.attackRange = 14f; d.meleeRange = 2f; d.attackDamage = 12f;
        d.ignoreBelowGrowth = 0.2f; d.panicAboveGrowth = 0.6f;
        d.migrationChancePerMinute = 0.16f; d.migrationDistance = new Vector2(300f, 650f);
        d.health = 70f; d.nutrition = 40f;
        d.predator = true; d.preySpecies = new[] { "Cervo", "Cervo-Real", "Lebre" };
        d.huntChancePerMinute = 0.3f;
        EditorUtility.SetDirty(d);

        // ================================================ POPULAÇÕES DO DESERTO
        // O deserto é duro, mas NÃO é morto: vida esparsa, concentrada nos oásis
        // (o bônus de água do spawner faz as margens dos oásis fervilharem).

        // -------------------------------------------------- LEBRE DO DESERTO
        d = NewDef("LebreDoDeserto");
        d.speciesName = "Lebre";
        d.description = "População do deserto: grupos menores, sempre em movimento atrás de " +
                        "comida escassa. A base da cadeia alimentar entre as rochas.";
        d.social = Social.Rebanho;
        d.groupSize = new Vector2Int(2, 4);
        d.memberSpacing = 3f; d.catchUpDistance = 9f;
        d.roles = new[]
        {
            Role("Hares/HareMale", RoleKind.Membro, "Adulta", 2, 3),
            Role("Hares/HareYoung", RoleKind.Filhote, "Filhote", 0, 2, 0.6f, 0.95f, 0.88f, 1f),
        };
        d.biomes = new[] { B(Biome.Deserto, 0.3f, 1f) };
        d.spawnWeight = 8f; d.maxActiveGroups = 4;
        d.activity = Activity.Madrugada | Activity.Entardecer | Activity.Noite;
        d.walkSpeed = 0.7f; d.trotSpeed = 2.4f; d.runSpeed = 7f; d.turnSpeed = 320f;
        d.bravery = 0.05f; d.aggression = 0f; d.curiosity = 0.15f; d.alertness = 0.9f;
        d.grazing = 0.5f; d.restfulness = 0.3f; d.waterAffinity = 0.5f;
        d.playfulness = 0.8f;
        d.sightRange = 42f; d.fleeDistance = 30f; d.attackRange = 0f;
        d.panicAboveGrowth = 0.5f;
        d.migrationChancePerMinute = 0.1f; d.migrationDistance = new Vector2(200f, 420f);
        d.health = 14f; d.nutrition = 12f;
        EditorUtility.SetDirty(d);

        // ------------------------------------------------- RAPOSA DO DESERTO
        d = NewDef("RaposaDoDeserto");
        d.speciesName = "Raposa";
        d.description = "Quase sempre sozinha, faminta e ainda mais curiosa. Ronda os oásis " +
                        "caçando lebres na tocaia.";
        d.social = Social.Hibrido; d.soloChance = 0.75f;
        d.groupSize = new Vector2Int(1, 2);
        d.memberSpacing = 3.5f;
        d.roles = new[]
        {
            Role("Foxes/Fox", RoleKind.Membro, "Adulta", 1, 2),
            Role("Foxes/FoxCub", RoleKind.Filhote, "Filhote", 0, 2, 0.3f, 0.9f, 0.85f, 1f),
        };
        d.biomes = new[] { B(Biome.Deserto, 0.3f, 1f) };
        d.spawnWeight = 5f; d.maxActiveGroups = 3;
        d.activity = Activity.Entardecer | Activity.Noite | Activity.Madrugada;
        d.walkSpeed = 1f; d.trotSpeed = 3.2f; d.runSpeed = 8.2f; d.turnSpeed = 300f;
        d.bravery = 0.4f; d.aggression = 0.1f; d.curiosity = 0.95f; d.alertness = 0.8f;
        d.grazing = 0.2f; d.restfulness = 0.45f; d.waterAffinity = 0.55f;
        d.unpredictability = 0.15f; d.playfulness = 0.9f;
        d.sightRange = 70f; d.fleeDistance = 24f; d.meleeRange = 1.6f; d.attackDamage = 4f;
        d.panicAboveGrowth = 0.6f;
        d.migrationChancePerMinute = 0.1f; d.migrationDistance = new Vector2(220f, 480f);
        d.health = 26f; d.nutrition = 20f;
        d.predator = true; d.preySpecies = new[] { "Lebre" }; d.huntChancePerMinute = 0.35f;
        EditorUtility.SetDirty(d);

        // ------------------------------------------------- JAVALI DO DESERTO
        d = NewDef("JavaliDoDeserto");
        d.speciesName = "Javali";
        d.description = "Grupos pequenos fuçando raízes entre as rochas, sempre por perto de um " +
                        "oásis. Tão imprevisível quanto o parente da floresta — talvez mais.";
        d.social = Social.Hibrido; d.soloChance = 0.35f;
        d.groupSize = new Vector2Int(2, 4);
        d.memberSpacing = 3.5f; d.catchUpDistance = 10f;
        d.roles = new[]
        {
            Role("Boars/BoarMale", RoleKind.Lider, "Macho", 1, 1, 0.8f, 1f, 1f, 1.08f),
            Role("Boars/BoarFemale", RoleKind.Membro, "Fêmea", 1, 2),
            Role("Boars/BoarYoung", RoleKind.Filhote, "Filhote", 0, 2, 0.5f, 0.9f, 0.85f, 1f),
        };
        d.biomes = new[] { B(Biome.Deserto, 0.35f, 0.8f) };
        d.spawnWeight = 3f; d.maxActiveGroups = 3;
        d.activity = Activity.Entardecer | Activity.Noite;
        d.walkSpeed = 1.1f; d.trotSpeed = 3.3f; d.runSpeed = 8f; d.turnSpeed = 260f;
        d.bravery = 0.55f; d.aggression = 0.5f; d.curiosity = 0.3f; d.alertness = 0.6f;
        d.grazing = 0.85f; d.restfulness = 0.3f; d.waterAffinity = 0.65f;
        d.unpredictability = 0.9f; d.playfulness = 0.7f;
        d.sightRange = 55f; d.fleeDistance = 28f;
        d.attackRange = 11f; d.meleeRange = 2.2f; d.attackDamage = 16f;
        d.ignoreBelowGrowth = 0.1f; d.panicAboveGrowth = 0.7f;
        d.migrationChancePerMinute = 0.08f; d.migrationDistance = new Vector2(180f, 400f);
        d.health = 80f; d.nutrition = 55f;
        EditorUtility.SetDirty(d);

        // --------------------------------------------------- LOBO DO DESERTO
        d = NewDef("LoboDoDeserto");
        d.speciesName = "Lobo";
        d.description = "Alcateias pequenas e nômades que cruzam grandes distâncias entre um " +
                        "oásis e outro, caçando lebres e javalis. O grande predador do deserto.";
        d.social = Social.Alcateia;
        d.groupSize = new Vector2Int(2, 4);
        d.memberSpacing = 4f; d.catchUpDistance = 12f;
        d.roles = new[]
        {
            Role("Wolfes/WolfMale", RoleKind.Lider, "Alfa", 1, 1, 1f, 1f, 1.02f, 1.1f),
            Role("Wolfes/WolfMale", RoleKind.Membro, "Adulto", 1, 3),
            Role("Wolfes/WolfCub", RoleKind.Filhote, "Filhote", 0, 1, 0.35f, 0.9f, 0.85f, 1f),
        };
        d.biomes = new[] { B(Biome.Deserto, 0.35f, 0.7f) };
        d.spawnWeight = 2f; d.maxActiveGroups = 2;
        d.activity = Activity.Entardecer | Activity.Noite | Activity.Madrugada;
        d.walkSpeed = 1.2f; d.trotSpeed = 3.7f; d.runSpeed = 9f; d.turnSpeed = 280f;
        d.bravery = 0.75f; d.aggression = 0.5f; d.curiosity = 0.5f; d.alertness = 0.8f;
        d.grazing = 0.1f; d.restfulness = 0.4f; d.waterAffinity = 0.55f; d.playfulness = 0.9f;
        d.sightRange = 85f; d.fleeDistance = 20f;
        d.attackRange = 14f; d.meleeRange = 2f; d.attackDamage = 12f;
        d.ignoreBelowGrowth = 0.2f; d.panicAboveGrowth = 0.6f;
        d.migrationChancePerMinute = 0.2f; d.migrationDistance = new Vector2(350f, 700f);
        d.health = 70f; d.nutrition = 40f;
        d.predator = true; d.preySpecies = new[] { "Lebre", "Javali" };
        d.huntChancePerMinute = 0.35f;
        EditorUtility.SetDirty(d);
    }

    // -------------------------------------------------------------- UTILS
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }
}
