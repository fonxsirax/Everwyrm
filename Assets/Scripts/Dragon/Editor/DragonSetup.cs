using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Setup automático do dragão jogável.
/// Menu: Tools > Dragão > Setup Completo
///  1. Gera "Assets/Dragao/Dragon Player.controller" usando o máximo possível dos
///     clips do Unka: locomoção, voo, planar, ataques, fogo, rugido, esquivas,
///     reações de dano, sono, estol/queda e mortes.
///  2. Configura o prefab "Unka Realistic": CharacterController, DragonController,
///     DragonVitals, root motion OFF.
///  3. Liga a DragonCamera na Main Camera da cena aberta.
/// </summary>
public static class DragonSetup
{
    const string AnimDir = "Assets/Malbers Animations/Dragons/4 - Unka the Dragon/Animations/";
    const string PrefabPath = "Assets/Prefabs/Unka Realistic.prefab";
    const string OutDir = "Assets/Dragao";
    const string ControllerPath = OutDir + "/Dragon Player.controller";

    public static void SetupAll()
    {
        var controller = BuildAnimator();
        ConfigurePrefab(controller);
        WireScene();
        AssetDatabase.SaveAssets();
        Debug.Log("<b>Dragão pronto!</b> WASD anda · Space decola · LMB/Q/E ataca · F fogo · T ruge · R dorme · Alt esquiva.");
    }

    /// <summary>Regenera animator + prefab sem tocar na cena aberta (auto-setup).</summary>
    public static void RegenerateAnimatorAndPrefab()
    {
        ConfigurePrefab(BuildAnimator());
        AssetDatabase.SaveAssets();
        Debug.Log("<b>Dragão atualizado:</b> Animator regenerado com os estados novos.");
    }

