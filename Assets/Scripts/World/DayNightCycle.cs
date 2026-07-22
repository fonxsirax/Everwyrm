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
    [Tooltip("TESTE: começa a partida já de NOITE (na hora abaixo). Desligado = começa em startHour, de dia, como hoje.")]
    public bool startAtNight = false;
    [Tooltip("Hora usada quando 'Start At Night' está ligado.")]
    [Range(0f, 24f)] public float nightStartHour = 21f;
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
    [Tooltip("Temperatura de cor do sol (Kelvin) ao longo do arco do dia — quente nas pontas, levemente dourada ao meio-dia (5900K; 6500K = neutro).")]
    public AnimationCurve sunTemperature = DefaultSunTemperature();
    [Tooltip("Altura máxima do sol no céu ao meio-dia (graus).")]
    [Range(20f, 90f)] public float maxSunElevation = 62f;
    [Tooltip("Gira o percurso leste→oeste no mundo (graus).")]
    [Range(-180f, 180f)] public float orbitYaw = 0f;

    [Header("Lua (a LUZ — o visual do disco/fase/textura fica no NightSky)")]
    [Tooltip("Intensidade da lua no auge da noite (lux). Estilizado — a real é ~0.25. 100 = noite AZUL-PROFUNDA legível; acima de ~200 o céu clareia como dia nublado.")]
    public float moonMaxLux = 100f;
    public Color moonColor = new(0.72f, 0.78f, 1f);
    [Tooltip("Sombras da lua à noite (só um direcional projeta sombra por vez; o sol está desligado à noite).")]
    public bool moonShadows = true;
    [Tooltip("Peso da sombra da lua (1 = sombra 100% preta). Abaixo de 1 vira o 'piso' de visibilidade da noite: nada fica breu total.")]
    [Range(0f, 1f)] public float moonShadowDimmer = 0.65f;

    [Header("Exposição (clarear/escurecer geral)")]
    [Tooltip("Exposição AUTOMÁTICA: adapta como o olho — floresta fechada de noite clareia, meio-dia a céu aberto escurece (conserta 'estourado' de dia e 'breu' à noite). A curva abaixo vira o CENTRO da faixa permitida.")]
    public bool autoExposure = true;
    [Tooltip("Meia-largura da faixa da exposição automática (± EV em torno da curva).")]
    [Range(0.25f, 3f)] public float autoExposureRange = 1.25f;
    [Tooltip("EV alvo por HORA do dia. Dia ~14.6 (valor do preset), noite ~7.5. Com autoExposure ligado é o centro da faixa; desligado, é o valor fixo.")]
    public AnimationCurve exposureByHour = DefaultExposureByHour();

    [Tooltip("Multiplicador da luz indireta (ambiente do céu/probes/reflexos) por HORA. À noite fica em ~0.3: preenche as sombras com o ambiente da lua — muito baixo deixa o primeiro plano preto e a vegetação ao longe 'acesa' por contraste.")]
    public AnimationCurve indirectByHour = DefaultIndirectByHour();

    [Header("Sombras")]
    [Tooltip("Distância máxima de sombra (m) por HORA. Dia = 300 (calibração ALP). Noite ESTENDIDA: além desse limite os objetos recebem a lua sem sombra e ficam 'pálidos de dia' (pedras do deserto, árvores longe).")]
    public AnimationCurve shadowDistanceByHour = new(
        new Keyframe(0f, 700f), new Keyframe(5f, 700f), new Keyframe(7f, 300f),
        new Keyframe(17f, 300f), new Keyframe(19f, 700f), new Keyframe(24f, 700f));
    [Tooltip("Transmissão da luz direcional através das folhas por HORA (0-1). Reduzida à noite: copas retroiluminadas pela lua deixam de 'brilhar' pálidas ao longe.")]
    public AnimationCurve transmissionByHour = new(
        new Keyframe(0f, 0.3f), new Keyframe(5f, 0.3f), new Keyframe(7f, 1f),
        new Keyframe(17f, 1f), new Keyframe(19f, 0.3f), new Keyframe(24f, 0.3f));

    [Header("Nuvens (CloudLayer nativo — fundação do clima futuro)")]
    [Tooltip("Nuvens 2D iluminadas pela luz direcional REAL: douradas no amanhecer/pôr do sol, prateadas ao luar; encobrem estrelas naturalmente.")]
    public bool clouds = true;
    [Tooltip("Opacidade global — o knob que o clima futuro vai animar (0 = céu limpo).")]
    [Range(0f, 1f)] public float cloudOpacity = 0.5f;
    [Tooltip("Mapa de nuvens custom (canal R = padrão usado). Vazio = textura default do HDRP.")]
    public Texture2D cloudMap;
    [Tooltip("Espessura/auto-sombreamento das nuvens.")]
    [Range(0f, 1f)] public float cloudThickness = 0.5f;
    [Tooltip("Sombras das nuvens no chão (viram cookie da luz direcional).")]
    public bool cloudShadows = true;
    [Tooltip("Vento do céu (velocidade de scroll das nuvens, km/h).")]
    public float cloudWindSpeed = 30f;
    [Range(0f, 360f)] public float cloudWindOrientation = 40f;

    [Header("Névoa")]
    public bool fogEnabled = true;
    [Tooltip("Distância média da névoa (m) por HORA — menor = mais densa. Noite densa de propósito: engole a vegetação distante iluminada sem sombra.")]
    public AnimationCurve fogDistanceByHour = DefaultFogDistanceByHour();
    [Tooltip("Tinta da névoa ao longo das 24h (0 = meia-noite, 0.5 = meio-dia).")]
    public Gradient fogTintByHour = DefaultFogTint();
    [Tooltip("Altura máxima da camada de névoa (m).")]
    public float fogMaxHeight = 120f;
    [Tooltip("Névoa volumétrica (feixes de luz; custo alto de GPU).")]
    public bool volumetricFog = false;

    /// <summary>Versão da calibração aplicada pelo EverwyrmAutoSetup — evita
    /// re-rodar migrações a cada recompilação.</summary>
    [HideInInspector] public int tuningVersion;
    public const int CurrentTuningVersion = 6;

    // ---------------------------------------------------------------- estado
    double hours;                       // hora do dia [0, 24) — fonte de verdade
    int lastHourInt = -1;
    bool wasNight;
    Light sun, moon;
    HDAdditionalLightData sunHd, moonHd;
    Volume volume;
    Exposure exposure;
    Fog fog;
    IndirectLightingController indirect;
    HDShadowSettings shadows;
    PhysicallyBasedSky pbsSky;
    VisualEnvironment env;
    CloudLayer cloudLayer;

    /// <summary>Overrides do céu no volume de runtime — o NightSky escreve o
    /// campo estelar (spaceEmission*) aqui. Null antes do Start.</summary>
    public PhysicallyBasedSky Sky => pbsSky;
    /// <summary>Perfil do volume de runtime (p/ o NightSky adicionar overrides
    /// próprios, ex.: HDRI de debug). Null antes do Start.</summary>
    public VolumeProfile Profile => volume != null ? volume.profile : null;
    /// <summary>VisualEnvironment do volume de runtime (troca de tipo de céu).</summary>
    public VisualEnvironment Env => env;
    /// <summary>Dados HD da lua (corpo celeste) — o NightSky configura textura
    /// de superfície/earthshine/flare. Null antes do Start.</summary>
    public HDAdditionalLightData MoonData => moonHd;

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
    /// <summary>Remapeia a hora real para o "relógio solar" das curvas — que
    /// são desenhadas assumindo nascer=6h e pôr=18h. Se o designer mudar
    /// sunriseHour/sunsetHour, as curvas ESTICAM coerentemente em vez de
    /// quebrarem em silêncio. Público: o NightSky usa também.</summary>
    public float SolarCurveHour(float hour)
    {
        if (hour >= sunriseHour && hour < sunsetHour)
            return 6f + (hour - sunriseHour) / Mathf.Max(0.01f, sunsetHour - sunriseHour) * 12f;
        float nightLen = Mathf.Max(0.01f, 24f - (sunsetHour - sunriseHour));
        float t = hour >= sunsetHour ? hour - sunsetHour : hour + (24f - sunsetHour);
        return Mathf.Repeat(18f + t / nightLen * 12f, 24f);
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

    /// <summary>Período de atividade da fauna derivado da hora — é o que o
    /// WildlifeClock consome (animais dormem/caçam por horário de verdade).</summary>
    public AnimalDefinition.ActivityPeriod FaunaPeriod
    {
        get
        {
            float h = TimeOfDay;
            if (h >= sunriseHour - 2f && h < sunriseHour + 1f)
                return AnimalDefinition.ActivityPeriod.Madrugada;
            if (h >= sunsetHour - 1f && h < sunsetHour + 2f)
                return AnimalDefinition.ActivityPeriod.Entardecer;
            return IsNight ? AnimalDefinition.ActivityPeriod.Noite
                           : AnimalDefinition.ActivityPeriod.Dia;
        }
    }

    /// <summary>Pula o relógio para uma hora (debug/eventos). Determinístico.</summary>
    public void SetTime(float newHours)
    {
        hours = Mathf.Repeat(newHours, 24f);
        Apply();
    }

    /// <summary>Restaura o relógio completo (hora + dia) — save/load e sync de
    /// rede: o servidor manda (TimeOfDay, Day) e o cliente repõe aqui.</summary>
    public void SetTime(float newHours, int day)
    {
        Day = Mathf.Max(1, day);
        SetTime(newHours);
    }

    // ---------------------------------------------------------------- ciclo
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        hours = Mathf.Repeat(startAtNight ? nightStartHour : startHour, 24f);
        // pluga a fauna no relógio real: corujas caçam à noite, cervos pastam
        // de dia (o WildlifeClock nasceu como ponte esperando este Provider)
        WildlifeClock.Provider = () => Instance != null ? Instance.FaunaPeriod
                                                        : AnimalDefinition.ActivityPeriod.Dia;
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
        if (Instance == this)
        {
            Instance = null;
            WildlifeClock.Provider = null;   // fauna volta ao "Dia" neutro
        }
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
                    moonHd.shadowDimmer = moonShadowDimmer;   // piso de visibilidade
            }
        }

        // ---- exposição, luz indireta e névoa por HORA SOLAR (curvas desenhadas
        // com nascer=6/pôr=18 esticam junto com sunriseHour/sunsetHour)
        float ch = SolarCurveHour(hour);
        if (exposure != null)
        {
            float ev = exposureByHour.Evaluate(ch);
            exposure.fixedExposure.value = ev;
            exposure.limitMin.value = ev - autoExposureRange;
            exposure.limitMax.value = ev + autoExposureRange;
        }
        if (indirect != null)
        {
            // dim de lightmaps/probes DIFUSOS gerados de dia — sem isso a
            // vegetação baked continua "clareada" no meio da noite.
            indirect.indirectDiffuseLightingMultiplier.value =
                Mathf.Clamp01(indirectByHour.Evaluate(ch));
            // REFLEXOS ficam SEMPRE cheios (e vencem o 0.7 do perfil ALP):
            // é o céu vivo na água — estrelas/lua/nuvens/pôr do sol. Diminuir
            // isto à noite desconectava a água do céu.
            indirect.reflectionLightingMultiplier.value = 1f;
            indirect.reflectionProbeIntensityMultiplier.value = 1f;
        }
        if (fog != null)
        {
            fog.meanFreePath.value = Mathf.Max(1f, fogDistanceByHour.Evaluate(ch));
            fog.tint.value = fogTintByHour.Evaluate(ch / 24f);
        }
        if (shadows != null)
        {
            shadows.maxShadowDistance.value = Mathf.Max(50f, shadowDistanceByHour.Evaluate(ch));
            shadows.directionalTransmissionMultiplier.value = Mathf.Clamp01(transmissionByHour.Evaluate(ch));
        }
        if (cloudLayer != null)
            cloudLayer.opacity.value = cloudOpacity;   // ao vivo — o clima futuro anima isto
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
        // ORDEM IMPORTA: o HDLightRenderDatabase decide se a luz é "directional"
        // NO REGISTRO do HDAdditionalLightData — e o AddHDLight sozinho seta o
        // tipo DEPOIS de registrar. Criada assim, a lua ficava fora da lista de
        // corpos celestes p/ sempre: iluminava, mas o DISCO nunca renderizava.
        // Criar a Light já direcional ANTES do componente HD resolve.
        moon = go.AddComponent<Light>();
        moon.type = LightType.Directional;
        moonHd = go.AddHDLight(LightType.Directional);
        moon.color = moonColor;
        moon.intensity = moonMaxLux;
        moon.shadows = moonShadows ? LightShadows.Soft : LightShadows.None;
        moon.enabled = false;
        moonHd.interactsWithSky = true;
        // Manual: a fase vem do NightSky (o sol está desligado à noite, então
        // ReflectSunLight não teria fonte). Disco/fase/textura: NightSky.
        moonHd.celestialBodyShadingSource = HDAdditionalLightData.CelestialBodyShadingSource.Manual;

        // sombra da lua com a MESMA qualidade do sol da cena. O default do
        // HDRP p/ luz criada em runtime (resolução baixa, bias genérico) fazia
        // objetos pequenos (bushes) perderem a própria sombra à distância —
        // "de longe brilha, de perto escurece".
        moonHd.shadowResolution.useOverride = true;
        moonHd.shadowResolution.@override = 2048;
        if (sunHd != null)
        {
            moonHd.normalBias = sunHd.normalBias;
            moonHd.slopeBias = sunHd.slopeBias;
            moonHd.shadowNearPlane = sunHd.shadowNearPlane;
        }
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

        env = profile.Add<VisualEnvironment>();
        env.skyType.overrideState = true;
        env.skyType.value = (int)SkyType.PhysicallyBased;
        env.skyAmbientMode.overrideState = true;
        env.skyAmbientMode.value = SkyAmbientMode.Dynamic;
        // explícito: nada de herdar default ambíguo do stack — o espaço
        // (estrelas) exige World e o modo Advanced (EarthSimple ignora
        // spaceEmissionTexture no renderer do PBS)
        env.renderingSpace.overrideState = true;
        env.renderingSpace.value = RenderingSpace.World;

        pbsSky = profile.Add<PhysicallyBasedSky>();   // atmosfera terrestre padrão
        pbsSky.type.overrideState = true;
        pbsSky.type.value = PhysicallyBasedSkyModel.EarthAdvanced;

        if (clouds)
        {
            env.cloudType.overrideState = true;
            env.cloudType.value = (int)CloudType.CloudLayer;
            // vento global do céu (o CloudLayer escuta por padrão)
            env.windSpeed.overrideState = true;
            env.windSpeed.value = cloudWindSpeed;
            env.windOrientation.overrideState = true;
            env.windOrientation.value = cloudWindOrientation;

            cloudLayer = profile.Add<CloudLayer>();
            cloudLayer.opacity.overrideState = true;
            cloudLayer.opacity.value = cloudOpacity;
            var la = cloudLayer.layerA;
            // sem isto as nuvens ficam ESTÁTICAS: o default (None) ignora o
            // vento global; Procedural faz o mapa deslizar na direção/veloc.
            // do vento configurado acima
            la.distortionMode.overrideState = true;
            la.distortionMode.value = CloudDistortionMode.Procedural;
            if (cloudMap != null)
            {
                la.cloudMap.overrideState = true;
                la.cloudMap.value = cloudMap;
            }
            la.thickness.overrideState = true;
            la.thickness.value = cloudThickness;
            la.castShadows.overrideState = true;
            la.castShadows.value = cloudShadows;
        }

        exposure = profile.Add<Exposure>();
        exposure.mode.overrideState = true;
        exposure.mode.value = autoExposure ? ExposureMode.Automatic : ExposureMode.Fixed;
        exposure.meteringMode.overrideState = true;
        exposure.meteringMode.value = MeteringMode.CenterWeighted;
        exposure.fixedExposure.overrideState = true;
        exposure.fixedExposure.value = exposureByHour.Evaluate((float)hours);
        exposure.limitMin.overrideState = true;
        exposure.limitMax.overrideState = true;

        indirect = profile.Add<IndirectLightingController>();
        indirect.indirectDiffuseLightingMultiplier.overrideState = true;
        indirect.reflectionLightingMultiplier.overrideState = true;
        indirect.reflectionProbeIntensityMultiplier.overrideState = true;

        // só a distância — os demais parâmetros de sombra (cascades etc.)
        // continuam vindo do perfil ALP da cena
        shadows = profile.Add<HDShadowSettings>();
        shadows.maxShadowDistance.overrideState = true;
        shadows.directionalTransmissionMultiplier.overrideState = true;

        // SSR garantido ligado (a água usa p/ refletir a CENA; o céu é fallback)
        var ssr = profile.Add<ScreenSpaceReflection>();
        ssr.enabled.overrideState = true;
        ssr.enabled.value = true;

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

    /// <summary>Curvas padrão (públicas: o auto-setup usa para migrar cenas
    /// salvas com calibrações antigas).</summary>
    public static AnimationCurve DefaultExposureByHour() => new(
        new Keyframe(0f, 6.3f), new Keyframe(4.5f, 6.3f), new Keyframe(6f, 8f),
        new Keyframe(8f, 13.4f), new Keyframe(12f, 14.6f), new Keyframe(16f, 13.4f),
        new Keyframe(18f, 8f), new Keyframe(19.5f, 6.6f), new Keyframe(24f, 6.3f));

    public static AnimationCurve DefaultIndirectByHour() => new(
        new Keyframe(0f, 0.3f), new Keyframe(5f, 0.3f), new Keyframe(7.5f, 1f),
        new Keyframe(16.5f, 1f), new Keyframe(19.5f, 0.3f), new Keyframe(24f, 0.3f));

    public static AnimationCurve DefaultSunTemperature() => new(
        new Keyframe(0f, 2200f), new Keyframe(0.12f, 4300f), new Keyframe(0.5f, 5900f),
        new Keyframe(0.88f, 4300f), new Keyframe(1f, 2200f));

    public static AnimationCurve DefaultFogDistanceByHour() => new(
        new Keyframe(0f, 320f), new Keyframe(6f, 350f), new Keyframe(10f, 1400f),
        new Keyframe(15f, 1400f), new Keyframe(18f, 380f), new Keyframe(21f, 320f),
        new Keyframe(24f, 320f));

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
