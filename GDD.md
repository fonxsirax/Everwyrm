# EVERWYRM — Game Design Document (GDD v2.0)

> v2.0 — pivô de "vida de um dragão" (estilo The Isle) para **linhagem de dragões** com base
> persistente, genética e progressão permanente. A v1.0 está no histórico do git.
> Itens marcados **[ABERTO]** ainda não foram decididos; **[IMPL]** já existe no código.

## High Concept

**Administre uma linhagem de dragões através das gerações.**

Everwyrm é um Sandbox Survival onde o jogador não controla um único dragão, mas uma
**dinastia**. Dragões nascem de ovos na base do jogador, crescem, atingem o auge colossal,
envelhecem e morrem — mas a linhagem continua: filhotes herdam golpes, natureza e
potencial genético dos pais. O progresso real vive na **base** e na **genética acumulada**,
não no indivíduo.

A morte deixa de ser perda total (o problema de retenção do gênero The Isle / Path of
Titans) e vira parte do ciclo. Comps comerciais: V Rising (base + criatura poderosa),
Palworld/ARK (breeding com herança de stats), WolfQuest (vida animal com família),
Niche (genética como jogo).

## Pilares do Projeto

1. **Legado permanente** — todo minuto jogado deixa algo que sobrevive à morte do dragão:
   base melhor, genes melhores, golpes aprendidos, pergaminhos, território conhecido.
2. **Crescimento visível** — o dragão muda de tamanho, forma e capacidade ao longo da vida;
   a linhagem muda de qualidade ao longo das gerações.
3. **Gameplay emergente** — sem missões roteirizadas; histórias nascem de fome, território,
   clima, IA, eventos e genética ("o filhote albino da minha terceira geração...").
4. **Mundo que recompensa exploração** — biomas, cavernas, bosses e eventos puxam o jogador
   pra longe da base; a base puxa de volta.
5. **Simplicidade elegante e modular** — poucos sistemas, muitos cruzamentos. Tudo
   data-driven (espécie/ataque/evento novo = asset novo, zero código) e serializável.

## Os Dois Loops

### Loop de sessão (20 min – 1 h)
```
Sair da base → explorar/caçar → comer (nutrição molda atributos) → evento/descoberta
→ arriscar mais longe (boss? caverna? clã?) → voltar com recursos/pergaminho → construir/incubar
```

### Meta-loop de geração (dezenas de horas)
```
Ovo → filhote → adulto → colossal (auge) → cio/reprodução → velhice → morte
   ↘ novos ovos herdam genética → escolher o sucessor → linhagem melhora →
     desafios maiores ficam viáveis → repetir
```

## O Jogo Essencial (MVP — a espinha)

Teste de uma frase: **"Everwyrm é um jogo bonito onde você vive a vida de um dragão — e
quando ele morre, a vida continua pela linhagem."** Tudo que não serve a essa frase é
camada (Geladeira), não espinha. A primeira metade da frase já está ~85% construída; o
MVP é construir a segunda.

Seis peças, nada mais:

1. **Viver** — caçar, comer, crescer, voar. **[IMPL]**
2. **Envelhecer** — idade com o jogo aberto, velhice enfraquece, morte natural
   (extensão do DragonGrowth).
3. **Uma base** — fixa, pequena, pré-criada no spawn. **Uma única construção: o ninho**
   (revogar + 10 galhos). Sem tiers, sem paredes, sem expansão.
4. **Gerar** — fêmea no cio → evento de parceiro (simples, quase-cutscene: dragão
   aparece, cortejo curto, ovo no ninho — sem combate/rival) → 1 ovo → incuba.
5. **Herdar** — filhote nasce com natureza + IVs sobre os **3 atributos atuais** + golpes
   dos pais. (Vitalidade/Instinto ficam na Geladeira; vida longa/curta pode vir da
   natureza — ex.: Sereno vive mais.)
6. **Continuar** — o dragão morre, a UI de linhagem mostra os vivos/ovos, o jogador
   assume o herdeiro. Save modular cobrindo só a tabela deste GDD.

**Critério de pronto**: em uma sessão, o jogador vê um ovo virar um herdeiro visivelmente
melhor (ou pior!) que o pai, e sente que perder o velho não foi perder o jogo.

**Contrato de escopo** (as duas regras do projeto):

- Toda ideia nova entra na **Geladeira** por padrão, nunca no escopo. Promover algo exige
  a espinha jogável primeiro.
- Pergunta aberta que não bloqueia a espinha recebe uma **resposta-padrão** (a mais
  barata) e só é revisitada se doer no playtest. Ver seção "Decisões-padrão".

O MVP corresponde às fases **F0–F2** do roadmap. Nada do corte acima toca no visual: o
apelo gráfico vive no mundo, no dragão e nos VFX — já construídos.

## Mundo

**[IMPL]** Mundo praticamente infinito por streaming de tiles determinísticos (seed),
5 biomas contínuos: **Campos, Floresta Antiga, Montanhas Rochosas, Tundra, Deserto
Rochoso** (com oásis raros). Lagos, riachos na floresta, ciclo dia/noite real (dia de 30
min, lua com fases, céu estelar por seed), neve ambiente, correntes de ar nas montanhas.

