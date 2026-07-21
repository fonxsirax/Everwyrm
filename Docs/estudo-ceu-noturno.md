# Estudo — Céu Noturno do Everwyrm (HDRP 17.0.4)

> Levantamento completo das capacidades nativas do HDRP para céu, atmosfera,
> corpos celestes, exposição, reflexos e volumetria — verificado **no código-fonte
> do pacote instalado** (17.0.4), não em documentação genérica. Cada decisão
> arquitetural referencia a capacidade nativa que a sustenta.
>
> Meta: um céu noturno que faça o jogador parar de voar só para olhar.

---

## 1. Inventário de capacidades nativas (verificadas)

### 1.1 Physically Based Sky (PBS) — o coração do sistema
Já em uso pelo DayNightCycle (volume runtime, prioridade 50).

| Capacidade | Campo/API | Verificado |
|---|---|---|
| Atmosfera física (Rayleigh/Mie/ozônio) | `PhysicallyBasedSky` | ✅ em uso |
| **Cubemap de espaço atrás da atmosfera** | `spaceEmissionTexture` (Cubemap) | ✅ `PhysicallyBasedSky.cs:146` |
| Intensidade do espaço | `spaceEmissionMultiplier` | ✅ `:150` |
| **Rotação do firmamento** | `spaceRotation` (Vector3) | ✅ `:154` |

O space emission é renderizado **dentro do shader do céu**, atenuado pela própria
atmosfera. Consequências (todas de graça):
- Estrelas **somem no amanhecer pela física** (o espalhamento da atmosfera
  iluminada as encobre) — sem alpha, sem transição artificial. Exigência do
  design atendida nativamente.
- Estrelas entram no **cubemap de reflexo do céu e no ambient probe**
  (`SkyManager.GetReflectionTexture` / `ambientProbe`) → água, SSR e probes
  futuros refletem o céu correto sem nenhum trabalho extra.
- `spaceRotation` animável por frame → **rotação real do firmamento durante a
  noite** (estrelas nascem e se põem, como na vida real). Vende o céu como
  "lugar", não papel de parede.

### 1.2 Corpos celestes (sol e lua já são)
`HDAdditionalLightData` de qualquer luz direcional com `interactsWithSky`:

| Capacidade | Campo | Verificado |
|---|---|---|
| Tamanho aparente | `angularDiameter` | ✅ em uso (lua 3°) |
| **Textura de superfície (crateras)** | `surfaceTexture` | ✅ `:873` |
| Tinta da superfície | `surfaceTint` | ✅ |
| Fase da lua + rotação da fase | `moonPhase`, `moonPhaseRotation` | ✅ em uso |
| Luz refletida da terra no lado escuro | `earthshine` | ✅ `:843` |
| **Glow/halo nativo** | `flareSize`, `flareFalloff`, `flareTint`, `flareMultiplier` | ✅ `:849-867` |
| Sombreamento | `celestialBodyShadingSource` (Emission/ReflectSunLight/Manual) | ✅ em uso (Manual) |

O disco é desenhado pelo shader do céu (`PhysicallyBasedSkyRendering.hlsl`:
`ComputeMoonPhase`, `ComputeEarthshine`) → fase e iluminação fisicamente
consistentes, e a lua aparece nos reflexos (faz parte do céu).

### 1.3 Nuvens (o futuro clima já tem casa)
- **CloudLayer** (`Runtime/Sky/CloudSystem/CloudLayer/`): nuvens 2D baked,
  **iluminadas pela luz direcional real** (douradas no pôr do sol, prateadas
  sob a lua), 2 camadas, vento/scroll, **sombras de nuvem no chão** (vira
  cookie da direcional). Aceita **mapa de nuvem custom** → pool de padrões
  (limpo/cirros/carregado). Renderiza **por cima do céu** → nuvem encobre
  estrelas e lua sem nenhum código nosso.
- **Volumetric Clouds** (`Runtime/Lighting/VolumetricClouds/`): nuvens 3D
  reais, atravessáveis em voo. Custo relevante (~1–2 ms+); candidata a
  qualidade "Ultra" futura, não à base.

### 1.4 Exposição, volumetria, reflexos, flare
- **Exposure**: Automatic com limites por curva — já implementado no ciclo.
- **Fog volumétrico** (`enableVolumetricFog`) + **Local Volumetric Fog** com
  textura 3D — candidato natural para **auroras** futuras (cortinas emissivas
  volumétricas por bioma) e neblina de clima.
- **Reflexos**: céu → cubemap de reflexo + ambient probe automáticos; SSR e
  Water System (nativo do HDRP 17, já habilitado pelo WaterSetup) fazem
  fallback pro céu → estrelas/lua na água **sem arquitetura extra**.
