# Estudo — Atributos do Dragão: o que existe, por que não funciona, e o que pode virar feature

> Auditoria completa do prefab `Assets/Prefabs/Unka Realistic.prefab` (instanciado em `Assets/Scenes/Main.unity`),
> dos ScriptableObjects que o alimentam, e proposta de reestruturação com base em jogos de criatura/pet.
>
> Julho/2026

---

## 1. Inventário — tudo que o dragão tem hoje

### 1.1 Componentes no prefab

O root `Unka Realistic` carrega 8 MonoBehaviours nossos (+ `Animator`, `CharacterController`).
`DragonAbilities`, `DragonDamageFeedback` e `DragonSounds` entram por código no `Awake`.

| Componente | Campos serializados | Papel |
|---|---:|---|
| `DragonController` | **85** | chão, decolagem, voo, pouso, nado, colisão, custos |
| `DragonVitals` | 14 | fome / energia / vida |
| `DragonGrowth` | 19 | tamanho, condição corporal, peso, velhice |
| `DragonAttributes` | 18 | os 3 atributos + nature + IV |
| `DragonFlight` | 1 (`profile`) | delega tudo ao `FlightProfile` |
| `DragonSounds` | lista de eventos | Wing Flap / Hurt (clips vazios) |
| `DragonPossession` | 0 | troca de skin/record |
| `DragonDissolve` | 2 | morte |

Fora do prefab, mas parte do dragão:

| Asset | Campos | Papel |
|---|---:|---|
| `FlightProfile` (SO) | **25** | toda a física de voo |
| `DragonRecord` (SO) | 8 + `DragonState` | identidade da linhagem |
| `DragonState` | 16 | o que um save grava |
| `DragonGenes` | **12** | genoma visual |
| `DragonSkin` (SO) | 13 | paleta / limites de cor |
| `DragonAttackData` (SO) | 24 × N ataques | habilidades data-driven |

**Total: ~210 campos serializados que descrevem um dragão.**

### 1.2 Os eixos de identidade que EXISTEM

```
Atributos gastáveis   Speed · Power · Resistance          14 pontos na vida inteira
IVs (sorteados)       ivSpeed/Power/Resistance/Vigor      0..31, × 0.1 = até +3.1 "pontos"
Nature                9 temperamentos                     ±10% em um atributo
Genes                 12 genes morfológicos e de cor      ZERO efeito mecânico
Crescimento           Scale · Condition · Age             8 multiplicadores derivados
```

### 1.3 O que cada atributo controla hoje

| | Velocidade | Poder | Resistência |
|---|---|---|---|
| efeitos | vel. máxima (`SpeedMul`), aceleração (`AccelMul`) | dano (`DamageMul`), raio da chama (`FlameSizeMul`), faro (`DominanceRadius`) | vida, energia, aceleração, fome, subida de decolagem, **teto de voo** |
| nº de efeitos | **2** | 3 | **6** |

### 1.4 O que o **crescimento** controla (e é muito mais rico)

`SpeedScale · AccelTime · RunSpeedMul · ClimbMul · SinkMul · EnergyCostMul · TurnAgilityMul · AccelAgilityMul · DashCooldownMul · BoostMul · ElderMul · GrowthSpeedMul · LifespanMul`

**13 multiplicadores** — quase o dobro dos atributos, e o jogador não escolhe nenhum.

---

## 2. Diagnóstico — por que o sistema não está bom

### P1. Os pontos são escassos demais para o número de efeitos
14 pontos totais (5 iniciais + 9 níveis), 3 destinos. Uma build "pura velocidade" tem 14 × 6% = **+84% de velocidade máxima** — mas na prática ninguém joga puro, então a diferença real entre duas builds fica em ~30–40% de *um número*. Isso não cria fantasia, cria decimal.

### P2. Resistência está sobrecarregada; Velocidade está vazia
Resistência é a escolha obviamente melhor: vida, energia, aceleração, fome, decolagem *e* teto de voo. Velocidade dá dois números que a escala do corpo já dá de graça (`SpeedScale = Scale`). Não há dilema — há uma resposta certa.