- Pântano e Vulcões (v1.0): **[ABERTO]** — rebaixados de "bioma" para possíveis
  landmarks/eventos (ex.: campo vulcânico como arena de boss), salvo decisão em contrário.
- **Cavernas** *(novo)*: pontos de entrada no mundo levam a interiores explóriáveis no
  estilo Stardew Valley (progressão por profundidade, recursos raros, perigo crescente).
  **[ABERTO]**: instanciadas (cena separada — barato) vs. escavadas no mundo (caro).
- **Alterações permanentes** do mundo (claims de base, futuras cicatrizes) são salvas como
  decisões de runtime no estado do mundo — mesmo padrão do lago inicial **[IMPL]**.

## Bases

A base é o coração da progressão permanente.

- O jogador começa com **1 base pequena já criada** (spawn). Meta futura: até 4 bases pelo
  mapa (reduzem deslocamento, criam objetivos de longo prazo). **[ABERTO]** como se
  desbloqueiam as demais.
- **Claim ("revogar terra")**: a base ocupa um espaço de terra revogado — a vegetação da
  área some e o terreno é reservado ao jogador. Tamanhos: **Pequena / Média / Grande**.
  **[ABERTO]** como o tamanho cresce.
  - *Nota técnica*: mesmo padrão do lago garantido do spawn — lista de claims (centro,
    raio, tier) no estado do mundo; aplaina o heightmap e mascara o scatter de vegetação.
- **Construção sem inventário**: o dragão não tem inventário (conceito não existe no
  jogo). Para construir, o jogador **revoga a estrutura** (ninho, parede, etc.), ela
  aparece incompleta/fantasma, e ele traz os itens necessários **um a um** (ex.: ninho =
  10 galhos). Capacidade de carga pode escalar com o tamanho do dragão (filhote carrega 1
  galho; colossal derruba a árvore). **[ABERTO]** — todo o sistema de
  construção/recursos/possível inventário será revisitado.
- Funções da base: nascimento e incubação de ovos · **troca de dragões (via UI)** ·
  armazenamento de recursos · expansão da progressão · ponto seguro da linhagem.
- Dragões não-controlados **não aparecem na base** (economia de meshes). Colocar um dragão
  visível como "enfeite" é opcional. A troca é por interface.

## Ciclo de Vida

Vida-alvo inicial: **~30 horas de relógio com o jogo aberto** (ajustável; era 170h na
proposta original — número final é tuning). Regras do relógio:

- A idade avança **enquanto o jogo está rodando**, para todos os dragões da linhagem —
  **nunca offline**.
- **Fome/energia só drenam no dragão controlado.** Dragões guardados envelhecem, mas não
  passam fome.

| Fase | Descrição |
|---|---|
| **Ovo** | Incuba na base. **[ABERTO]** tempo/aceleração de incubação. |
| **Filhote** | Jogável. Modelo: linha *Little Dragons* (Malbers) — compra futura; fallback: Unka em escala reduzida **[IMPL]**. Frágil, voo limitado; fase de aprender o mundo. |
| **Adulto** | O corpo do jogo atual **[IMPL]**: caça, voo pleno, combate, cio. |
| **Colossal** | O **auge tardio**: mesma mesh em escala muito maior, maior poder destrutivo da vida do dragão. Ignora alguns colliders (vegetação). Conteúdo endgame é calibrado pra ele. |
| **Velhice** | Continua imenso, mas **todos os atributos declinam** — voa mais baixo, corre pior, regenera menos. A ficha/UI mostra a fase e a expectativa de vida (morte natural nunca é surpresa). |
| **Morte** | Natural (velhice) ou vida chegando a 0. O jogador troca para outro dragão/ovo na base. |

Aparência acompanha a idade (escala + blend shapes **[IMPL]**; meshes por fase quando os
assets existirem).

## Genética e Linhagem

Cada dragão é um **registro** (DragonRecord) com:

- **Natureza** — personalidade que enviesa atributos (ex.: Dócil +Vitalidade/−Poder,
  Agressivo +Poder/−Instinto...). Lista proposta na seção de Atributos.
- **IVs (Individual Values)** — potencial genético por atributo, **oculto na UI**: o
  jogador não vê números, mas percebe pela ficha que "os atributos deste dragão não estão
  bons". Um dragão pode nascer melhor ou pior que outro.
- **Golpes herdados** — ataques (assets) que os pais conheciam podem vir de nascença.
- **Herança de stats** — "como os jogos fazem normalmente" (ARK/Pokémon): cada IV do
  filhote sorteia entre o do pai e o da mãe; natureza sorteada com viés às dos pais.
- **Mutação** — proposta: chance rara de IV fora da faixa dos pais ou golpe inesperado,
  para a linhagem não estagnar matematicamente. **[ABERTO]** confirmação e taxas.

### Reprodução