- **Lens Flare SRP** (`LensFlareComponentSRP`, core package): flare artístico
  adicional para a lua, se o `flare*` do corpo celeste não bastar.

### 1.5 Extensibilidade oficial
`SkySettings`/`SkyRenderer` são públicos — o HDRP suporta céus 100% custom
como cidadãos de primeira classe. **Decisão: não usar.** O PBS + space
emission cobre tudo que precisamos; um céu custom clonaria centenas de linhas
do PBS (atenuação atmosférica, precompute, reflexos) e quebraria a cada
upgrade de versão. Fica registrado como saída de emergência, não como plano.

---

## 2. Decisões por elemento

### 2.1 Estrelas — cubemap procedural seeded no `spaceEmissionTexture`
**Decisão**: gerar um cubemap de estrelas por mundo (seed do
[[worldgen-persistence-state|WorldGenState]]), via shader de geração executado
**uma vez** no carregamento (Graphics.Blit para as 6 faces de um
RenderTexture cubemap). Conteúdo:
- Milhares de estrelas: posição/tamanho/brilho por hash determinístico do seed;
  **temperatura de cor por corpo negro** (azuis quentes raras, brancas,
  amarelas, vermelhas frias — distribuição realista);
- **Via Láctea procedural**: faixa galáctica com fBm (2–3 oitavas) modulando
  densidade de estrelas + névoa luminosa fraca;
- HDR real (valores > 1) para o bloom do perfil ALP pegar as mais brilhantes.

**Por quê**: é o único caminho que dá ao mesmo tempo (a) fade atmosférico
físico no amanhecer, (b) reflexo correto na água futura, (c) custo por frame
≈ zero (1 sample de cubemap no pass do céu que já existe), (d) determinismo
por seed. Texture repetida/fundo chapado — o medo do design — não acontece:
cubemap não repete (é uma esfera única) e a rotação do firmamento +
variedade de magnitude quebram qualquer leitura de "textura".

**Slot para HDRI externo**: o campo é o mesmo (`spaceEmissionTexture`). Um
`Cubemap` externo (Polyhaven etc.) no Inspector substitui o procedural sem
tocar em NADA do resto. Arquitetura dual exigida pelo design: atendida por
um único campo.

**Cintilação (twinkle)**: análise honesta —
- Custom SkyRenderer com twinkle por estrela: qualidade máxima, manutenção
  péssima (clonar PBS). **Rejeitado.**
- Modular `spaceEmissionMultiplier` por noise temporal: shimmer global sutil,
  custo zero. **Aceito** (fase 1) — sutileza é o correto; cintilação forte só
  existe no horizonte na vida real.
- "Hero stars": ~60–100 quads emissivos em dome anexado à câmera, cintilação
  individual por fase — **fase 2 opcional** se a fase 1 não bastar (só as
  estrelas mais brilhantes cintilam de verdade; custo desprezível; refletem
  via SSR quando na tela, limitação aceitável).

### 2.2 Lua — corpo celeste nativo turbinado
**Decisão**: manter a lua como corpo celeste do HDRP (nada de quad/mesh
custom) e turbinar: `angularDiameter` 6–8° (fantasia), `surfaceTexture` com
slot no Inspector, fase Manual (já é), `earthshine` ligado (lado escuro
visível — detalhe que vende), `flareSize/falloff/tint` para o halo.

**Textura**: o **CGI Moon Kit da NASA (gratuito, domínio público)** é o ganho
visual mais barato de todo o sistema — crateras reais numa lua de 7° são o
"uau" da cena. **Fallback procedural funcional**: textura de crateras gerada
(fBm + círculos com rim) no mesmo slot, para o sistema nunca depender do
asset. Iluminação fisicamente consistente: nativa do shader do céu (fase +
earthshine), nada a reinventar.

### 2.3 Estrelas cadentes — evento raro, VFX próprio (justificado)
Única peça sem solução nativa (HDRP não tem "meteoros"; VFX Graph **não está
no projeto** — verificado no manifest). **Decisão**: sistema próprio pequeno
no padrão que o projeto já usa para fogo (`CombatVFX`/Shuriken):
- Agendamento pelo DayNightCycle: intervalo aleatório (Poisson, média
  configurável ~90 s, só à noite), **nunca em cadência fixa** — raridade é o
  que torna especial;
- Trajetória: ponto aleatório do domo alto, direção/velocidade/comprimento
  variados por evento; 0.4–1.2 s de vida;
- Visual: risco emissivo HDR (LineRenderer/quad esticado) com fade suave —
  o bloom do perfil ALP dá o glow sem custo extra; **partículas de detritos:
  sim** — um trail Shuriken de ~10–20 partículas por evento custa nada e
  dobra o realismo (recomendação aceita);
- Reflexo: via SSR quando na tela (evento de 1 s — limitação irrelevante).

