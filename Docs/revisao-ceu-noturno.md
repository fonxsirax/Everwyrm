# Revisão crítica — Sistema de Céu (pós-implementação)

> Auditoria do que foi construído entre "céu preto" e o estado atual, separando
> o que ficou bom, o que era cicatriz de debug e o que merecia refatoração.
> Itens marcados ✅ foram executados nesta revisão; 💡 são propostas abertas.

## O que estava certo e permanece

- **Arquitetura nativa** (PBS + spaceEmission + corpos celestes): validada na
  prática — fade atmosférico do amanhecer, reflexos futuros e rotação do
  firmamento funcionam de graça. A decisão do estudo se pagou.
- **Uma classe por sistema** (DayNightCycle ~ tempo/luz, NightSky ~ celeste):
  idioma do projeto (InfiniteTerrain é 1 arquivo). Não fragmentar.
- **Gerador procedural seeded + slot HDRI dual**: exatamente como planejado.
- **Auto-exposição com limites por curva**: resolveu dia estourado e noite
  ilegível com um único mecanismo.

## Problemas identificados e corrigidos ✅

1. **Cicatrizes de debug em produção** — `debugRawSky`/`ApplyRawSky`,
   `LogEffectiveSky` (2 corrotinas de log), `ProbeMaxValue` (readback de GPU a
   cada Play), logs verbosos. Cumpriram seu papel no diagnóstico (acharam 3
   bugs!) e foram REMOVIDOS. O log de geração virou 1 linha.
2. **Ownership da lua dividido** — diâmetro/fase no DayNightCycle;
   textura/brilho/halo no NightSky. Consolidado: **DayNightCycle = LUZ da lua
   (lux/cor/sombras) + órbita; NightSky = TODO o visual do disco** (diâmetro,
   fase, textura, earthshine, brilho, halo). Um lugar para o designer procurar.
3. **Fase da lua era estática** — virou **ciclo lunar automático de 29.5 dias
   in-game** (determinístico, `NightSky.MoonPhase` público p/ gameplay futuro —
   eventos de lua cheia etc.), com override manual opcional.
4. **Curvas quebravam se sunrise/sunset mudasse** — todas as curvas "por hora"
   assumem nascer=6/pôr=18 nas keys. Novo `SolarCurveHour()` remapeia a hora
   real para o relógio solar: mudar `sunriseHour/sunsetHour` agora ESTICA as
   curvas coerentemente em vez de quebrá-las em silêncio.
5. **Migrações-espaguete no EverwyrmAutoSetup** — cadeia de guards v1/v2/v3
   solta no Run(). Consolidadas em `MigrateCycleTuning()` com carimbo
   `tuningVersion` (roda 1x por cena; guards por valor continuam protegendo
   calibração manual do designer).
6. **Miudezas**: RNG unificado (Random.onUnitSphere → System.Random do seed),
   vazamento do material de detritos no OnDestroy, `ConfigureMoon(0)` com
   parâmetro fantasma → `ApplyMoonVisuals()` sem estado de change-detection
   (campos simples, aplicar todo frame é mais barato que detectar mudança),
   meteoros de 1800 m → 1200 m (menos atenuação da camada de névoa).

## Analisado e mantido de propósito

- **Névoa NÃO apaga meteoros/estrelas** como eu temia numa análise anterior: a
  névoa é de ALTURA (`fogMaxHeight` ~120 m) — raios para o alto atravessam só a
  camada baixa (~40-50% de atenuação). Meteoros a 1200 m ficaram só um pouco
  mais presentes.
- **Sub-pixel stars** (camada de escala 160 no gerador): viram "poeira de
  fundo" a 1024px/face — efeito desejável, não bug.
- **Parâmetros de geração vs runtime** no NightSky: continua sendo preciso
  "Regerar estrelas" (menu ⋮) após mexer em densidade/intensidade — documentado
  no header do Inspector. Auto-regen em edição adicionaria complexidade por
  pouco ganho.
- **Config package** com `PrecomputedAtmosphericAttenuation=0`: mantido com
  comentário longo no arquivo — flag=1 desliga o modo espaço do PBS (causa raiz
  do céu preto). Não reverter.

## Propostas abertas 💡 (não executadas — decisão de arte/escopo)

1. **CloudLayer diurno**: o céu PBS de dia é um azul estéril sem nuvens. O
   CloudLayer nativo (iluminado pelo sol real, vento, sombras de nuvem) é a
   maior melhoria visual barata disponível E é a fundação do clima futuro.
   Sugestão: ativar com o cloud map default do HDRP e calibrar.
2. **Textura NASA na lua** (CGI Moon Kit): slot pronto (`moonSurfaceTexture`).
3. **Blockade Labs**: gerar 2-3 céus noturnos "hero" (LDR — testar banding).
4. **Preview em modo edição** (`ExecuteAlways`): designers só veem o céu em
   Play hoje. Custo médio (guards de editor em todo o runtime-build); avaliar
   quando a iteração de arte intensificar.
5. **WildlifeClock → DayNightCycle**: fauna dormir/caçar por horário (gameplay,
   fora do escopo do céu).

## Estado final dos arquivos

- `DayNightCycle.cs` — tempo (fonte única), sol/lua (luz+órbita), volume
  runtime (céu PBS, exposição, névoa, sombras, indireta), relógio solar.
- `NightSky.cs` — estrelas (cubemap seeded), Via Láctea, rotação do
  firmamento, visual da lua + ciclo lunar, estrelas cadentes.
- `NightSkyStarGen.shader` — gerador one-time (hash de domínio grande).
- `EverwyrmAutoSetup.cs` — presença dos componentes + migrações versionadas +
  conversões de material dos biomas.
- `Packages/...-config/ShaderConfig.cs(.hlsl)` — flag do espaço documentado.