1. Fêmea adulta entra em **cio** (um ou mais períodos antes da velhice).
2. Acasalamento: com **evento aleatório** (dragão parceiro aparece no mundo) → ovo surge
   automaticamente na base; ou **entre dois dragões do jogador** (fêmea no cio).
3. Outras formas de adquirir ovos podem existir (ovo selvagem em evento, recompensa de
   boss...). **[ABERTO]**.
4. **[ABERTO]** cap de dragões vivos por base/linhagem.

### Fim de linhagem

Sem game over dedicado por enquanto: o jogo suporta **múltiplos saves e voltar a um ponto
salvo**. Se a linhagem acabar, o jogador recarrega. (Rede de segurança adicional — ovo
selvagem garantido — fica como opção futura.)

## Atributos do Dragão (rework de 6 atributos — jul/2026) **[IMPL]**

> Esta seção é o **mapa de ajuste** do sistema de atributos: onde cada número vive e
> quem ele governa. Todos os campos citados são `[SerializeField]` no componente
> `DragonAttributes` do prefab `Unka Realistic` (ajuste no Inspector) e podem ser
> simulados sem jogar em **Tools > Everwyrm > Balanço do Dragão**.

### Como os atributos sobem — SEM pontos

Não existe "level up com distribuição de pontos". Os atributos crescem **sozinhos**
com a **MATURIDADE** do dragão. Cada atributo tem sua própria **curva** (`AnimationCurve`
no Inspector), então uns chegam cedo e outros só no fim.

```
Maturidade = Curva( growthWeight·Crescimento + (1−growthWeight)·Idade )   [0..1]
             × (1 − elderDecline·Velhice)          ← a velhice corrói

Atributo   = CurvaDoAtributo(Maturidade) × maxAttribute       ← base pela idade
           + (IV / maxIV) × maxAttribute × ivInfluence        ← talento genético
           , tudo × Natureza (±natureModifier)
```

Campos de tuning (em `DragonAttributes`): `maxAttribute` (escala, default 10),
`growthWeight` (peso do tamanho vs. idade), `maturityCurve`, `elderDecline`,
`natureModifier`, `maxIV`, `ivInfluence`, `maxTier`, e uma `AnimationCurve` por
atributo (`agilityCurve`, `mightCurve`, `vigorCurve`, `ardorCurve`, `windCurve`,
`instinctCurve`).

### Os 6 atributos e o que cada um governa

Migração do sistema de 3 (Velocidade→**Agilidade**, Poder→**Força**,
Resistência→**Vigor**), mais 3 novos (Chama, Fôlego, Instinto):

| Atributo | Governa (multiplicador → onde é consumido) | Campo do ganho |
|---|---|---|
| **Força** (Might) | dano físico (`DamageMul`→DragonAbilities) · força do Wing Boost (`BoostMul`→DragonController) | `damageGain`, `boostGain` |
| **Chama** (Ardor) | tamanho/dano da chama e queimadura (`FlameSizeMul`→ataques `isFire`) | `flameGain` |
| **Agilidade** (Agility) | velocidade máx. (`SpeedMul`) · aceleração (`AccelMul`) · giro chão/voo (`TurnMul`) | `speedGain`, `accelAgilityGain`, `turnGain` |
| **Vigor** (Vigor) | vida (`MaxHealthMul`) · aguenta a fome (`HungerDecayMul`) · empurrão do corpo (`AccelMul`). O **IV de Vigor** também acelera o crescimento e alonga a vida (DragonGrowth) | `healthGain`, `hungerResistGain`, `accelVigorGain` |
| **Fôlego** (Wind) | energia máx. (`MaxEnergyMul`) · **eficiência** de energia — custo de tudo (`EnergyCostMul`) · subida de decolagem (`TakeoffClimbMul`) · **teto de voo** (`MaxAltitude`) | `energyGain`, `energyEfficiencyGain`, `takeoffClimbGain`, `ceilingBase/Gain/Cap` |
| **Instinto** (Instinct) | faro/dominância no minimapa (`DominanceRadius`) · **janelas de i-frame** dash/boost/esquiva (`IFrameMul`) · **leitura das correntes de ar** (`WindReadMul`) | `dominanceBase/Gain`, `iframeGain`, `windReadGain` |

Os **nomes** dos multiplicadores herdados (`SpeedMul`, `DamageMul`, etc.) foram
mantidos apontando para os novos atributos, então os consumidores não mudaram — só a
fonte por trás. Os multiplicadores **novos** do rework: `TurnMul`, `BoostMul`,
`EnergyCostMul`, `IFrameMul`, `WindReadMul`.

### Natureza (temperamento) — 15 no total