### 2.4 Integração com o ciclo (sem transições bruscas)
`DayNightCycle` continua a **única fonte de tempo**. Novos deveres:
- animar `spaceRotation` (rotação do firmamento acompanha a hora);
- `spaceEmissionMultiplier` por curva (ajuste artístico fino + shimmer);
- agendar estrelas cadentes (só `IsNight`).
O desaparecimento das estrelas no amanhecer é **da atmosfera** (nativo). A
lua já nasce/se põe pelo arco existente.

### 2.5 Clima futuro (escalabilidade exigida)
Nada a preparar além do que a arquitetura já dá:
- **CloudLayer/VolumetricClouds renderizam por cima do céu** → encobrem
  estrelas/lua natural e parcialmente (nuvem esparsa deixa estrelas nos
  buracos). Estados de clima = trocar mapa de nuvem + opacidade no MESMO
  volume runtime que já usamos.
- Aurora: Local Volumetric Fog emissivo com textura 3D animada, por bioma
  (Tundra) — custom futuro, não bloqueia nada de agora.
- Chuva/neve/neblina: fog + partículas, ortogonais ao céu.

### 2.6 Água futura
Zero requisitos novos: estrelas, Via Láctea e lua vivem no céu do HDRP →
cubemap de reflexo, ambient probe, SSR e Water System já os refletem por
construção. (A exigência "não dificultar reflexos" foi critério eliminatório
na escolha do spaceEmissionTexture sobre qualquer solução de
partículas/quads para o campo estelar principal.)

---

## 3. Custos (GPU/CPU/memória)

| Componente | Custo por frame | Custo one-time | Memória |
|---|---|---|---|
| PBS (já em uso) | ~0.1–0.3 ms (pass do céu, re-render quando sol/rotação mudam) | precompute LUTs ao mudar atmosfera | baixa |
| Space emission | ~0 (1 sample no pass já existente) | — | ver cubemap |
| Cubemap estrelas 2048 RGBAHalf (runtime) | 0 | ~2–5 ms no load (Blit ×6) | ~100 MB — **usar 1024 (~25 MB) ou gerar no editor com BC6H (~8 MB)**; começar com 1024 runtime, medir |
| Corpos celestes (2) | ~0 (loop no shader do céu) | — | textura da lua 2k ≈ 5 MB |
| Rotação do firmamento | re-render do céu/frame (já acontece pelo sol em movimento — **custo já pago**) | — | — |
| CloudLayer (futuro) | ~0.1 ms + bake ao mudar | bake compute | baixa |
| Volumetric Clouds (futuro) | 1–2 ms+ | — | média |
| Estrelas cadentes | ~0 (1 evento raro, dezenas de partículas) | — | ~0 |
| Hero stars fase 2 | ~0 (≤100 quads) | — | ~0 |

CPU: tudo dirigido pelo `Update` do DayNightCycle já existente (sets de
parâmetros de volume — nanosegundos). Determinismo: `System.Random(seed)` na
geração; nenhuma dependência de tempo real.

---

## 4. Arquitetura de implementação (proposta)

```
DayNightCycle (existente — única fonte de tempo)
 └── NightSky (novo componente irmão, mesmo GameObject)
      ├── gera/possui o cubemap de estrelas (seed do WorldGenState)
      ├── aplica no PBS do volume runtime: spaceEmissionTexture/Multiplier/Rotation
      ├── configura a lua: angularDiameter, surfaceTexture (slot), earthshine, flare
      ├── ShootingStarSpawner (agendador + pool de 2-3 riscos reutilizáveis)
      └── Inspector: seed override, hdriOverride (Cubemap), moonTexture,
          curvas (multiplier por hora), parâmetros de raridade das cadentes
```

Compatibilidade futura: só APIs públicas e estáveis do HDRP (parâmetros de
volume + HDAdditionalLightData). O único shader novo é o **gerador** do
cubemap — roda fora do pipeline de render (escreve textura), imune a
mudanças internas do HDRP.

## 5. Riscos e mitigação
- **VRAM do cubemap runtime**: começar 1024, expor no Inspector; caminho de
  editor com BC6H documentado se preciso.
- **Qualidade da Via Láctea procedural vs HDRI**: slot dual já resolve; o
  procedural é o piso garantido, o HDRI é upgrade drop-in.
- **Shimmer global parecer artificial**: amplitude ≤5% e frequência baixa;
  desligável por curva.
- **Lua gigante + PCSS**: sombras da lua com penumbra enorme se
  `angularDiameter` alto for usado no cálculo de sombra — testar e, se
  preciso, sombra com diâmetro angular menor que o visual (parâmetros
  separados no HDRP: sombra usa `m_SunLightConeAngle`... verificar na
  implementação).