### P3. Nenhum atributo destrava uma AÇÃO
Todos multiplicam. Nada muda o *verbo*. Comparar com o efeito qualitativo que existe no jogo e não é dirigido por atributo: `flapsPerCycle = 2` (batidas por ciclo). Ir de 2 para 3 batidas muda o voo de forma que o jogador **sente**; +8% de energia máxima, não.

### P4. Os IVs são invisíveis e sem agência
IV 31 × `ivBonusPerPoint 0.1` = 3.1 pontos efetivos contra 14 gastos — ~22% do total, o que é *significativo*, mas o jogador não pode fazer nada a respeito. No Pokémon o IV só funciona porque existe o **EV**: 508 pontos que o jogador ganha *escolhendo o que caçar*. Sem o par IV+EV, o IV vira ruído no começo e nota de rodapé no fim.

### P5. A Nature promete personalidade e entrega multiplicação
`Stealthy`, `Brutal`, `Tenacious` são nomes de temperamento. O efeito é ±10% num valor efetivo já pequeno — na prática ±0,3 ponto. Nos jogos de referência o temperamento muda *comportamento* (Monster Rancher: o monstro **desobedece**; Digimon World: a disciplina muda **em que ele evolui**), não a terceira casa decimal.

### P6. O genoma é 100% cosmético
12 genes herdáveis, cruzamento mendeliano implementado, mutação implementada — e **nada disso toca a jogabilidade**. `wingFingers` (membrana da asa!) não afeta o planeio. `bodySpikes` não é armadura. `tint` não é camuflagem. Num jogo cujo pivô é *dinastia + genética*, esse é o maior desperdício do sistema: criar seletivamente não muda como o dragão joga, só como ele aparece.

### P7. ~180 dos ~210 campos são constantes mortas
Enumerando só os que gritam por um dono: `dashCooldown`, `dashIFrames`, `attackCancelWindow`, `boostImpulse`, `airDodgeYaw`, `flyDodgeTurn`, `flapsPerCycle`, `swoopConversion`, `windInfluence`, `sinkAtSpeed`, `autoLandMaxSlope`, `impactHeavy`, `staggerTime`, `safeFallTreeFraction`, `criticalEnergyCap`, `regenRestingWellFed`, `starveBelowHunger`, `buoyDepth`, `eatRange`, `gainKgPerMinute`... Cada um desses é uma feature esperando um atributo.

### P8. Falta a camada qualitativa que todo jogo do gênero tem
Passivas (Palworld), Abilities (Pokémon), Genes de combo (MH Stories), Mutações (ARK). São efeitos **nomeados, legíveis e herdáveis** — a matéria-prima de "olha o dragão que eu criei". Hoje não existe nada disso.

---

## 3. Pesquisa — o que os jogos do gênero fazem

