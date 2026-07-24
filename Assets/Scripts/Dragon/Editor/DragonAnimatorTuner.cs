using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Rework de Movimento (hack and slash) — aplica nos ASSETS o que o código novo
/// espera. Idempotente: rodar de novo só reafirma os valores.
///
///  1. ANIMATOR (Assets/Dragao/Dragon Player.controller):
///     - transições de decolagem/voo/pouso ENCURTADAS (0.08–0.15 s, padrão de
///       action game: troca comandada nunca espera exit time);
///     - NOVA transição Locomotion→Fly (Speed ≥ 1.5): decolagem em corrida
///       plena nem toca a animação de salto;
///     - parâmetro "Stealth" + estado Stealth Idle com o clip "Unka Idle
///       Attack Mode" (postura de caça que o pack trazia sem uso);
///     - estado "Fly Fall Death" com o clip "UPFly Fall Death": o TOMBO de
///       bater forte em algo voando (o estol por exaustão segue no Stall Fall).
///  2. PREFAB (Unka Realistic): valores novos do DragonController que já
///     estavam serializados (groundAccel, flyAccel, turnSpeedAir, bankAngle).
///  3. FLIGHTPROFILE (asset): resposta vertical, ciclo de batidas, mergulho
///     com overspeed e swoop.
///
/// Menu: Tools > Everwyrm > Rework Movimento — Aplicar.
/// (v2 — reescrito no rework de jul/2026; se este menu não aparecer, o import
/// deste arquivo falhou: Reimport neste .cs resolve.)
/// </summary>
public static class DragonAnimatorTuner
{
    const string ControllerPath = "Assets/Dragao/Dragon Player.controller";
    const string PrefabPath = "Assets/Prefabs/Unka Realistic.prefab";
    const string ProfilePath = "Assets/Scriptables/Resources/Balance/FlightProfile.asset";
    const string StealthClipName = "Unka Idle Attack Mode";
    const string FallDeathClipName = "UPFly Fall Death";
    const string AnimDir = "Assets/Malbers Animations/Dragons/4 - Unka the Dragon/Animations";

    [MenuItem("Tools/Everwyrm/Rework Movimento — Aplicar (Animator + Prefab + FlightProfile)")]
    public static void Apply()
    {
        int changes = 0;
        changes += TuneAnimator();
        changes += TunePrefab();
        changes += TuneFlightProfile();

        AssetDatabase.SaveAssets();
        Debug.Log($"[Rework Movimento] Concluído — {changes} ajuste(s) aplicado(s). " +
                  "Se a CENA tiver um Unka com overrides nesses campos, os overrides " +
                  "vencem o prefab: confira o Inspector do dragão na cena.");
    }

