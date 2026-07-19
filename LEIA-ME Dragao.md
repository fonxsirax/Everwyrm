# Everwyrm — Protótipo do Dragão

## Como ativar (1 clique)

1. Abra o projeto no Unity (cena `Assets/Scenes/Main.unity`).
2. Menu **Tools > Dragão > Setup Completo**.
3. Play.

O setup gera `Assets/Dragao/Dragon Player.controller` (~50 clips dos FBX do Unka), configura o prefab **Unka Realistic** (CharacterController + DragonController + DragonVitals + DragonGrowth, root motion off), liga a **DragonCamera** na Main Camera e cria o **Food Spawner** na cena. O HUD é criado sozinho em runtime.

## Controles

### Chão

| Ação | Tecla |
|---|---|
| Andar / ré | W / S |
| **Correr** | Automático: siga andando pra frente e o dragão acelera até correr. Peso importa: gordo/grande demora mais para engatar |
| Virar (com turn-in-place) | A / D |
| Bater as asas (pulinho) | Space parado |
| **Decolar** | Space andando pra frente |
| **Comer carcaça** | G perto da comida (aparece a dica no HUD) |
| Combo mordida/garras | Botão esq. do mouse |
| Golpe de cauda / Rajada de asas | Q / E |
| Jato de fogo | F (15 energia) |
| Rugido | T (automático ao subir de fase!) |
| Esquiva | Alt (+ A/D para o lado) |
| Dormir / acordar | R |
| Gastar ponto de atributo | 1 Velocidade · 2 Poder · 3 Resistência |
| Ficha do dragão (pausa) | Tab — abre sozinha ao subir de nível no chão |

### Voo (skill-based!)

| Ação | Tecla |
|---|---|
| **Decolar + ganhar altura** | SEGURAR Space nos primeiros ~3s de voo = subida contínua |
| **Depois: bater asa (1 toque = 1 batida, 2 por ciclo)** | Space — esgotou? Solte e espere ~0.5s pra renovar (pips ao lado da barra de Energia) |
| Acelerar / frear | W / S |
| Virar (com inclinação) | A / D |
| Mergulhar (troca altitude por velocidade) | Ctrl ou C |
| Esquiva aérea | Alt |
| Planar | Automático entre batidas — voar RÁPIDO conserva altitude, lento afunda |
| Recuperar altitude de graça | Planar sobre encostas de Montanha (updrafts — o HUD avisa) |
| Pousar | Descer devagar perto do chão |

O ciclo é: **bater → subir → planar → escolher a hora da próxima batida**. Ritmo certo = voa longe gastando pouco; spam de Space = sobe pouco e cansa. Todos os números em `Assets/Scriptables/Resources/FlightProfile.asset` (por espécie/upgrade de asas no futuro).

Mouse orbita, scroll dá zoom, Esc solta o cursor. Morreu? Enter renasce.

## Crescimento e Peso (GDD: Crescimento Visível)

- **Tamanho**: o dragão cresce com o tempo — muito mais rápido bem alimentado. Filhote (0.45x) → Adulto → Colossal (1.7x). A câmera, as velocidades e o alcance acompanham. Ao mudar de fase, ele ruge sozinho.
- **Condição corporal**: muda DEVAGAR (minutos), seguindo a média de alimentação — uma refeição não engorda. Usa as blend shapes do modelo: "Belly Fat" incha a barriga; as shapes "Thin" definham barriga, peito, pescoço, pernas, cauda e asas quando passa fome.
- **Peso** (derivado de tamanho³ × condição) afeta: tempo para engatar a corrida, velocidade máxima, subida no voo (gordo sobe mal), planeio (gordo afunda; grande plana melhor — GDD) e custo de energia.
- Corpo saudável (nem magro, nem gordo) cresce mais rápido.

## Atributos Evolutivos (GDD)

Sem XP: o **nível vem do crescimento** (Nv 1–10). Cada nível dá 1 ponto, gasto com 1/2/3:

- **Velocidade** — velocidade máxima (chão/voo/mergulho) e aceleração.
- **Poder** — dano e chama (para o futuro combate/fogo) e **Dominância Territorial**: o raio do "faro" que detecta comida ao longe (dica "Faro: comida a Xm" no HUD; depois detectará eventos, perigos e dragões no minimapa).
- **Resistência** — vida, energia total (duração do voo), aceleração e resistência à fome.

## Sobrevivência

- **Fome** cai constantemente; crítica reduz teto de energia; zerada mata aos poucos.
- **Energia**: voar/correr/atacar gastam (× peso); planar quase nada; dormir (R) recupera rápido.
- **Estol**: energia zerada em voo = queda (UPFall High) + dano na aterrissagem.
- **Comida**: carcaças aparecem pelo mapa (placeholder até o sistema de caça). G para comer em mordidas.

## Mundo Infinito (procedural)

Menu **Tools > Dragão > 3 - Mundo Infinito na Cena**: desativa o terrain fixo e cria o `World (Infinite Terrain)`.

- Tiles de Terrain (250 m) gerados ao redor do dragão e descartados ao longe — mapa praticamente infinito, determinístico pela `Seed`.
- **Biomas** por mapas contínuos de temperatura/umidade/montanha: Campos, Floresta Antiga (árvores do pacote ALP), Montanhas Rochosas (picos ~110 m) e Tundra (neve). Sem costuras entre tiles.
- FoodSpawner, faro e pouso funcionam por raycast — nada muda. O dragão nasce sobre o terreno gerado.
- Ajustes no Inspector: `Load Radius` (raio de tiles, padrão 2 = ~625 m de vista), `Tile Size`, `Max Height`, cores dos biomas, densidade de árvores.
- Fase 2 (GDD): substituir o noise por blocos de bioma construídos à mão, região por região; Pântano e Vulcões.

## Arquitetura (Observer)

O HUD não faz polling: assina eventos —
`DragonVitals.OnStatsChanged/OnDeath`, `DragonGrowth.OnGrowthChanged/OnStageChanged`,
`DragonController.OnHintChanged`. Qualquer sistema novo (sons, IA, achievements)
pode assinar os mesmos eventos sem tocar no HUD.

## Arquivos

- `Assets/Scripts/Dragon/DragonController.cs` — movimento, voo, ações, momentum
- `Assets/Scripts/Dragon/DragonVitals.cs` — Fome/Energia/Vida (eventos)
- `Assets/Scripts/Dragon/DragonGrowth.cs` — escala, blend shapes, peso (eventos)
- `Assets/Scripts/Dragon/DragonHUD.cs` — HUD observer
- `Assets/Scripts/Dragon/DragonCamera.cs` — câmera (escala com o dragão)
- `Assets/Scripts/World/Carcass.cs` + `FoodSpawner.cs` — comida
- `Assets/Scripts/Dragon/Editor/DragonSetup.cs` — setup automático
- `GDD.md` — Game Design Document