    // ------------------------------------------------------------- ANIMATOR
    public static AnimatorController BuildAnimator()
    {
        cache.Clear();
        if (!AssetDatabase.IsValidFolder(OutDir))
            AssetDatabase.CreateFolder("Assets", "Dragao");
        AssetDatabase.DeleteAsset(ControllerPath);

        var c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        foreach (var p in new[] { "Speed", "Turn", "Vertical", "IdleVar", "DodgeDir", "HitVar", "DeathVar" })
            c.AddParameter(p, AnimatorControllerParameterType.Float);
        foreach (var p in new[] { "Flying", "Glide", "Rest", "Falling", "Swimming" })
            c.AddParameter(p, AnimatorControllerParameterType.Bool);
        foreach (var p in new[] { "Flap", "Roar", "Attack", "Fire", "Dodge", "Hit", "Die" })
            c.AddParameter(p, AnimatorControllerParameterType.Trigger);
        c.AddParameter("AttackType", AnimatorControllerParameterType.Int);

        var sm = c.layers[0].stateMachine;

        // ---- Idles variados (árvore aninhada dentro da locomoção)
        var idleTree = Tree1D(c, "IdleTree", "IdleVar", new[]
        {
            (Clip("Unka Idle.FBX", "UIdle 01"), 0f),
            (Clip("Unka Idle.FBX", "UIdle 02"), 1f),
            (Clip("Unka Idle.FBX", "UIdle 03"), 2f),
        });

        // ---- Chão: blend 2D (Turn x Speed)
        var loco = sm.AddState("Locomotion");
        var locoTree = Tree2D(c, "LocomotionTree", "Turn", "Speed", new (Motion, Vector2)[]
        {
            (idleTree,                               new Vector2( 0, 0)),
            (Clip("Unka Turn.FBX", "UTurn Left"),    new Vector2(-1, 0)),
            (Clip("Unka Turn.FBX", "UTurn Right"),   new Vector2( 1, 0)),
            (Clip("Unka WalkBack.FBX", "UWalBack"),  new Vector2( 0,-1)),
            (Clip("Unka Walk.FBX", "UWalk"),         new Vector2( 0, 1)),
            (Clip("Unka Walk.FBX", "UWalk Left"),    new Vector2(-1, 1)),
            (Clip("Unka Walk.FBX", "UWalk Right"),   new Vector2( 1, 1)),
            (Clip("Unka Trot.FBX", "UTrot"),         new Vector2( 0, 2)),
            (Clip("Unka Trot.FBX", "UTrot Left"),    new Vector2(-1, 2)),
            (Clip("Unka Trot.FBX", "UTrot Right"),   new Vector2( 1, 2)),
            (Clip("Unka Run.FBX", "URun"),           new Vector2( 0, 3)),
            (Clip("Unka Run.FBX", "URun Left"),      new Vector2(-1, 3)),
            (Clip("Unka Run.FBX", "URun Right"),     new Vector2( 1, 3)),
        });
        loco.motion = locoTree;
        sm.defaultState = loco;

        // ---- Batida de asas parado / decolagem
        var flap = State(sm, "Flap Ground", Clip("Unka Fly InPlace.FBX", "UPFly Stand"));
        var takeoff = State(sm, "TakeOff", Clip("Unka Jump.FBX", "UJump Up"));

        // ---- Voo batendo asas: blend 2D (Turn x Vertical)
        var fly = sm.AddState("Fly");
        fly.motion = Tree2D(c, "FlyTree", "Turn", "Vertical", new (Motion, Vector2)[]
        {
            (Clip("Unka Fly InPlace.FBX", "UPFly"),                new Vector2( 0, 0)),
            (Clip("Unka Fly InPlace.FBX", "UPFly L"),              new Vector2(-1, 0)),
            (Clip("Unka Fly InPlace.FBX", "UPFly R"),              new Vector2( 1, 0)),
            (Clip("Unka Fly InPlace.FBX", "UPFly Up Forward"),     new Vector2( 0, 1)),
            (Clip("Unka Fly InPlace.FBX", "UPFly Up Forward L"),   new Vector2(-1, 1)),
            (Clip("Unka Fly InPlace.FBX", "UPFly Up Forwad R"),    new Vector2( 1, 1)), // typo do asset
            (Clip("Unka Fly InPlace.FBX", "UPFly Down"),           new Vector2( 0,-1)),
            (Clip("Unka Fly InPlace.FBX", "UPFly Down Forward L"), new Vector2(-1,-1)),
            (Clip("Unka Fly InPlace.FBX", "UPFly Down Forward R"), new Vector2( 1,-1)),
        });

        // ---- Planar: blend 1D (Turn)
        var glide = sm.AddState("Glide");
        glide.motion = Tree1D(c, "GlideTree", "Turn", new[]
        {
            (Clip("Unka Fly InPlace.FBX", "UPGlide L"), -1f),
            (Clip("Unka Fly InPlace.FBX", "UPGlide"), 0f),
            (Clip("Unka Fly InPlace.FBX", "UPGlide R"), 1f),
        });

        // ---- Estol (queda sem energia) e pouso
        var fall = State(sm, "Stall Fall", Clip("Unka Fly InPlace.FBX", "UPFall High"));
        var land = State(sm, "Land", Clip("Unka Fly InPlace.FBX", "UPFall Land"));

        // ---- Natação: blend 2D (Turn x Speed) — Speed 2 = nado rápido
        var swim = sm.AddState("Swim");
        swim.motion = Tree2D(c, "SwimTree", "Turn", "Speed", new (Motion, Vector2)[]
        {
            (Clip("Unka Swim InPlace.FBX", "UPSwim Idle"),       new Vector2( 0, 0)),
            (Clip("Unka Swim InPlace.FBX", "UPSwim Turn Left"),  new Vector2(-1, 0)),
            (Clip("Unka Swim InPlace.FBX", "UPSwim Turn Right"), new Vector2( 1, 0)),
            (Clip("Unka Swim InPlace.FBX", "UPSwim Back"),       new Vector2( 0,-1)),
            (Clip("Unka Swim InPlace.FBX", "UPSwim Forward"),    new Vector2( 0, 1)),
            (Clip("Unka Swim InPlace.FBX", "UPSwim Fast"),       new Vector2( 0, 2)),
        });

        // ---- Dormir
        var restStart = State(sm, "Rest Start", Clip("Unka Sleep.FBX", "USleep Start"));
        var restIdle = State(sm, "Rest Idle", Clip("Unka Sleep.FBX", "USleep Idle"));
        var restEnd = State(sm, "Rest End", Clip("Unka Sleep.FBX", "USleep End"));

        // ---- Rugido e fogo
        var roar = State(sm, "Roar", Clip("Unka GetHitOpp.FBX", "U ROAR"));
        var fire = State(sm, "Fire Breath", Clip("Unka Attack.FBX", "UAttack FireBreath L"));

        // ---- Ataques corpo a corpo (AttackType 0-7)
        string[] attackClips =
        {
            "UAttack Bite Front", "UAttack Claws L", "UAttack Claws R", "UAttack Bite 2",
            "UAttack Tail L", "UAttack Tail R", "UAttack Wing L", "UAttack Wings R"
        };
        var attacks = new AnimatorState[attackClips.Length];
        for (int i = 0; i < attackClips.Length; i++)
            attacks[i] = State(sm, attackClips[i], Clip("Unka Attack.FBX", attackClips[i]));

        // ---- Esquivas (chão e ar)
        var dodgeL = State(sm, "Dodge L", Clip("Unka Dodge.FBX", "UGround Dodge Left"));
        var dodgeR = State(sm, "Dodge R", Clip("Unka Dodge.FBX", "UGround Dodge Right"));
        var flyDodgeL = State(sm, "Fly Dodge L", Clip("Unka Fly InPlace.FBX", "UPFly Dodge L"));
        var flyDodgeR = State(sm, "Fly Dodge R", Clip("Unka Fly InPlace.FBX", "UPFly Dodge R"));

        // ---- Reação de dano (blend das 4 direções)
        var hit = sm.AddState("Get Hit");
        hit.motion = Tree1D(c, "HitTree", "HitVar", new[]
        {
            (Clip("Unka GetHit.FBX", "UGetHit Left"), 0f),
            (Clip("Unka GetHit.FBX", "UGetHit Front Left 2"), 1f),
            (Clip("Unka GetHit.FBX", "UGetHit Front Right 2"), 2f),
            (Clip("Unka GetHit.FBX", "UGetHit Back Left"), 3f),
        });

        // ---- Morte (2 variações)
        var death = sm.AddState("Death");
        death.motion = Tree1D(c, "DeathTree", "DeathVar", new[]
        {
            (Clip("Unka Death.FBX", "UDeath Left"), 0f),
            (Clip("Unka Death.FBX", "UDeath Dramatic Right"), 1f),
        });

        // ================================================ TRANSIÇÕES
        // decolagem / voo / pouso
        Cond(loco.AddTransition(takeoff), 0.12f, (AnimatorConditionMode.If, "Flying"));
        Cond(loco.AddTransition(flap), 0.08f, (AnimatorConditionMode.If, "Flap"));
        ExitTime(flap.AddTransition(loco), 0.8f, 0.2f);
        Cond(flap.AddTransition(takeoff), 0.1f, (AnimatorConditionMode.If, "Flying"));
        ExitTime(takeoff.AddTransition(fly), 0.55f, 0.3f);
        Cond(takeoff.AddTransition(land), 0.15f, (AnimatorConditionMode.IfNot, "Flying"));
        Cond(fly.AddTransition(glide), 0.45f, (AnimatorConditionMode.If, "Glide"));
        Cond(glide.AddTransition(fly), 0.3f, (AnimatorConditionMode.IfNot, "Glide"));
        Cond(fly.AddTransition(land), 0.15f, (AnimatorConditionMode.IfNot, "Flying"));
        Cond(glide.AddTransition(land), 0.15f, (AnimatorConditionMode.IfNot, "Flying"));
        ExitTime(land.AddTransition(loco), 0.65f, 0.25f);

        // natação: entra do chão (andou pra dentro do lago) ou caindo/pousando na água
        Cond(loco.AddTransition(swim), 0.3f, (AnimatorConditionMode.If, "Swimming"));
        Cond(swim.AddTransition(loco), 0.3f,
            (AnimatorConditionMode.IfNot, "Swimming"), (AnimatorConditionMode.IfNot, "Flying"));
        Cond(swim.AddTransition(takeoff), 0.12f, (AnimatorConditionMode.If, "Flying"));
        Cond(fly.AddTransition(swim), 0.25f, (AnimatorConditionMode.If, "Swimming"));
        Cond(glide.AddTransition(swim), 0.25f, (AnimatorConditionMode.If, "Swimming"));
        Cond(fall.AddTransition(swim), 0.2f, (AnimatorConditionMode.If, "Swimming"));

        // estol
        Cond(fly.AddTransition(fall), 0.3f, (AnimatorConditionMode.If, "Falling"));
        Cond(glide.AddTransition(fall), 0.3f, (AnimatorConditionMode.If, "Falling"));
        Cond(fall.AddTransition(land), 0.12f, (AnimatorConditionMode.IfNot, "Flying"));
        Cond(fall.AddTransition(fly), 0.3f,
            (AnimatorConditionMode.IfNot, "Falling"), (AnimatorConditionMode.If, "Flying"));

        // dormir
        Cond(loco.AddTransition(restStart), 0.25f, (AnimatorConditionMode.If, "Rest"));
        ExitTime(restStart.AddTransition(restIdle), 0.9f, 0.15f);
        Cond(restIdle.AddTransition(restEnd), 0.2f, (AnimatorConditionMode.IfNot, "Rest"));
        ExitTime(restEnd.AddTransition(loco), 0.8f, 0.2f);

        // ações no chão — a saída volta para o Fly se "Flying" estiver ligado
        // (habilidades 1-4 do DragonAbilities entram por CrossFade também em voo)
        Cond(loco.AddTransition(roar), 0.15f, (AnimatorConditionMode.If, "Roar"));
        ExitToFlyOrLoco(roar, fly, loco, 0.9f, 0.25f);
        Cond(loco.AddTransition(fire), 0.1f, (AnimatorConditionMode.If, "Fire"));
        ExitToFlyOrLoco(fire, fly, loco, 0.9f, 0.25f);
        Cond(loco.AddTransition(hit), 0.1f, (AnimatorConditionMode.If, "Hit"));
        ExitTime(hit.AddTransition(loco), 0.85f, 0.2f);

        for (int i = 0; i < attacks.Length; i++)
        {
            var t = loco.AddTransition(attacks[i]);
            Cond(t, 0.1f, (AnimatorConditionMode.If, "Attack"));
            t.AddCondition(AnimatorConditionMode.Equals, i, "AttackType");
            ExitToFlyOrLoco(attacks[i], fly, loco, 0.85f, 0.2f);
        }

        // esquivas
        Cond(loco.AddTransition(dodgeL), 0.08f,
            (AnimatorConditionMode.If, "Dodge"), (AnimatorConditionMode.Less, "DodgeDir"));
        Cond(loco.AddTransition(dodgeR), 0.08f,
            (AnimatorConditionMode.If, "Dodge"), (AnimatorConditionMode.Greater, "DodgeDir"));
        ExitTime(dodgeL.AddTransition(loco), 0.8f, 0.15f);
        ExitTime(dodgeR.AddTransition(loco), 0.8f, 0.15f);
        foreach (var src in new[] { fly, glide })
        {
            Cond(src.AddTransition(flyDodgeL), 0.1f,
                (AnimatorConditionMode.If, "Dodge"), (AnimatorConditionMode.Less, "DodgeDir"));
            Cond(src.AddTransition(flyDodgeR), 0.1f,
                (AnimatorConditionMode.If, "Dodge"), (AnimatorConditionMode.Greater, "DodgeDir"));
        }
        ExitTime(flyDodgeL.AddTransition(fly), 0.85f, 0.2f);
        ExitTime(flyDodgeR.AddTransition(fly), 0.85f, 0.2f);

        // morte (de qualquer estado)
        var dieT = sm.AddAnyStateTransition(death);
        dieT.canTransitionToSelf = false;
        Cond(dieT, 0.2f, (AnimatorConditionMode.If, "Die"));

        EditorUtility.SetDirty(c);
        Debug.Log("Animator gerado em " + ControllerPath);
        return c;
    }