Cada natureza sobe um atributo e baixa outro (±`natureModifier`, default 10%). Três
são neutras. As 9 antigas mantiveram o índice de serialização (efeito remapeado); 6
novas cobrem Chama/Fôlego/Instinto. Tabela em `DragonNature.cs`
(`DragonNatureTable.Effect`): Fierce (+Força −Vigor) · Brutal (+Força −Agi) · Agile
(+Agi −Força) · Stealthy (+Agi −Vigor) · Sturdy (+Vigor −Agi) · Tenacious (+Vigor
−Força) · Fiery (+Chama −Fôlego) · Smoldering (+Chama −Vigor) · Tireless (+Fôlego
−Força) · Windborne (+Fôlego −Agi) · Cunning (+Instinto −Força) · Feral (+Instinto
−Fôlego). **[ABERTO]**: renomear para PT no futuro (identificadores ficam EN).

### IVs (talento por atributo) e a Maturidade → Degrau

- **6 IVs** (um por atributo), 0..`maxIV` (31), sorteados no nascimento. Migração:
  dragões salvos herdam Velocidade→Agilidade, Poder→Força, Resistência→Fôlego (via
  `FormerlySerializedAs`); Chama e Instinto nascem 0 em dragões antigos. O IV de Vigor
  manteve o nome (era o talento de vida longa, agora também é o atributo Vigor).
- **Degrau de maturidade** (`Tier`, 1..`maxTier`): fatia a maturidade em degraus. É o
  que `DragonAttackData.unlockLevel` compara para desbloquear ataques. O Tier é
  **monotônico** — envelhecer enfraquece o corpo, mas não se desaprende um golpe.
- **Serialização**: a ordem do enum `Attribute` (Agility, Might, Vigor, Ardor, Wind,
  Instinct) e do enum `DragonNature` são contrato — só **acrescente no fim**.

> ⚠️ **Nota de re-serialização do prefab**: o rework renomeou/adicionou campos em
> `DragonAttributes`. Ao abrir o Unity, os campos NOVOS assumem os defaults do código
> (curvas e ganhos acima); valores de curva tunados na refatoração anterior de 3
> atributos se perdem. **Reabra o Balanço do Dragão e retune as 6 curvas** — é rápido.

## Traços herdáveis **[IMPL]**

A camada qualitativa da linhagem (inspiração: passivas do Palworld). Cada dragão nasce
com **0..3 traços** sorteados (`DragonRecord.MaxTraitSlots`), herdados na reprodução
(`Breed`, ~55% por traço + chance de mutação). Todo traço tem efeito nomeado e um
trade-off. Dados em `DragonTrait.cs`; **números e fiação em `DragonTraits.cs`** (é lá
que se balanceia). Sem record, a lista fica vazia e tudo é neutro.

| Traço | Efeito (número) | Onde é aplicado |
|---|---|---|
| **Sangue Frio** | −30% custo de energia no frio (Tundra/Montanha), +30% no calor (Deserto) | `EnergyCostBiomeMul` → DragonController.CostMul |
| **Ossos Ocos** | −15% peso · plana melhor (−20% afundamento) · −15% vida · +25% dano de queda | DragonFlight.SinkMul, DragonVitals.MaxHealthEff, DragonController.ResolveFall |
| **Estômago de Ferro** | +25% de nutrição por refeição | DragonController (comer) |
| **Termonauta** | 2× aproveitamento das correntes de ar | DragonFlight.WindReadMul |
| **Insone** | +60% regen de energia parado (dispensa o R) · +20% fome | DragonVitals |
| **Fôlego de Forja** | sopro (isFire) sem cooldown · −30% vida | DragonAbilities.cooldown, DragonVitals |
| **Guelras Vestigiais** | +50% nado · respira submerso (nado sem custo) · −15% teto de voo | DragonController (nado), DragonFlight.Ceiling |
| **Couraça** | −20% dano recebido · −10% giro | DragonVitals.Damage, DragonController.TurnMul |

**[ABERTO]** — traços fáceis de acrescentar depois (só entrar no fim do enum
`DragonTrait` e dar um acessor em `DragonTraits`): visão noturna, olfato aguçado
(compõe com Instinto), pele ígnea (imune a fogo), etc.

## Mutação (o motor da linhagem) **[IMPL]** — revisado jul/2026

**UMA rolagem por ninhada**, não mais uma por IV. A versão original rolava
`mutationChance` (8%) **6 vezes independentes** (uma por atributo) — na prática quase
toda ninhada saía com "algum" outlier, virou ruído (número subindo sem graça) em vez de
prêmio raro. `DragonRecord.Breed` agora faz UM sorteio (`RollMutation`) com dois
resultados possíveis, e o resto da ninhada nasce sem mutação nenhuma:

| Resultado | Chance (knob) | Efeito |
|---|---|---|
| **Slot de ataque extra** *(preferido)* | `extraSlotMutationChance` (2%) | `record.bonusAttackSlots = 1` — um 5º slot de golpe (tecla 5), acima dos 4 padrão. Ver DragonAbilities abaixo |
| Outlier de stat | `statMutationChance` (5%) | UM atributo aleatório parte do melhor pai e **ultrapassa o teto** normal (até +`ivOutlierBonus`=4) |
| Nada | ~93% | ninhada normal — os 6 IVs só herdam (mid/melhor pai ± desvio), sem overshoot |

