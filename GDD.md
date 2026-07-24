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

## Atributos (proposta v2)

Valor final de cada atributo:

```
Atributo = Base da fase de vida
         × CurvaDeIdade (sobe até o Colossal, declina na velhice)
         × Natureza (±10% em 2 atributos)
         × (1 + IV)                  ← genético, oculto
         + Treino (pontos por nível) ← escolha do jogador [IMPL]
         + Nutrição (bônus por alimentação, com teto)
```

Cinco atributos (era 3 — Dominância sai de Poder e vira Instinto; Vitalidade é nova):

| Atributo | Governa |
|---|---|
| **Velocidade** | Corrida, aceleração, voo, mergulho, nado |
| **Poder** | Dano físico, dano/tamanho/alcance da chama |
| **Resistência** | Vida, energia, teto de voo, resistência à fome, decolagem |
| **Vitalidade** *(novo)* | **Tempo de vida** (dragões com Vitalidade alta vivem mais dias), velocidade de crescimento, regeneração, início mais tardio da velhice |
| **Instinto** *(novo)* | Faro/Dominância Territorial (detecção de comida, fauna, eventos, tesouros no minimapa) **[IMPL como Poder]**, eficiência de tocaia/caça |

Naturezas propostas (**[ABERTO]** aprovar/expandir): Dócil (+Vit −Pod) · Agressivo
(+Pod −Inst) · Arisco (+Vel −Vit) · Estoico (+Res −Vel) · Astuto (+Inst −Res) ·
Voraz (+Res −Inst) · Imponente (+Pod −Vel) · Sereno (+Vit −Pod). Neutras possíveis.

Treino: 1 ponto por nível, nível deriva do crescimento (sem XP tradicional) **[IMPL]**.
Velhice reduz **todos** os atributos via CurvaDeIdade.

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

- **[IMPL]** 4 slots de habilidade (teclas 1–4), ataques como assets data-driven
  (projétil/área/melee, queimadura, área de fogo), desbloqueio por nível, dano/chama
  escalam com Poder e tamanho do corpo.
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
Resistência** (ar rarefeito, sem parede invisível), natação e voo rasante com spray.
Números centralizados em FlightProfile (asset) — pronto para variações por idade/fase.

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
- UI: barras de vida/energia/fome/crescimento, pips de batida de asa, minimapa com faro +
  relógio **[IMPL]** · **ficha do dragão** com fase de vida, idade, expectativa,
  atributos (IVs ocultos — mas dá pra "sentir" que estão ruins) · **UI de linhagem/base**
  *(novo)*: lista de dragões vivos, ovos incubando, troca de dragão ativo.

## Save System

Princípio: **modular e explícito**. Cada sistema declara o que salva (módulo versionado
com chave própria); o GDD mantém a tabela do contrato:

| Estado | Salvo? | Notas |
|---|---|---|
| Seed + identidade do mundo (lago inicial...) | ✅ | **[IMPL]** WorldGenState |
| Claims de base (posição, raio, tier) | ✅ | novo — extensão do WorldGenState |
| Construções da base (tipo, posição, completa/incompleta, itens entregues) | ✅ | novo |
| Recursos armazenados na base | ✅ | novo |
| DragonRecords (genética, natureza, IVs, idade, fome, vida, golpes, base) | ✅ | novo — o coração do save |
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