| Jogo | Mecânica | O que aproveitar |
|---|---|---|
| **Pokémon** | IV (0–31, sorteado) + EV (508 pts, ganhos jogando, cap 252/stat) + Nature (±10%) + Ability | O **EV é o que dá agência**: o jogador escolhe o que caçar e isso esculpe o bicho. Ability = efeito nomeado, não número. |
| **Digimon World** | Stats 1–9999 + **Weight** + Happiness (−100..100) + Discipline (0..100) + care mistakes; a evolução é *gated* por combinações desses | Peso e cuidado como eixos de primeira classe. A forma final é consequência de como você criou. Já temos `condition01` e `Starve01` — falta o gate. |
| **Monster Rancher** | Fatigue, Stress, Loyalty = (Fear/2 + Spoil/2), **Form**; estresse/fadiga não zerados **encurtam a vida** | Loop de cuidado com custo real: forçar o dragão cobra expectativa de vida. Temos `lifespanMinutes` sem nada ligado nele. |
| **Palworld** | Até **4 passivas** por Pal, sorteadas, **herdáveis**, com trade-off; Work Suitability; Partner Skill | O molde exato do que falta: slots de traço herdável com prós e contras. |
| **ARK** | Stats por nível + herança (70% chance do melhor pai) + **mutação** (+2 níveis num stat + mudança de cor) + imprinting (+20%) | Mutação como *outlier* visível — o motor de rejogabilidade de linhagem. Nosso `mutationChance` só re-sorteia dentro da faixa normal; não cria outlier. |
| **MH Stories 2** | **Bingo 3×3 de genes**: 3 na mesma linha (cor ou ícone) = bônus grande; Rite of Channeling transfere genes | Sinergia de conjunto: o valor não está no gene isolado, está na combinação. Casa com nossa grade de blend shapes. |
| **Day of Dragons / Path of Titans** | Crescimento por tempo+alimentação; árvores de mutação (Breeding/Movement/Survival); subespécies com bônus de stat | Confirma que crescimento-por-tempo é o padrão do nicho — e que a diferenciação vem de **árvores/subespécies**, não de +% . |

**Padrão comum a todos:** cada criatura tem (a) números herdados que o jogador não controla, (b) números que o jogador esculpe **jogando**, e (c) efeitos nomeados que mudam o verbo. Nós temos só (a), e um punhado de pontos manuais em (b).

---

## 4. Proposta A — reestruturar os eixos: 3 → 6 atributos

O problema P2 (Resistência faz tudo, Velocidade faz nada) se resolve quebrando os dois eixos sobrecarregados e criando o que hoje é órfão.

| Novo atributo | Vem de | Dono de |
|---|---|---|
| **Força** (`Might`) | Poder | dano físico, bote das asas, subida, derrubar/carregar presa, impacto de investida |
| **Chama** (`Ardor`) | Poder | tamanho/duração/área do fogo, queimadura, custo do sopro |
| **Agilidade** (`Agility`) | Velocidade | vel. máxima, giro, dash, esquiva, cast/recover de golpes |
| **Vigor** (`Vigor`) | Resistência | vida, regeneração, resistência a stagger, dano de queda, fome |
| **Fôlego** (`Wind`) | Resistência | energia máxima **e custo de tudo**, batidas por ciclo, teto de voo |
| **Instinto** (`Instinct`) | *novo* | faro, furtividade, janelas (i-frames, cancel, bônus de timing), leitura de vento, pouso preciso |

**Por que Instinto é o mais importante dos novos:** ele é o atributo que compra *janelas*, não números. `dashIFrames`, `attackCancelWindow`, `flapBonusPower`, `windInfluence`, `autoLandMaxSlope` — todos mudam como o jogo **responde**, não quanto ele soma. É o antídoto direto ao P3.

> Migração: `Attribute` grava como índice (contrato de serialização documentado em `DragonAttributes.cs:126`).
> `Speed=0, Power=1, Resistance=2` precisam ficar onde estão; os três novos entram no fim (3,4,5).
> Records antigos continuam válidos, com os novos eixos em 0.

**Orçamento de pontos:** com 6 eixos, 14 pontos é pouco demais. Sugestão: `maxLevel 10 → 20` e 2 pontos por nível (~45 pontos na vida), com o crescimento levando ~2× mais tempo — ou manter 10 níveis e dar 3 pontos por nível.

---

## 5. Proposta B — a tabela: cada campo serializado e quem deveria dirigi-lo

Legenda: **F**orça · **C**hama · **A**gilidade · **V**igor · **Fô**lego · **I**nstinto · *Gene* · *Escala/Condição* · — (deixar constante)

### 5.1 `DragonController` — chão