Por quê preferir o slot: um golpe a mais no loadout **muda o jogo** de um jeito que
mais uns pontos num atributo que já satura não muda. `record.mutations` (0 ou 1) e
`bonusAttackSlots` aparecem na ficha e no log de possessão.

**Knobs de balanceamento** (`[SerializeField]` em `DragonBase`, seção "Balanceamento da
MUTAÇÃO"): `traitMutationChance` (natureza/genes/traços, 8%), `statMutationChance` (5%),
`extraSlotMutationChance` (2%), `ivOutlierBonus` (4). `DragonBase.BreedNew(...)` é o
ponto de entrada pronto para quando a reprodução em jogo (ninho/ovo) existir — hoje
nada chama Breed() ainda (F0 placeholder), mas os knobs já estão no Inspector.

### Slot de ataque extra (DragonAbilities)

`MaxSlotCount` = 5 é o teto rígido (dimensiona os arrays); `baseSlotCount` = 4
(`[SerializeField]`, balanceável) é o padrão sem mutação. `ActiveSlotCount = base +
bonusAttackSlots`, sempre travado no teto. O 5º slot só aparece na HUD
(`DragonAttackHUD`) e só responde à tecla `5` (`DragonInput.AbilityDown`) para quem
nasceu com o bônus — a maioria dos dragões nunca o vê.

> Os **genes visuais** (espinhos, cor) NÃO recebem outlier por ora: empurrar uma blend
> shape além de 100 arrisca deformar o modelo, e isso precisa de validação visual no
> Unity. A mutação vive no domínio dos **stats/slots**. **[ABERTO]** estender a genes
> depois de testar o alcance seguro das morphs.

## Alimentação 2.0

Substitui o modelo v1 ("carne genérica enche a fome"):

- **Cada tipo de animal vira um tipo de alimento** ao morrer, e **cada alimento concede
  bônus de atributo** ao dragão (o componente "Nutrição" da fórmula acima). Exemplo de
  mapa inicial (tuning): lebre→Velocidade · javali/alce→Resistência · urso→Poder ·
  cervo→Vitalidade · lobo→Instinto. Com teto por atributo, para dieta dirigida sem grind
  infinito.
- **Fome cheia = não come.** Comer só é possível com espaço na barra; a barra cheia
  implica dragão mais pesado.
- **Peso/digestão serão rebalanceados** — o modelo atual de condição corporal
  (magro/saudável/gordo afetando corrida, subida, planeio, custo de energia **[IMPL]**)
  continua como fundação, mas os números e o acoplamento com a digestão mudam. **[ABERTO]**.
- Consequência de design: o FoodSpawner de carcaças grátis **[IMPL]** morre; toda comida
  vem de caça real, carcaças de predadores (roubáveis **[IMPL]**) e eventos.

## Combate, Skills e Pergaminhos

- **[IMPL]** 4 slots de habilidade (teclas 1–4; **5º slot raro** por mutação — ver
  "Mutação"), ataques como assets data-driven (projétil/área/melee, queimadura, área de
  fogo), desbloqueio por **degrau de maturidade** (não por pontos), dano escala com
  **Força** e o raio da chama com **Chama**, ambos × tamanho do corpo.
- **Pergaminhos** *(novo)*: bosses derrotados liberam pergaminhos que ensinam **novas
  skills** — e skills aprendidas **podem ser herdadas** pelos filhotes. É a ponte
  boss → genética: o desafio de hoje vira o enxoval da próxima geração.
  **[ABERTO]**: pergaminho ensina um indivíduo (e a herança propaga) ou desbloqueia
  para a linhagem toda.
- Golpes usáveis em voo têm flag própria **[IMPL]** — ver Controles.

## Conteúdo de Desafio (o "pra quê" do poder)

A genética e o auge colossal precisam de conteúdo à altura. Referência de motivação de
bosses: **Don't Starve** (cada boss guarda uma recompensa que destrava um sistema).

1. **Bosses únicos** — criaturas nomeadas em locais/eventos específicos; drop:
   pergaminhos de skill (herdáveis) + recursos raros. Conteúdo que só o Colossal resolve,
   ou que ele resolve muito mais fácil.
2. **Cavernas** — exploração vertical estilo Stardew: andares, recursos de construção
   raros, criaturas próprias, chefes de profundidade. **[ABERTO]** formato.
3. **Clãs de humanos hostis** — tentam matar o dragão; assentamentos/acampamentos como
   ameaça e fonte de saque. **[ABERTO]** — nota: reverte a decisão v1 de "mundo sem
   civilização"; exige assets/IA humanoides (custo alto) e definição de tom.
4. **Megafauna / presas lendárias** — versões raras e gigantes da fauna (o pipeline de
   variação de escala/vida já suporta **[IMPL]**).
5. **Defesa (futuro)** — eventos que ameaçam a base dão função defensiva às construções.
   **[ABERTO]**.

## Fauna (Ecossistema Vivo)

**[IMPL]** — IA universal parametrizada (asset novo = espécie nova): lebre, raposa, cervo
(rebanho/stag/real), alce (+com cria), javali, urso (+ursa com filhotes), lobo (alcateia
que caça de verdade), populações próprias do deserto concentradas nos oásis. Organização
social coesa (líder, filhotes com mãe, migrações, fuga coletiva), períodos de atividade
reais (dia/noite), reação ao tamanho do dragão, tocaia, uivos em coro, carcaças reais.

A cadeia alimentar v1 (bisões→mamutes) foi substituída pela fauna real; mamute/bisão
podem voltar como megafauna de evento. **[ABERTO]**.

## Voo

**[IMPL]** — voo como habilidade: ciclo de batidas com bônus de timing, planeio sustentado
por velocidade, mergulho, estol por exaustão, colisão física sem dano (o chão machuca,
via queda), pouso automático rente ao chão, updrafts nas montanhas, **teto de voo por
Fôlego** (ar rarefeito, sem parede invisível), natação e voo rasante com spray.
Números centralizados em FlightProfile (asset) — pronto para variações por idade/fase.

> **NERF jul/2026**: a calibração anterior do impulso de voo (`flapLift` 17,
> `flapBonusLift` 13, `maxRiseSpeed` 21, `takeoffClimbRate` 12) ficou apelona — quase
> um flap já quase saturava o teto de subida. `flapLift`/`flapBonusLift` caíram para
> **1/4** (4.25 / 3.25); `maxRiseSpeed` e `takeoffClimbRate` também baixaram (10 / 6).
> Junto, a variação de sustentação por PESO (`DragonGrowth.liftWhenLean/Fat`) foi
> estreitada (era 1.3↔0.5 = ~2.6× de diferença entre magro e gordo; agora 1.1↔0.85,
> ~1.3×) — o impulso não deve variar muito de dragão pra dragão. Voar de verdade agora
> pede RITMO (batidas + planeio), não um único flap quase-perfeito.

**Rework hack and slash (jul/2026) [IMPL]** — o voo virou o brinquedo do predador:

- **Mergulho com overspeed**: Ctrl segurado passa da velocidade máxima (`diveOverspeed`);
- **Swoop**: sair do mergulho converte a queda em velocidade à frente
  (`swoopConversion`) com recuperação dobrada — o loop de energia mergulho→rasante;
- **Wing Boost**: Shift toque = batida forte com aceleração instantânea, i-frames curtos
  e custo de energia;
- **Duas esquivas aéreas, em camadas**: `Shift + A/D` = esquiva simples (desvia o rumo
  ~18°, i-frames, sem animação dedicada); `A/D + Shift + Space` = **esquiva completa**,
  que toca o clipe exato `Fly Dodge L/R` (por CrossFade, não por trigger — era o
  trigger disputando o blend de voo que saía impreciso) e **vira o voo inteiro** ~90°
  para o novo rumo, em vez de empurrar de lado mantendo a direção. O combo absorve a
  esquiva simples que o próprio Shift dispara antes, então cobra energia uma vez só;
- **Tombo na colisão**: bater FORTE em algo voando derruba o dragão com o clipe
  `UPFly Fall Death` (o estol por exaustão segue no `Stall Fall` — queda controlada,
  não tombo);
- **Curvas por velocidade**: fechadas devagar, amplas em alta (fator 1.5→0.85);
- Velocidade **engata rápido e sangra devagar** (accel assimétrica) — conservar
  velocidade é o prazer central do voo;
- Câmera antecipa: lead na curva, pitch/FOV extras no mergulho.

## Eventos Dinâmicos

Eventos aleatórios spawnam no mundo usando **prefabs pré-prontos** + os sistemas
existentes (IA, clima, fogo, território). São **efêmeros por design — nunca salvos**.

Lista base (v1 + novos): migração de herbívoros · outro dragão caçando · **parceiro em
cio (evento de acasalamento)** · disputa territorial · tesouro escondido · incêndio ·
tempestade · criatura albina · caverna desconhecida · **boss único** · ninho/ovo
abandonado · grande predador · presa lendária · **acampamento de clã humano [ABERTO]**.

**[ABERTO]** — a lista final de eventos-prefab e o "diretor" de spawn (frequência, anel
ao redor do jogador, pressão por seca de eventos — o padrão já existe no spawner de
fauna **[IMPL]**).

## Controles & UI

- **Interação por mouse** *(novo)*: a maioria das interações é clicar — o cursor em hover
  vira um **sprite da ação** (comer, falar, interagir). Substitui o modelo "olhar +
  tecla" (G de comer sai).
- **Mira em voo** *(novo)*: voando, uma mira/retículo aparece para disparar os golpes
  usáveis no ar; slots de golpes não-usáveis em voo ficam `interactable = false` no HUD.
- **Movimento & game feel (rework hack and slash, jul/2026) [IMPL]** — prioridade
  absoluta: responsividade (referências: DMC, Bayonetta, Dragon's Dogma, Spyro).
  O peso do dragão vira derrapada/câmera/VFX — nunca atraso de input.
  - **Sempre corre**; aceleração quase instantânea (~0,35 s até 90% da corrida);
  - **Chão relativo à câmera**: WASD move na direção da tela, o corpo gira sozinho
    (fechado devagar, arco em corrida). No voo A/D seguem girando;
  - **Ctrl segurado = Stealth**: passo de caçada (velocidade do antigo "andar"),
    fauna percebe a ~1/3 do normal (percepção 0.35→0.12) — tocaia de verdade;
  - **Shift = explosão**: no CHÃO é um dash **lateral puro** — Shift+A ou Shift+D,
    as duas teclas precisam ser um toque FRESCO e próximo uma da outra (segurar a
    direcional de antes de apertar Shift não dispara: solte e aperte as duas juntas
    ou quase); no AR, Shift toque = Wing Boost/esquiva, segurar = mergulho.
    i-frames curtos, custo de energia, **cancela golpes após ~40% do swing**;
    reaproveita a mesma animação Dodge L/R do dash aéreo;
  - **Buffer de comandos** (~0,2 s): apertar antes da hora enfileira e executa no
    primeiro frame possível; coyote de voo (Space no ar sempre abre as asas);
  - **Decolagem em corrida** pula a animação de salto (Locomotion→Fly direto);
    parada, o salto é uma animação **fechada**: trava rotação e ações pela duração
    REAL do clipe (lida do Animator, não um tempo fixo) — não gira nem é
    interrompida por outro comando, corrige o dragão torto de vez;
  - **Space no chão sempre decola**, parado inclusive — o salto `UJump Up` toca
    inteiro e as asas dão o **bote** no fim do clipe (impulso de subida + empurrão
    à frente, shake e VFX). Antes o impulso vinha todo no 1º frame e chegava gasto
    na virada para o voo — a decolagem *perdia* força no clímax. O antigo "bate
    asas parado" (`UPFly Stand`) saiu: parado nunca se via o salto bom;
  - **Pouso vira corrida** se houver velocidade (sem parada brusca);
  - Feedback: hit stop nos golpes que conectam, micro-shake em dash/decolagem/boost,
    VFX de poeira/sopro (DashBurst/WingBoost). **Squash & stretch: NÃO** — rig
    realista deformaria mal; o equivalente é shake + VFX + trancos de sustentação;
  - **Filhote vs colossal**: filhote gira/acelera melhor, dash renova mais rápido,
    boost fraco e é mais furtivo (×0.85 na percepção); colossal é um aríete que
    conserva velocidade (boost ×1.15, giro ×0.85).
- Mapa de teclas atual: `WASD` mover · `Ctrl` stealth · `Space` asa/decolar ·
  `Shift+A`/`Shift+D` dash lateral (chão) · `Shift` boost/mergulho (ar) ·
  `Shift+A/D` esquiva simples (ar) · `A/D + Shift + Space` esquiva completa (ar) ·
  `LMB` combo · `RMB`/`F` fogo · `Q`/`E` cauda/asas · `1–4` skills · `T` rugir ·
  `R` descansar · `G` comer · `Tab` ficha. Bindings centralizados em
  `DragonInput` (preparo p/ gamepad).
- Esquema final ainda converge para mouse-driven. **[ABERTO]** gamepad.
- UI: barras de vida/energia/fome/crescimento, pips de batida de asa, **pips de degrau de
  maturidade** (ao lado de Crescimento — dourado = conquistado, o atual pisca com o
  progresso) **[IMPL]**, minimapa com faro + relógio **[IMPL]** · **ficha do dragão**
  (Tab, `DragonStatsMenu`) com fase de vida, idade, expectativa, os **6 atributos** em
  barras, natureza, **6 IVs**, **traços**, **mutações**, degrau + próximo golpe, stats
  derivados e projeção por fase **[IMPL]** · aviso de **ar rarefeito** perto do teto de
  voo **[IMPL]** · **UI de linhagem/base** *(novo)*: lista de dragões vivos, ovos
  incubando, troca de dragão ativo.

## Save System

Princípio: **modular e explícito**. Cada sistema declara o que salva (módulo versionado
com chave própria); o GDD mantém a tabela do contrato:

| Estado | Salvo? | Notas |
|---|---|---|
| Seed + identidade do mundo (lago inicial...) | ✅ | **[IMPL]** WorldGenState |
| Claims de base (posição, raio, tier) | ✅ | novo — extensão do WorldGenState |
| Construções da base (tipo, posição, completa/incompleta, itens entregues) | ✅ | novo |
| Recursos armazenados na base | ✅ | novo |
| DragonRecords (genética, natureza, 6 IVs, traços, mutações, idade, fome, vida, golpes, base) | ✅ | novo — o coração do save |
| Ovos (pais, genética rolada, progresso de incubação) | ✅ | novo |
| Relógio do mundo (hora, dia) | ✅ | DayNightCycle já expõe SetTime **[IMPL]** |
| Pergaminhos/skills desbloqueadas | ✅ | novo |
| Outras alterações permanentes com posição no mapa | ✅ | conforme surgirem |
| Fauna (posições, grupos, carcaças) | ❌ | efêmera por design **[IMPL]** |
| Eventos ativos no mapa | ❌ | efêmeros — respawnam pelo diretor |
| VFX, clima do momento, projéteis | ❌ | efêmeros |

- **Múltiplos saves + retorno a ponto salvo**: o jogador pode manter vários saves e
  voltar a um checkpoint (é também a resposta atual para fim de linhagem).
  **[ABERTO]** granularidade (slots manuais? autosave por dia de jogo?).
- Objetivo: voltar dias depois e encontrar a linhagem exatamente onde parou — o mundo
  *parece* persistente, mas só a camada permanente é serializada (save pequeno).

## Multiplayer (fase futura — essencial, não-MVP)

Jogadores compartilham o **mesmo save de base/linhagem**; ao entrar, cada um escolhe um
dos dragões vivos do clã. Administração de uma família de dragões em co-op.

- **Não sai no MVP**, mas é feature essencial: **toda a arquitetura nova nasce
  multiplayer-ready** — DragonRecord/save como dados autoritativos, mundo determinístico
  por seed (já é a filosofia do WorldGenState **[IMPL]**), mutações de estado como
  comandos sobre registros.
- **[ABERTO]** tecnologia (Photon vs. cliente-servidor próprio).

## Escopo & Roadmap

Corte pragmático por fases (cada fase é jogável):

- **F0 — Fundação da linhagem**: separar DragonRecord (dados) do avatar (possession,
  padrão já usado pela fauna); save core modular; relógio de idade; morte→troca via UI.
- **F1 — Base**: claim com aplainamento/limpeza de vegetação; ninho revogado + entrega de
  galhos; incubação de ovo; remoção do FoodSpawner (alimentação 2.0 mínima).
- **F2 — Gerações**: cio, acasalamento (evento + interno), herança (natureza/IVs/golpes),
  velhice + morte natural, 5 atributos, nutrição por tipo de alimento.
- **F3 — Conteúdo de desafio**: diretor de eventos, primeiro boss + pergaminho, caverna
  piloto, megafauna. Filhote com mesh própria (Little Dragons).
- **F4 — Multiplayer**: netcode sobre a arquitetura de registros.
- Áudio segue política atual: ganchos prontos **[IMPL]**, clips por último.

Fora do escopo (sem mudança de decisão): árvore de habilidades complexa, economia/NPCs
comerciais, construção livre estilo Valheim.

## Decisões-padrão (valem até doer no playtest)

Defaults cravados pelo contrato de escopo — nenhuma pergunta fica "aberta" se não
bloqueia a espinha:

| Tema | Default |
|---|---|
| Cap de dragões vivos | 6 por linhagem (UI e performance confortáveis) |
| Tempo de incubação | ~1 dia de jogo (30 min reais); ajustável |
| Mutação genética | Sim, rara (~5% por IV fora da faixa dos pais); golpes mutantes só via Geladeira |
| Naturezas | As 8 propostas na seção Atributos; só stats no MVP (comportamento é Geladeira) |
| Pergaminho | Por indivíduo, herdável (tesouro genético) — quando bosses entrarem |
| Cavernas | Instanciadas (cena separada com andares) — quando entrarem |
| Rollback/saves | Slots manuais + autosave por dia de jogo |
| Pântano/Vulcões | Landmarks/arenas de evento, não biomas |
| Vida do dragão | 30 h de relógio com jogo aberto (slider de tuning) |
| Evento de parceiro | Quase-cutscene (sem rival/combate) |
| Multiplayer (tech) | Decidir só na F4; até lá, arquitetura de registros/comandos |
| Controles | Esquema atual no MVP; mouse-driven + mira em voo entram como camada de UI |

## Geladeira

Ideias aprovadas em espírito, fora do escopo até a espinha ser jogável:

- Multiplayer (save de clã compartilhado) — essencial, fase F4.
- Clãs de humanos hostis (decidir lore/civilização quando promover).
- Cavernas estilo Stardew; bosses únicos + pergaminhos; megafauna/presas lendárias.
- Bases 2–4, tamanhos de base (P/M/G), construção modular, recursos além do galho.
- Alimentação 2.0 (bônus de atributo por tipo de alimento) e rebalance peso/digestão.
- Atributos Vitalidade e Instinto (5-atributos).
- Controles mouse-driven completos + mira em voo; gamepad.
- Meshes por idade (Little Dragons para o filhote — comprar só quando promover).
- Rede de segurança de linhagem (ovo selvagem garantido).
- Eventos dramáticos de acasalamento (rival, disputa).
- Defesa de base (eventos que ameaçam construções).
- Disputas PvP (1×1) com seu dragão — duelo entre dragões de jogadores; depende do multiplayer (F4).
- Mapa de tamanho fixo (configurável na criação do save), ainda procedural — permite garantir
  biomas em posições coerentes (gelo e deserto em extremos opostos, sem transições
  "impossíveis") e simplifica muita coisa. Mais coerente que o modelo Minecraft (sandbox
  focado em construção) para a proposta do jogo. **Candidata forte a promoção em breve.**