    // -------------------------------------------------------------- PREFAB
    static void ConfigurePrefab(AnimatorController controller)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var animator = root.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            var cc = root.GetComponent<CharacterController>();
            if (cc == null) cc = root.AddComponent<CharacterController>();

            Bounds b = new Bounds(root.transform.position, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                b.Encapsulate(r.bounds);
            float height = Mathf.Clamp(b.size.y, 1.5f, 6f);
            cc.height = height;
            cc.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.28f, 0.4f, height * 0.45f);
            cc.center = new Vector3(0f, height * 0.55f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = Mathf.Min(0.6f, height * 0.25f);

            if (root.GetComponent<DragonController>() == null)
                root.AddComponent<DragonController>();
            if (root.GetComponent<DragonVitals>() == null)
                root.AddComponent<DragonVitals>();
            if (root.GetComponent<DragonGrowth>() == null)
                root.AddComponent<DragonGrowth>();
            if (root.GetComponent<DragonAttributes>() == null)
                root.AddComponent<DragonAttributes>();
            if (root.GetComponent<DragonFlight>() == null)
                root.AddComponent<DragonFlight>();
            if (root.GetComponent<DragonSounds>() == null)
                root.AddComponent<DragonSounds>();
            if (root.GetComponent<DragonAbilities>() == null)
                root.AddComponent<DragonAbilities>();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("Prefab configurado: CharacterController + DragonController + DragonVitals + DragonGrowth.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // --------------------------------------------------------------- CENA
    static void WireScene()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("Nenhuma Main Camera na cena aberta — adicione DragonCamera manualmente.");
            return;
        }
        var dc = cam.GetComponent<DragonCamera>();
        if (dc == null) dc = Undo.AddComponent<DragonCamera>(cam.gameObject);

        var dragon = Object.FindFirstObjectByType<DragonController>();
        if (dragon != null) dc.target = dragon.transform;
        else
        {
            var go = GameObject.Find("Unka Realistic");
            if (go != null) dc.target = go.transform;
            else Debug.LogWarning("Arraste o prefab 'Unka Realistic' para a cena e defina o Target da DragonCamera.");
        }
        // comida no mundo (placeholder até o sistema de caça)
        var spawner = Object.FindFirstObjectByType<FoodSpawner>();
        if (spawner == null)
            spawner = new GameObject("Food Spawner").AddComponent<FoodSpawner>();
        if (dragon != null) spawner.target = dragon.transform;

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Câmera e Food Spawner configurados.");
    }