| Campo | Valor | Dono proposto | Feature que isso cria |
|---|---:|---|---|
| `walkSpeed` | 5 | **I** | passo de caçada mais rápido: stealth deixa de ser lento demais para valer a pena |
| `runSpeed` | 10 | A *(já)* | — |
| `groundAccel` | 35 | A + F *(já)* | — |
| `groundDecel` | 25 | *Escala/Condição* (inverso) | **derrapada de peso**: colossal escorrega ao parar, filhote crava. Já há `AccelAgilityMul`; falta o freio |
| `turnRateIdle` / `turnRateRun` | 540 / 240 | **A** | raio de curva como stat: o dragão ágil vira no lugar em corrida plena |
| `gravity` | 28 | — | |

### 5.2 Decolagem

| Campo | Valor | Dono | Feature |
|---|---:|---|---|
| `runningTakeoffFraction` | 0.5 | **F** | dragão forte decola de menos corrida — e no topo da Força decola parado sem penalidade |
| `takeoffHopClimb` | 6 | F | altura do salto |
| `takeoffLaunchClimb` | 12 | **F** | o bote das asas: a diferença entre sair do chão e *saltar* |
| `takeoffLaunchForward` | 3 | F | |
| `takeoffLockMinimum`/`MaxLock`/`Grace` | — | — | técnicos |

### 5.3 Dash / Boost / Esquiva — o coração do hack & slash

| Campo | Valor | Dono | Feature |
|---|---:|---|---|
| `dashSpeedMul` | 1.7 | **A** | distância do dash |
| `dashDuration` | 0.35 | A | |
| `dashCooldown` | 0.6 | A + *Escala (já)* | |
| `dashIFrames` | 0.3 | **I** | **janela de invulnerabilidade como stat** — um dragão de instinto atravessa o golpe |
| `boostImpulse` | 8 | F + *Escala (já)* | |
| `boostMaxOverspeed` | 1.25 | A | teto de overspeed |
| `boostCooldown` | 1 | **Fô** | frequência do boost |
| `boostIFrames` | 0.25 | I | |
| `boostCost` | 6 | Fô | |
| `airDodgeYaw` | 18.2 | A | ângulo do juke |
| `flyDodgeTurn` | 90 | **A** | quanto o voo vira na esquiva completa |
| `flyDodgeDuration` | 0.45 | A (menor) | esquiva mais rápida |
| `flyDodgeIFrames` | 0.4 | I | |
| `flyDodgeCost` | 8 | Fô | |
| `attackCancelWindow` | 0.4 | **I** | **cancelar golpe mais cedo** — muda a fluidez do combo inteiro. Provavelmente o item mais impactante da tabela |

### 5.4 Voo

| Campo | Valor | Dono | Feature |
|---|---:|---|---|
| `minFlySpeed` | 6 | **Fô** | voa mais devagar sem estolar → "planador" vira arquétipo |
| `cruiseSpeed` | 14 | A | |
| `maxFlySpeed` | 26 | A *(já)* | |
| `flyAccel` | 22 | A | |
| `turnSpeedAir` | 95 | **A** | |
| `bankAngle` / `pitchAngle` | 48 / 28 | A (visual) | curva mais agressiva *aparece* |
| `climbRate` | 7.5 | F + Fô | |
| `diveRate` | 14 | *Escala* + F | mergulho de colossal é uma bomba |
| `glideSink` | 1.6 | ***gene* `wingFingers`** | **envergadura passa a existir mecanicamente** |
| `stallSink` | 9 | Fô | |

### 5.5 Pouso — território inteiro sem dono

| Campo | Valor | Dono | Feature |
|---|---:|---|---|
| `landMaxSpeed` | 11 | **A** | pousa vindo mais rápido |
| `landApproachProbe` / `landDescendRate` / `landFlareRate` | 16 / 16 / 3 | **I** | **pouso de precisão** |
| `autoLandHeight` | 1.6 | I | |
| `autoLandMaxSlope` | 0.7 | **I** | **pousar em encosta íngreme** — destrava *lugares do mapa*. É acesso, não número |

### 5.6 Queda e colisão

