using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Ciclo dia/noite em TEMPO REAL — um dia completo (24h do jogo) dura
/// `dayLengthMinutes` reais. Com 30 min e nascer/pôr do sol em 06:00/18:00,
/// ficam exatos 15 min de dia e 15 de noite.
///
/// Arquitetura (mesmo padrão do resto do projeto — tudo auto-construído):
///  - "Sequestra" o sol direcional já existente na cena (Morning_Sun do ALP,
///    mantendo sombras/volumetria calibradas) e cria a lua em runtime.
///  - Cria um Volume global de runtime (como o DragonDamageFeedback) que troca
///    o céu HDRI estático por Physically Based Sky com ambiente DINÂMICO:
///    céu, luz ambiente e reflexos escurecem/coloram sozinhos conforme o sol
///    gira — nascer/pôr do sol alaranjados sem textura nenhuma.
///  - Exposição fixa animada por curva + névoa por curva/gradiente fazem a
///    transição suave claro↔escuro sem estourar nem virar breu.
///
/// Tempo: acumulador `double` de horas — determinístico, independente de
/// framerate e sem perda de precisão em sessões longas. Fonte única de verdade
/// para clima/estações/eventos futuros (TimeOfDay, IsNight, OnSunrise...).
/// </summary>
[DefaultExecutionOrder(-100)]
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    [Header("Tempo")]
    [Tooltip("Duração de um dia COMPLETO (24h do jogo) em minutos reais.")]
    [Min(0.1f)] public float dayLengthMinutes = 30f;
    [Tooltip("Hora do dia em que a partida começa (0-24).")]
    [Range(0f, 24f)] public float startHour = 9f;
    [Tooltip("Multiplicador da velocidade do tempo (1 = normal, 0 = pausa o relógio). Útil para debug e eventos.")]
    [Min(0f)] public float timeScale = 1f;
    [Tooltip("Hora do nascer do sol. Com 6/18, dia e noite duram 15 min cada.")]
    [Range(0f, 12f)] public float sunriseHour = 6f;
    [Tooltip("Hora do pôr do sol.")]
    [Range(12f, 24f)] public float sunsetHour = 18f;

    [Header("Sol")]
    [Tooltip("Intensidade do sol ao meio-dia (lux). 130000 = valor do preset da cena.")]
    public float sunMaxLux = 130000f;
    [Tooltip("Fração da intensidade ao longo do arco do dia (0 = nascer, 1 = pôr).")]
    public AnimationCurve sunIntensity = new(
        new Keyframe(0f, 0.02f), new Keyframe(0.08f, 0.3f), new Keyframe(0.5f, 1f),
        new Keyframe(0.92f, 0.3f), new Keyframe(1f, 0.02f));
    [Tooltip("Temperatura de cor do sol (Kelvin) ao longo do arco do dia — quente nas pontas, neutra ao meio-dia.")]
    public AnimationCurve sunTemperature = new(
        new Keyframe(0f, 2200f), new Keyframe(0.12f, 4300f), new Keyframe(0.5f, 6500f),
        new Keyframe(0.88f, 4300f), new Keyframe(1f, 2200f));
    [Tooltip("Altura máxima do sol no céu ao meio-dia (graus).")]
    [Range(20f, 90f)] public float maxSunElevation = 62f;
    [Tooltip("Gira o percurso leste→oeste no mundo (graus).")]
    [Range(-180f, 180f)] public float orbitYaw = 0f;

    [Header("Lua")]
    [Tooltip("Intensidade da lua no auge da noite (lux). Estilizado — a real é ~0.25.")]
    public float moonMaxLux = 30f;
    public Color moonColor = new(0.72f, 0.78f, 1f);
    [Tooltip("Diâmetro visual da lua no céu (graus). A real tem ~0.5.")]
    [Range(0.5f, 8f)] public float moonAngularDiameter = 3f;
    [Tooltip("Fase da lua (0 = nova, 0.5 = cheia).")]
    [Range(0f, 1f)] public float moonPhase = 0.35f;
    [Tooltip("Sombras da lua à noite (só um direcional projeta sombra por vez; o sol está desligado à noite).")]
    public bool moonShadows = true;

    [Header("Exposição (clarear/escurecer geral)")]
    [Tooltip("Exposição fixa (EV) por HORA do dia. Dia ~14.6 (valor do preset), noite ~5.5. É esta curva que dá a sensação de escurecer.")]
    public AnimationCurve exposureByHour = new(
        new Keyframe(0f, 5.5f), new Keyframe(4.5f, 5.5f), new Keyframe(6f, 8f),
        new Keyframe(8f, 13.4f), new Keyframe(12f, 14.6f), new Keyframe(16f, 13.4f),
        new Keyframe(18f, 8f), new Keyframe(19.5f, 5.8f), new Keyframe(24f, 5.5f));

    [Header("Névoa")]
    public bool fogEnabled = true;
    [Tooltip("Distância média da névoa (m) por HORA — menor = mais densa (amanhecer/anoitecer).")]
    public AnimationCurve fogDistanceByHour = new(
        new Keyframe(0f, 800f), new Keyframe(6f, 420f), new Keyframe(10f, 1400f),
        new Keyframe(15f, 1400f), new Keyframe(18f, 450f), new Keyframe(21f, 800f),
        new Keyframe(24f, 800f));
    [Tooltip("Tinta da névoa ao longo das 24h (0 = meia-noite, 0.5 = meio-dia).")]
    public Gradient fogTintByHour = DefaultFogTint();
    [Tooltip("Altura máxima da camada de névoa (m).")]
    public float fogMaxHeight = 120f;
    [Tooltip("Névoa volumétrica (feixes de luz; custo alto de GPU).")]
    public bool volumetricFog = false;

    // ---------------------------------------------------------------- estado
    double hours;                       // hora do dia [0, 24) — fonte de verdade
    int lastHourInt = -1;
    bool wasNight;
    Light sun, moon;
    HDAdditionalLightData sunHd, moonHd;
    Volume volume;
    Exposure exposure;
    Fog fog;

    // ------------------------------------------------------------ API PÚBLICA
    /// <summary>Hora do dia em horas [0, 24). Ex.: 18.75 = 18:45.</summary>
    public float TimeOfDay => (float)hours;
    /// <summary>Dias completos vividos (começa em 1).</summary>
    public int Day { get; private set; } = 1;
    public bool IsNight => hours < sunriseHour || hours >= sunsetHour;
    /// <summary>0 no nascer → 1 no pôr do sol (clampado fora do dia).</summary>
    public float DayProgress01 =>
        Mathf.Clamp01(((float)hours - sunriseHour) / Mathf.Max(0.01f, sunsetHour - sunriseHour));
    /// <summary>0 no pôr → 1 no nascer do sol (clampado fora da noite).</summary>
    public float NightProgress01
    {
        get
        {
            float len = Mathf.Max(0.01f, 24f - (sunsetHour - sunriseHour));
            float t = hours >= sunsetHour ? (float)hours - sunsetHour
                                          : (float)hours + (24f - sunsetHour);
            return Mathf.Clamp01(t / len);
        }
    }
    /// <summary>Hora formatada "HH:MM" para UI.</summary>
    public string ClockText
    {
        get
        {
            int h = (int)hours;
            int m = (int)((hours - h) * 60.0);
            return $"{h:00}:{m:00}";
        }
    }

    public event Action<int> OnHourChanged;   // hora inteira virou (0-23)
    public event Action OnSunrise, OnSunset;  // futuros: fauna dormir, eventos...

    /// <summary>Pula o relógio para uma hora (debug/eventos). Determinístico.</summary>
    public void SetTime(float newHours)
    {
        hours = Mathf.Repeat(newHours, 24f);
        Apply();
    }

    // ---------------------------------------------------------------- ciclo
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        hours = Mathf.Repeat(startHour, 24f);
    }

    void Start()
    {
        // Start (não Awake): a cena inteira já acordou — as luzes existem.
        FindOrCreateSun();
        CreateMoon();
        BuildVolume();
        wasNight = IsNight;
        lastHourInt = (int)hours;
        Apply();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (moon != null) Destroy(moon.gameObject);
        if (volume != null) Destroy(volume.gameObject);
    }

    void Update()
    {
        hours += Time.deltaTime * timeScale * (24.0 / (dayLengthMinutes * 60.0));
        if (hours >= 24.0) { hours -= 24.0; Day++; }

        Apply();

        int h = (int)hours;
        if (h != lastHourInt) { lastHourInt = h; OnHourChanged?.Invoke(h); }
        bool night = IsNight;
        if (night != wasNight)
        {
            wasNight = night;
            if (night) OnSunset?.Invoke(); else OnSunrise?.Invoke();
        }
    }

    // ---------------------------------------------------------------- APPLY
    void Apply()
    {
        if (sun == null) return;
        bool night = IsNight;
        float hour = (float)hours;

        // ---- sol: arco leste→oeste; de noite continua abaixo do horizonte
        float p = DayProgress01;
        float sunElev = night ? -Mathf.Sin(NightProgress01 * Mathf.PI) * maxSunElevation
                              : Mathf.Sin(p * Mathf.PI) * maxSunElevation;
        float sunAz = Mathf.Lerp(90f, 270f, p) + orbitYaw;
        sun.transform.rotation = LookFromAzEl(sunAz, sunElev);
        if (sun.enabled != !night) sun.enabled = !night;
        if (!night)
        {
            sun.intensity = sunMaxLux * Mathf.Max(0f, sunIntensity.Evaluate(p));
            sun.useColorTemperature = true;
            sun.colorTemperature = sunTemperature.Evaluate(p);
        }

        // ---- lua: arco espelhado cruzando o céu durante a noite
        if (moon != null)
        {
            float np = NightProgress01;
            float moonElev = night ? Mathf.Sin(np * Mathf.PI) * maxSunElevation : -10f;
            float moonAz = Mathf.Lerp(90f, 270f, np) + orbitYaw;
            moon.transform.rotation = LookFromAzEl(moonAz, moonElev);
            if (moon.enabled != night) moon.enabled = night;
            if (night)
            {
                float fade = Mathf.Sin(np * Mathf.PI);        // nasce/põe suave
                moon.intensity = moonMaxLux * Mathf.Max(0.05f, fade);
                moon.color = moonColor;
                moon.shadows = moonShadows ? LightShadows.Soft : LightShadows.None;
                if (moonHd != null)
                {
                    moonHd.moonPhase = moonPhase;
                    moonHd.angularDiameter = moonAngularDiameter;
                }
            }
        }

        // ---- exposição e névoa por hora do dia
        if (exposure != null)
            exposure.fixedExposure.value = exposureByHour.Evaluate(hour);
        if (fog != null)
        {
            fog.meanFreePath.value = Mathf.Max(1f, fogDistanceByHour.Evaluate(hour));
            fog.tint.value = fogTintByHour.Evaluate(hour / 24f);
        }
    }

    /// <summary>Rotação de um direcional a partir de azimute/elevação (graus).
    /// Azimute bússola: 0 = norte, 90 = leste.</summary>
    static Quaternion LookFromAzEl(float azDeg, float elDeg)
    {
        float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
        Vector3 toBody = new(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el),
                             Mathf.Cos(el) * Mathf.Cos(az));
        return Quaternion.LookRotation(-toBody);
    }

    // ---------------------------------------------------------------- BUILD
    /// <summary>Usa o sol direcional mais forte já ativo na cena (preserva as
    /// sombras/volumetria calibradas do preset ALP); cria um se não houver.</summary>
    void FindOrCreateSun()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional || !l.gameObject.activeInHierarchy || !l.enabled)
                continue;
            if (sun == null || l.intensity > sun.intensity) sun = l;
        }
        if (sun == null)
        {
            var go = new GameObject("Sol (Day-Night)");
            go.transform.SetParent(transform, false);
            sunHd = go.AddHDLight(LightType.Directional);
            sun = go.GetComponent<Light>();
            sun.shadows = LightShadows.Soft;
            sun.intensity = sunMaxLux;
        }
        else sunHd = sun.GetComponent<HDAdditionalLightData>();

        if (sunHd != null) sunHd.interactsWithSky = true;   // PBS pinta o céu por ele
    }

    void CreateMoon()
    {
        var go = new GameObject("Lua (Day-Night)");
        go.transform.SetParent(transform, false);
        moonHd = go.AddHDLight(LightType.Directional);
        moon = go.GetComponent<Light>();
        moon.color = moonColor;
        moon.intensity = moonMaxLux;
        moon.shadows = moonShadows ? LightShadows.Soft : LightShadows.None;
        moon.enabled = false;
        moonHd.interactsWithSky = true;
        moonHd.angularDiameter = moonAngularDiameter;
        // Manual: a fase vem do parâmetro moonPhase (o sol está desligado à
        // noite, então ReflectSunLight não teria fonte).
        moonHd.celestialBodyShadingSource = HDAdditionalLightData.CelestialBodyShadingSource.Manual;
        moonHd.moonPhase = moonPhase;
    }

    /// <summary>Volume global de runtime (padrão DragonDamageFeedback): troca o
    /// céu HDRI estático por PBS + ambiente dinâmico e assume exposição/névoa.
    /// Prioridade 50 vence os volumes de preset do ALP (0) só nos overrides
    /// daqui — bloom/tonemapping/AO deles continuam valendo.</summary>
    void BuildVolume()
    {
        var go = new GameObject("Day-Night Volume");
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        var profile = volume.profile;   // instância própria de runtime

        var env = profile.Add<VisualEnvironment>();
        env.skyType.overrideState = true;
        env.skyType.value = (int)SkyType.PhysicallyBased;
        env.skyAmbientMode.overrideState = true;
        env.skyAmbientMode.value = SkyAmbientMode.Dynamic;

        profile.Add<PhysicallyBasedSky>();   // atmosfera terrestre padrão

        exposure = profile.Add<Exposure>();
        exposure.mode.overrideState = true;
        exposure.mode.value = ExposureMode.Fixed;
        exposure.fixedExposure.overrideState = true;
        exposure.fixedExposure.value = exposureByHour.Evaluate((float)hours);

        if (fogEnabled)
        {
            fog = profile.Add<Fog>();
            fog.enabled.overrideState = true;
            fog.enabled.value = true;
            fog.meanFreePath.overrideState = true;
            fog.tint.overrideState = true;
            fog.maximumHeight.overrideState = true;
            fog.maximumHeight.value = fogMaxHeight;
            fog.enableVolumetricFog.overrideState = true;
            fog.enableVolumetricFog.value = volumetricFog;
        }
    }

    static Gradient DefaultFogTint()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.45f, 0.55f, 0.75f), 0f),     // madrugada azulada
                new GradientColorKey(new Color(1f, 0.72f, 0.5f), 0.27f),      // amanhecer quente
                new GradientColorKey(Color.white, 0.5f),                      // meio-dia neutro
                new GradientColorKey(new Color(1f, 0.62f, 0.42f), 0.76f),     // entardecer
                new GradientColorKey(new Color(0.45f, 0.55f, 0.75f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