    // ------------------------------------------------------------- ANIMATOR
    static int TuneAnimator()
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null)
        {
            Debug.LogError($"[Rework Movimento] Controller não achado: {ControllerPath}");
            return 0;
        }

        var sm = ctrl.layers[0].stateMachine;
        var locomotion = Find(sm, "Locomotion");
        var takeOff = Find(sm, "TakeOff");
        var fly = Find(sm, "Fly");
        var glide = Find(sm, "Glide");
        var land = Find(sm, "Land");
        if (locomotion == null || takeOff == null || fly == null)
        {
            Debug.LogError("[Rework Movimento] Estados Locomotion/TakeOff/Fly não achados.");
            return 0;
        }

        int n = 0;

        // ---- Locomotion→TakeOff: mais curta e SÓ devagar (Speed < 1.5)
        foreach (var t in locomotion.transitions.Where(t => t.destinationState == takeOff))
        {
            n += Set(t, duration: 0.08f);
            if (!t.conditions.Any(c => c.parameter == "Speed"))
            {
                t.AddCondition(AnimatorConditionMode.Less, 1.5f, "Speed");
                n++;
            }
        }

        // ---- NOVA Locomotion→Fly: correndo, abre as asas e já está voando
        if (!locomotion.transitions.Any(t => t.destinationState == fly))
        {
            var t = locomotion.AddTransition(fly);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Flying");
            t.AddCondition(AnimatorConditionMode.Greater, 1.49f, "Speed");
            n++;
        }

        // ---- TakeOff→Fly: o salto toca INTEIRO. Cortar em 30% (a versão
        //      "snappy" da 1ª passada) mutilava justamente o bote das asas —
        //      e a decolagem em CORRIDA, que é o caminho rápido, nem passa por
        //      aqui: vai direto Locomotion→Fly. Este estado é só o salto parado.
        foreach (var t in takeOff.transitions.Where(t => t.destinationState == fly))
            n += Set(t, exitTime: 0.8f, duration: 0.15f);

        // ---- Fly→Glide e Land→Locomotion: crossfades mais vivos
        if (glide != null)
            foreach (var t in fly.transitions.Where(t => t.destinationState == glide))
                n += Set(t, duration: 0.25f);
        if (land != null)
            foreach (var t in land.transitions.Where(t => t.destinationState == locomotion))
                n += Set(t, exitTime: 0.45f, duration: 0.15f);

        n += AddStealthIdle(ctrl, sm, locomotion, takeOff);
        n += AddFlyFallDeath(sm, fly, land, Find(sm, "Stall Fall"), Find(sm, "Swim"));

        if (n > 0) EditorUtility.SetDirty(ctrl);
        return n;
    }

    /// <summary>Tombo da colisão em voo: bater forte em árvore/rocha derruba o
    /// dragão de verdade (clip "UPFly Fall Death", que o pack trazia sem uso).
    /// O DragonController entra nele por CrossFade; as saídas abaixo devolvem
    /// para Stall Fall (segue caindo), Fly (recuperou) ou Land/Swim.</summary>
    static int AddFlyFallDeath(AnimatorStateMachine sm, AnimatorState fly,
                               AnimatorState land, AnimatorState stallFall,
                               AnimatorState swim)
    {
        if (Find(sm, "Fly Fall Death") != null) return 0;

        var clip = FindClip(FallDeathClipName);
        if (clip == null)
        {
            Debug.LogWarning($"[Rework Movimento] Clip '{FallDeathClipName}' não achado — " +
                             "colisão em voo segue usando só o Stall Fall.");
            return 0;
        }

        var state = sm.AddState("Fly Fall Death", new Vector3(320f, 200f));
        state.motion = clip;

        // pousou/caiu na água no meio do tombo: sai na hora
        if (land != null) Link(state, land, 0.12f, ("Flying", false));
        if (swim != null) Link(state, swim, 0.2f, ("Swimming", true));
        // recuperou o voo (o desequilíbrio passou)
        if (fly != null) Link(state, fly, 0.25f, ("Falling", false), ("Flying", true));
        // tombo terminou e ainda está caindo: continua na queda longa
        if (stallFall != null)
        {
            var t = Link(state, stallFall, 0.25f, ("Falling", true));
            t.hasExitTime = true;
            t.exitTime = 0.85f;
        }
        return 1;
    }

    /// <summary>Transição sem exit time com condições booleanas.</summary>
    static AnimatorStateTransition Link(AnimatorState from, AnimatorState to,
                                        float duration, params (string param, bool on)[] conds)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = duration;
        foreach (var (param, on) in conds)
            t.AddCondition(on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
        return t;
    }

    /// <summary>Postura de caça parado em Stealth — usa o clip "Idle Attack
    /// Mode" que o pack da Malbers trazia sem nenhum estado.</summary>
    static int AddStealthIdle(AnimatorController ctrl, AnimatorStateMachine sm,
                              AnimatorState locomotion, AnimatorState takeOff)
    {
        if (!ctrl.parameters.Any(p => p.name == "Stealth"))
            ctrl.AddParameter("Stealth", AnimatorControllerParameterType.Bool);

        if (Find(sm, "Stealth Idle") != null) return 0;

        var clip = FindClip(StealthClipName);
        if (clip == null)
        {
            Debug.LogWarning($"[Rework Movimento] Clip '{StealthClipName}' não achado — " +
                             "Stealth funciona igual, só sem a postura de caça no idle.");
            return 0;
        }

        var state = sm.AddState("Stealth Idle",
            locomotion.transitions.Length > 0 ? sm.entryPosition + new Vector3(-260f, 160f)
                                              : new Vector3(0f, 200f));
        state.motion = clip;

        var enter = locomotion.AddTransition(state);
        enter.hasExitTime = false;
        enter.hasFixedDuration = true;
        enter.duration = 0.25f;
        enter.AddCondition(AnimatorConditionMode.If, 0f, "Stealth");
        enter.AddCondition(AnimatorConditionMode.Less, 0.25f, "Speed");

        var exitStealth = state.AddTransition(locomotion);
        exitStealth.hasExitTime = false;
        exitStealth.hasFixedDuration = true;
        exitStealth.duration = 0.2f;
        exitStealth.AddCondition(AnimatorConditionMode.IfNot, 0f, "Stealth");

        var exitMove = state.AddTransition(locomotion);
        exitMove.hasExitTime = false;
        exitMove.hasFixedDuration = true;
        exitMove.duration = 0.15f;
        exitMove.AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");

        var toAir = state.AddTransition(takeOff);
        toAir.hasExitTime = false;
        toAir.hasFixedDuration = true;
        toAir.duration = 0.1f;
        toAir.AddCondition(AnimatorConditionMode.If, 0f, "Flying");

        return 1;
    }

    // --------------------------------------------------------------- PREFAB
    static int TunePrefab()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[Rework Movimento] Prefab não achado: {PrefabPath}");
            return 0;
        }

        int n = 0;
        try
        {
            var dragon = root.GetComponentInChildren<DragonController>(true);
            if (dragon == null)
            {
                Debug.LogError("[Rework Movimento] DragonController não achado no prefab.");
                return 0;
            }

            var so = new SerializedObject(dragon);
            n += SetFloat(so, "groundAccel", 35f);
            n += SetFloat(so, "flyAccel", 22f);
            n += SetFloat(so, "turnSpeedAir", 95f);
            n += SetFloat(so, "bankAngle", 48f);
            if (n > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return n;
    }

    // -------------------------------------------------------- FLIGHTPROFILE
    static int TuneFlightProfile()
    {
        var p = AssetDatabase.LoadAssetAtPath<FlightProfile>(ProfilePath);
        if (p == null)
        {
            Debug.LogWarning($"[Rework Movimento] FlightProfile não achado em {ProfilePath} " +
                             "— o código usa os defaults novos se o asset não existir.");
            return 0;
        }

        int n = 0;
        // subida por batida do sistema de atributos automáticos: a força vem do
        // PESO (DragonGrowth.FlapLiftMul), não mais do tamanho do corpo (ver
        // DragonFlight). NERF jul/2026: a 1ª calibração (12/17/21/13) ficou apelona —
        // quase um flap já saturava o teto de subida. Reduzidos (flapLift/flapBonusLift
        // a 1/4) para exigir RITMO (batidas + planeio), não um único flap perfeito.
        n += SetIf(ref p.takeoffClimbRate, 6f);
        n += SetIf(ref p.flapLift, 4.25f);
        n += SetIf(ref p.maxRiseSpeed, 10f);
        n += SetIf(ref p.flapBonusLift, 3.25f);
        n += SetIf(ref p.energyPerFlap, 3f);
        n += SetIf(ref p.flapMinInterval, 0.22f);
        n += SetIf(ref p.cycleRecovery, 0.3f);
        n += SetIf(ref p.sinkAtStall, 3f);
        n += SetIf(ref p.slownessPower, 1.8f);
        n += SetIf(ref p.verticalResponse, 9f);
        n += SetIf(ref p.diveOverspeed, 1.3f);
        n += SetIf(ref p.swoopConversion, 0.35f);
        if (n > 0) EditorUtility.SetDirty(p);
        return n;
    }

    // ------------------------------------------------------------- HELPERS
    static AnimatorState Find(AnimatorStateMachine sm, string name) =>
        sm.states.FirstOrDefault(s => s.state.name == name).state;

    static int Set(AnimatorStateTransition t, float? exitTime = null, float? duration = null)
    {
        int n = 0;
        if (exitTime.HasValue && !Mathf.Approximately(t.exitTime, exitTime.Value))
        {
            t.exitTime = exitTime.Value;
            n++;
        }
        if (duration.HasValue && !Mathf.Approximately(t.duration, duration.Value))
        {
            t.hasFixedDuration = true;
            t.duration = duration.Value;
            n++;
        }
        return n;
    }

    static int SetFloat(SerializedObject so, string field, float value)
    {
        var prop = so.FindProperty(field);
        if (prop == null || Mathf.Approximately(prop.floatValue, value)) return 0;
        prop.floatValue = value;
        return 1;
    }

    static int SetIf(ref float field, float value)
    {
        if (Mathf.Approximately(field, value)) return 0;
        field = value;
        return 1;
    }

    /// <summary>Acha um clip pelo NOME DELE, inclusive quando é sub-asset de um
    /// FBX com outro nome (ex.: "UPFly Fall Death" mora no "Unka Fly InPlace.FBX").
    /// Buscar pelo nome do arquivo não encontrava esses — varre os FBX do rig.</summary>
    static AnimationClip FindClip(string clipName)
    {
        string[] dirs = AssetDatabase.IsValidFolder(AnimDir) ? new[] { AnimDir } : null;
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", dirs))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview") &&
                    clip.name == clipName)
                    return clip;
        }
        return null;
    }
}