| Campo | Valor | Dono | Feature |
|---|---:|---|---|
| `safeFallTreeFraction` | 0.7 | **V** | cai de mais alto sem se machucar |
| `fallDamageAtDouble` | 35 | V + *gene* densidade | |
| `impactLight` | 0.2 | *Escala* + F | |
| `impactHeavy` | 0.5 | **V + Escala** | **aríete**: colossal atravessa a copa sem cambalear (e derruba a árvore) |
| `impactSpeedLoss` | 0.6 | F (inverso) | conserva velocidade na batida |
| `impactKnockDown` | 6 | V (inverso) | |
| `staggerTime` | 1.2 | **V + I** | recupera o desequilíbrio mais rápido |
| `impactEnergyCost` | 6 | Fô | |

### 5.7 Natação

| Campo | Valor | Dono | Feature |
|---|---:|---|---|
| `swimSpeed` / `swimBackSpeed` / `swimTurnSpeed` / `swimAccel` | 3.4 / 1.2 / 85 / 4.5 | A + *gene* aquático | linhagem aquática vira uma build |
| `buoyDepth` | 0.85 | ***Condição corporal*** | **gordo flutua, magro afunda** — sutil, realista, de graça (já temos `condition01`) |
| `swimCost` | 2 | Fô | |

### 5.8 Custos de energia — tratar como bloco

`runCost 3.5` · `flapCost 3.5` · `climbCost 7` · `glideCost 0.3` · `takeoffCost 6` · `dodgeCost 8` · `fireCost 15` · `attackCost 3`

→ **Fôlego** aplica um multiplicador global de *eficiência*.

Isso é importante: hoje Resistência dá *mais barra*. Dar **menos consumo** é multiplicativo com tudo e cria um arquétipo verdadeiro — o dragão de longa distância, que voa a tarde inteira. `fireCost 15` sob **Chama** separa o dragão que cospe fogo o tempo todo do que cospe uma vez e fica sem ar.

### 5.9 `DragonVitals`

| Campo | Valor | Dono | Feature |
|---|---:|---|---|
| `maxHunger` | 100 | *Escala* + *gene* metabolismo | estômago grande |
| `hungerDecay` | 0.01 | V *(já)* | |
| `criticalHunger` | 25 | **V** | aguenta mais fome antes de degradar |
| `starvationDamage` | 2 | V | |
| `criticalEnergyCap` | 60 | **V** | com fome, ainda voa |
| `regenIdle` / `regenResting` | 3 / 10 | V + *Nature* | temperamento sereno descansa melhor |
| `regenSlow` / `regenRestingWellFed` | 0.35 / 2.5 | **V** | regeneração passiva é o que permite jogar agressivo |
| `regenMultiplier` / `drainMultiplier` | 5 / 0.1 | — | knobs globais de balanço |

### 5.10 `DragonGrowth`

| Campo | Valor | Dono | Feature |
|---|---:|---|---|
| `hatchlingScale` / `colossalScale` | 0.45 / 1.7 | ***gene* de porte** | **linhagens grandes vs. pequenas** — e `Scale` já alimenta 6 sistemas, então o gene rende sozinho |
| `fullGrowthMinutes` | 25 | ivVigor *(já)* | |
| `mealGrowthBonus` | 0.0012 | *gene* metabolismo | |
| `gainKgPerMinute` / `lossKgPerMinute` / `starvingLossKgPerMinute` | 1 / 1 / 3 | ***gene* metabolismo** | metabolismo lento = engorda fácil, resiste à fome, é pesado. Rápido = magro, ágil, precisa caçar sempre. É o *Weight* do Digimon World e o *Form* do Monster Rancher |
| `adultHealthyWeightKg` | 950 | *gene* densidade | densidade óssea: peso e dano de queda |
| `oldAgeMinutes` / `lifespanMinutes` | 40 / 55 | ivVigor *(já)* + **fadiga acumulada** | fecha o loop de cuidado |
| `elderPenalty` | 0.25 | **V** | envelhecer com graça |
| `starveBelowHunger` | 0.25 | V | |

### 5.11 `FlightProfile` — o achado maior