    // --------------------------------------------------------------- MUNDO
    [MenuItem("Tools/Dragão/3 - Mundo Infinito na Cena")]
    public static void SetupWorld()
    {
        // desativa terrains fixos existentes (o mundo agora é gerado)
        int off = 0;
        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            if (t.GetComponentInParent<InfiniteTerrain>() == null)
            {
                Undo.RecordObject(t.gameObject, "Disable fixed terrain");
                t.gameObject.SetActive(false);
                off++;
            }

        var world = Object.FindFirstObjectByType<InfiniteTerrain>();
        if (world == null)
            world = new GameObject("World (Infinite Terrain)").AddComponent<InfiniteTerrain>();
        if (world.GetComponent<MountainUpdrafts>() == null)
            world.gameObject.AddComponent<MountainUpdrafts>();   // correntes nas montanhas

        var dragon = Object.FindFirstObjectByType<DragonController>();
        if (dragon != null) world.player = dragon.transform;

        // texturas, árvores, arbustos e graminhas são auto-preenchidos pelo
        // próprio InfiniteTerrain (OnValidate) — nada a fazer aqui.

        // Wind Zone — faz as árvores/arbustos balançarem como na cena original
        if (Object.FindFirstObjectByType<WindZone>() == null)
        {
            var wind = new GameObject("Wind Zone").AddComponent<WindZone>();
            wind.mode = WindZoneMode.Directional;
            wind.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            wind.windMain = 0.6f;
            wind.windTurbulence = 0.3f;
            wind.windPulseMagnitude = 0.5f;
            wind.windPulseFrequency = 0.02f;
            Undo.RegisterCreatedObjectUndo(wind.gameObject, "Create Wind Zone");
        }

        // garante o auto-preenchimento dos assets ALP no componente
        world.SendMessage("OnValidate", SendMessageOptions.DontRequireReceiver);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Mundo infinito pronto: {off} terrain(s) fixo(s) desativado(s), " +
                  $"texturas/vegetação ALP atribuídas, Wind Zone ok. Dê Play e explore!");
    }

    // ------------------------------------------------------------- HELPERS
    static readonly Dictionary<string, AnimationClip[]> cache = new();

    static AnimationClip Clip(string fbx, string clipName)
    {
        if (!cache.TryGetValue(fbx, out var clips))
        {
            var list = new List<AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AnimDir + fbx))
                if (o is AnimationClip a && !a.name.StartsWith("__preview"))
                    list.Add(a);
            cache[fbx] = clips = list.ToArray();
        }
        foreach (var a in clips) if (a.name == clipName) return a;
        Debug.LogError($"Clip '{clipName}' não encontrado em {fbx}");
        return null;
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion)
    {
        var s = sm.AddState(name);
        s.motion = motion;
        return s;
    }

    static BlendTree Tree2D(AnimatorController c, string name, string px, string py,
                            (Motion motion, Vector2 pos)[] motions)
    {
        var t = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.FreeformCartesian2D,
            blendParameter = px,
            blendParameterY = py,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(t, c);
        foreach (var (m, pos) in motions)
            if (m != null) t.AddChild(m, pos);
        return t;
    }

    static BlendTree Tree1D(AnimatorController c, string name, string param,
                            (AnimationClip clip, float threshold)[] motions)
    {
        var t = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = param,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(t, c);
        foreach (var (clip, th) in motions)
            if (clip != null) t.AddChild(clip, th);
        return t;
    }

    static void Cond(AnimatorStateTransition t, float duration,
                     params (AnimatorConditionMode mode, string param)[] conds)
    {
        t.hasExitTime = false;
        t.duration = duration;
        foreach (var (mode, param) in conds) t.AddCondition(mode, 0f, param);
    }

    static void ExitTime(AnimatorStateTransition t, float exitTime, float duration)
    {
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.duration = duration;
    }

    /// <summary>Saída dupla: em voo (Flying) volta ao Fly; senão, à Locomotion.
    /// A transição condicionada vem primeiro — o Animator avalia em ordem.</summary>
    static void ExitToFlyOrLoco(AnimatorState state, AnimatorState fly,
                                AnimatorState loco, float exit, float duration)
    {
        var toFly = state.AddTransition(fly);
        ExitTime(toFly, exit, duration);
        toFly.AddCondition(AnimatorConditionMode.If, 0f, "Flying");
        ExitTime(state.AddTransition(loco), exit, duration);
    }
}