25 números de voo, **um único** tocado por atributo (`takeoffClimbTime`).

| Campo | Valor | Dono | Feature |
|---|---:|---|---|
| `takeoffClimbTime` | 3 | V *(já)* | |
| `takeoffClimbRate` | 9 | F | |
| `takeoffClimbEnergyPerSec` | 4 | Fô | |
| **`flapsPerCycle`** | **2** | **Fô (inteiro!)** | **2 → 3 → 4 batidas por ciclo.** Mudança qualitativa, não decimal. É o melhor upgrade disponível no sistema inteiro |
| `flapMinInterval` | 0.22 | A | cadência da batida |
| `cycleRecovery` | 0.3 | Fô | |
| `flapBonusLift` / `flapBonusForward` | 9 / 0.8 | **F** | recompensa do timing |
| `flapBonusPower` | 2 | **I** | **janela do timing mais generosa** — skill-assist por atributo |
| `flapLift` / `flapForwardBoost` | 9 / 1.1 | F | |
| `maxRiseSpeed` | 12 | F | |
| `sinkAtSpeed` / `sinkAtStall` | 0.7 / 3 | ***gene* `wingFingers`** + *Condição* | asa larga = planador; gordo afunda |
| `verticalResponse` | 9 | A | |
| `diveSink` / `diveOverspeed` | 14 / 1.3 | *Escala* + F | |
| `swoopConversion` | 0.35 | **I / A** | o loop mergulho→rasante fica rentável |
| `energyPerFlap` | 2 | Fô | |
| **`windInfluence`** | **1** | **I** | **ler o vento**: ganha mais das térmicas do `AirflowField`. Casa direto com as montanhas do horizonte |
| `ceilingSoftBand` | 40 | Fô | quão suave é o ar rarefeito |

### 5.12 `DragonAttackData` (por ataque)

| Campo | Dono | Feature |
|---|---|---|
| `baseDamage` | F *(já)* | |
| `energyCost` | **Fô** | |
| `cooldown` | **I** | |
| `castTime` | **A** | conjura mais rápido |
| `animationLock` | **A** | recupera mais rápido → mais golpes por janela |
| `areaRadius` | C *(já, só `isFire`)* | |
| `burnDamagePerSecond` / `burnDuration` / `fireAreaDuration` | **C** | **separar Chama de Força é o que faz "dragão de fogo" ≠ "dragão de garras"** |
| `projectileSpeed` | C + I | |
| `SlotCount` (=4) | **I / nível** | 3 → 5 slots de loadout |

---

## 6. Proposta C — combinações: features que nascem de dois ou mais eixos

Estas são as que valem mais, porque não existem como campo hoje — nascem do cruzamento.

| Feature | Fórmula | Por que é boa |
|---|---|---|
| **Carregar presa** | `Força × Escala > pesoDaCarcaça` | verbo novo: levar a caça pro ninho. `Carcass` já tem nutrição; falta massa |
| **Investida (aríete)** | dano de colisão em voo = `Força × massa × velocidade²` | hoje colidir só *atrapalha*. Com Força vira arma — e derruba árvores |
| **Sopro sustentado** | duração = `Fôlego`, largura = `Chama` | segurar RMB em vez de tocar. Dois eixos, um botão |
| **Furtividade real** | raio de detecção da fauna = `f(−Instinto, +Escala, −Condição)` | **crescer é perder furtividade.** Tensão de design de graça: o colossal não se esconde |
| **Camuflagem por bioma** | distância `tint`/`bodyMaterial` (genes) ↔ paleta do bioma | dá **função mecânica à cor**. Criar um dragão branco pra Tundra vira estratégia |
| **Contra-ataque** | esquivar dentro dos i-frames devolve i-frames + abre janela de cancel | `Agilidade × Instinto`. Puro hack & slash |
| **Frio de altitude** | acima de `0.8 × MaxAltitude`, dano contínuo mitigado por `Vigor` | o teto de voo deixa de ser só sustentação e vira sobrevivência |
| **Reserva de gordura** | fome zerada consome `condition01` antes da vida | `Vigor × Condição`. Metade já existe (`Starve01`), falta o buffer |
| **Faro estendido** | `DominanceRadius` também revela eventos, fauna e biomas no minimapa | `Poder × Instinto`. `DragonMinimap.rangeOverride` já lê o faro |
| **Ninho / território** | raio de dominância vira área possuída: fauna respawna melhor, rivais aparecem | conecta atributo → mundo → linhagem |

---

## 7. Proposta D — as três camadas que faltam

### D1. Hábito (o EV do Pokémon) — agência sem menu

O dragão ganha proficiência **no que faz**. Contadores em `DragonState`, cap total forçando escolha:

| Ação já instrumentada | Alimenta |
|---|---|
| metros voados / batidas de asa | Fôlego |
| dano corpo-a-corpo causado | Força |
| dano de fogo / queimaduras aplicadas | Chama |
| tempo em stealth · presas mortas sem ser detectado | Instinto |
| dano sofrido e sobrevivido · quedas | Vigor |
| tempo em velocidade > 80% do máximo | Agilidade |

Cap tipo Pokémon (ex.: 500 total, 200 por eixo). **Isto é o que falta para o IV fazer sentido** (P4): o IV vira o teto do talento e o hábito vira a escolha do jogador. E resolve P1 sem inflar os pontos manuais.

`DragonVitals.TotalEaten` já é exatamente esse padrão — só precisa de irmãos.

### D2. Traços herdáveis (as passivas do Palworld) — a camada qualitativa

2–4 slots por dragão, sorteados no nascimento, herdados com chance, **sempre com trade-off**:

| Traço | Efeito | Custo |
|---|---|---|
| *Sangue Frio* | −30% custo de energia na Tundra | +20% no deserto |
| *Ossos Ocos* | −15% peso, +planeio | −20% vida, +dano de queda |
| *Estômago de Ferro* | carcaça velha nutre igual | — |
| *Termonauta* | dobro do ganho das térmicas | — |
| *Insone* | não precisa descansar | fome cai 20% mais rápido |
| *Fôlego de Forja* | sopro não tem cooldown | −30% vida máxima |
| *Guelras Vestigiais* | nada rápido, respira submerso | −15% teto de voo |
| *Couraça* | `bodySpikes` viram armadura | −15% agilidade |

É a camada que gera conversa ("meu dragão tem Ossos Ocos + Termonauta"). Nenhum número faz isso.

### D3. Genoma com efeito mecânico — resolve P6

| Gene atual | Efeito proposto |
|---|---|
| `wingFingers` | **planeio** (`sinkAtSpeed/AtStall`, `glideSink`) e giro no ar |
| `armSpikes` / `legsSpikes` / `neckSpikes` / `bodySpikes` | armadura passiva + dano de contato; custo em peso e agilidade |
| `headVariant` | viés de ataque: crânio pesado = mordida; alongado = sopro |
| `hornVariant` | investida e intimidação (alcance do rugido) |
| `tint` / `wingHue` | camuflagem por bioma |
| `bodyMaterial` | resistência elemental (fogo / gelo) |
| **novos:** porte, metabolismo, densidade óssea, afinidade de bioma | ver §5.10 |

Bônus — **bingo de genes** (MH Stories): 3 genes da mesma família (ex.: 3 morfologias de asa acima de 70) destravam um bônus nomeado. A grade de blend shapes já existe em `DragonBlendShapes`.

### D4. Mutação de verdade (ARK) — motor da linhagem

`DragonGenes.Breed` já tem `mutationChance = 0.08`, mas ela só **re-sorteia dentro da faixa normal**. Mutação real = valor **fora** do intervalo dos pais (ex.: IV do filho pode passar de 31, gene contínuo pode passar de 100), marcada visualmente (cor fora da paleta). É o que faz criar seletivamente por 20 gerações valer a pena.

---

## 8. Arquitetura — o refactor que torna tudo isso viável

Hoje `DragonAttributes` expõe uma propriedade por consumidor: `SpeedMul`, `AccelMul`, `DamageMul`, `FlameSizeMul`, `MaxHealthMul`, `MaxEnergyMul`, `HungerDecayMul`, `TakeoffClimbMul`, `MaxAltitude`, `DominanceRadius`. Com ~60 modificadores propostos, isso vira 60 propriedades e 60 call sites espalhados — insustentável.

Proposta: **pipeline de stats**.

```
enum DragonStat { RunSpeed, TurnRate, DashIFrames, FlapLift, FlapsPerCycle, GlideSink, ... }

float Get(DragonStat s) = base[s] × Π( sources )
    sources = atributos · IVs · nature · hábito · genes · escala · condição · velhice · traços · status
```

Vantagens: um só lugar para balancear; a ficha (`DragonStatsMenu`) mostra a decomposição sozinha ("Velocidade de corrida 14.2 = 10 base × 1.28 atributo × 1.15 escala × 0.96 condição"); buffs temporários e status entram sem tocar consumidor; e cada campo novo do Inspector já nasce dirigível.

Sem esse passo, cada feature da §5 custa uma propriedade + uma edição no `DragonController`. Com ele, custa uma linha de tabela.

---

## 9. Priorização sugerida

**Passo 1 — destravar o que já existe (barato, impacto alto)**
- `flapsPerCycle` sob Fôlego (2→4) — a maior mudança de sensação por linha de código
- `attackCancelWindow` + `dashIFrames` + `flyDodgeIFrames` sob Instinto
- Bloco de custos de energia sob Fôlego (eficiência, não barra)
- `autoLandMaxSlope` sob Instinto (acesso a lugares)
- `buoyDepth` sob Condição (grátis, charmoso)

**Passo 2 — o refactor**
- Pipeline de stats (§8)
- 3 → 6 atributos, acrescentando no fim do enum

**Passo 3 — as camadas novas**
- Hábito/EV (resolve o IV)
- Genoma com efeito mecânico (resolve o pivô de linhagem)
- Traços herdáveis
- Mutação outlier

**Passo 4 — combinações**
- Carregar presa, investida, sopro sustentado, furtividade real, camuflagem, ninho

---

## Fontes

- [Individual values — Bulbapedia](https://bulbapedia.bulbagarden.net/wiki/Individual_values) · [Effort values — Bulbapedia](https://bulbapedia.bulbagarden.net/wiki/Effort_values) · [EVs, IVs e Natures — Game8](https://game8.co/games/Pokemon-Scarlet-Violet/archives/386382)
- [Digimon World — Evolution Requirements (GameFAQs)](https://gamefaqs.gamespot.com/ps/913684-digimon-world/faqs/73845/evolution-requirements)
- [Monster Rancher 2 — Complete Guide (Steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=2677436703) · [Raising methods (LegendCup)](https://legendcup.com/raisingmethodsmr2.php)
- [Palworld — Passive Skills Wiki](https://palworld.wiki.gg/wiki/Passive_Skills) · [Partner Skills — paldb](https://paldb.cc/en/Partner_Skill)
- [ARK — Mutations Wiki](https://ark.fandom.com/wiki/Mutations) · [Creature stats calculation](https://ark.fandom.com/wiki/Creature_stats_calculation) · [Imprinting](https://ark.fandom.com/wiki/Imprinting)
- [MH Stories 2 — Gene guide (PC Gamer)](https://www.pcgamer.com/monster-hunter-stories-2-gene-rainbow-rite-of-channeling/) · [Bingo Board (Gameranx)](https://gameranx.com/updates/id/242545/article/monster-hunter-stories-2-how-to-boost-monsties-with-gene-channeling-bingo-board-guide/)
- [Day of Dragons (Steam)](https://store.steampowered.com/app/1088090/Day_of_Dragons/) · [Path of Titans](https://pathoftitans.com/)
