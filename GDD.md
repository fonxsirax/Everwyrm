# EVERWYRM — Game Design Document (GDD v1.0)

## High Concept

**Viva toda a vida de um dragão.**

Everwyrm é um Sandbox Survival single-player onde o jogador nasce como um pequeno dragão e evolui até se tornar uma criatura colossal capaz de dominar ecossistemas inteiros.

O jogo não busca quantidade de conteúdo, mas profundidade. Cada sistema influencia vários outros, criando histórias únicas sem depender de roteiros.

A experiência é inspirada em The Isle, Path of Titans, Rain World e na exploração infinita de Minecraft, porém com foco total na fantasia de ser um dragão.

## Pilares do Projeto

### 1. Crescimento Visível

Tudo gira em torno da sensação de evolução. O jogador deve olhar para seu dragão após algumas horas e perceber imediatamente:

- ficou maior
- voa melhor
- caça criaturas maiores
- seu fogo é mais poderoso
- outras criaturas reagem de maneira diferente

### 2. Gameplay Emergente

Não existem missões tradicionais. As histórias surgem naturalmente da interação entre: fome, território, clima, IA, eventos e exploração.

Exemplo: Você pousa para caçar um cervo. Um dragão adulto aparece tentando roubá-lo. Durante a luta começa uma tempestade. O cervo foge. Ambos ficam sem alimento. Dias depois vocês se reencontram perto de um lago. Nenhuma dessas situações foi roteirizada.

### 3. Mundo que Recompensa Exploração

O horizonte sempre deve parecer interessante. Mesmo após dezenas de horas o jogador deve pensar: "Só vou ver o que existe depois daquela montanha."

### 4. Simplicidade Elegante

Todo sistema deve ser reutilizado. Poucos sistemas. Muitas interações.

## Gameplay Loop

Sessão média: 20 minutos até 1 hora

```
Explorar → Sentir fome → Encontrar alimento → Decidir se vale a pena caçar
→ Caçar → Comer → Crescer → Encontrar evento → Sobreviver
→ Explorar região mais difícil → Encontrar presas maiores → Repetir
```

## Estrutura Geral

### Estados principais do jogador

- Explorando
- Voando
- Caçando
- Descansando
- Alimentando-se
- Defendendo território
- Reproduzindo

Esses estados são suficientes para praticamente todo o jogo.

## Mundo

### Geração Procedural

O mapa é praticamente infinito. Ao invés de geração procedural completa, o jogo utiliza blocos de biomas cuidadosamente construídos, conectados proceduralmente.

Cada região possui: relevo, vegetação, fauna, clima, sons, eventos próprios e dragões predominantes.

Isso reduz drasticamente o custo de produção e garante qualidade visual.

### Biomas

| Bioma | Características |
|---|---|
| Floresta Antiga | alimento abundante, pequenos mamíferos, árvores gigantes, excelente para filhotes |
| Montanhas Rochosas | poucas presas, correntes de vento fortes, cavernas, dragões territoriais |
| Pântano | pouca visibilidade, criaturas agressivas, muita água, eventos raros |
| Campos | grandes herbívoros, fácil localizar alimento, poucos esconderijos |
| Vulcões | temperatura extrema, alimento escasso, grandes predadores, excelente para adultos |
| Tundra | pouca comida, neve, resistência importante, migrações frequentes |

## Ciclo de Vida

### Ovo
Tutorial. O jogador apenas observa sons externos. Pode escolher quando nascer.

### Filhote
Fase mais difícil. Pouca força, pouco fogo, voo limitado. Grande parte das criaturas representa ameaça. Objetivo: sobreviver.

### Adulto
A fantasia principal do jogo. O jogador já domina voo, caça grandes animais, disputa território, pode reproduzir.

### Colossal
Pouquíssimos inimigos naturais. A maior ameaça passa a ser manter um território enorme e sustentar seu peso.

## Peso

O peso cresce principalmente através da alimentação. Afeta: força, empurrões, dano por impacto, intimidação, consumo de alimento e velocidade de crescimento.

Dragões extremamente pesados precisam comer muito mais.

## Atributos Evolutivos

Cada nível concede um ponto. Apenas três atributos.

### Velocidade
Melhora: corrida, aceleração, mergulho, voo, perseguição. Ideal para caçadores.

### Poder
Melhora: dano físico, fogo, alcance do fogo, tamanho das chamas.

Também aumenta a **Dominância Territorial** — a presença do dragão no mundo. Quanto maior, mais distante ele percebe: alimento, eventos, outros dragões, grandes presas, tesouros. Não é visão mágica, é um instinto apurado. Funciona como um "radar natural".

### Resistência
Melhora: HP, energia, voo contínuo, recuperação, resistência à fome. Ideal para longas explorações.

## Sistema de Sobrevivência

Existem apenas três necessidades.

### Fome
Diminui constantemente. Em níveis críticos: menos stamina, recuperação lenta, menor crescimento. Sem comida: morre.

### Energia
Consumida por: voo, corrida, combate, fogo. Recuperada descansando.

### Vida
Recuperada lentamente, ou rapidamente após boa alimentação e descanso.

## Sistema de Alimentação

Cada presa possui: quantidade de carne, dificuldade e risco.

O jogador decide constantemente: vale a pena gastar energia para caçar? Ou procurar outra presa?

### Cadeia Alimentar

Filhote → coelhos → raposas → javalis → cervos → bisões → mamutes → dragões

## Sistema de Caça

A caça possui três etapas:

1. **Localizar** — encontrar rastros ou observar movimentação.
2. **Aproximar** — utilizar altura, vegetação, rochas, vento.
3. **Executar** — cada presa reage de forma diferente: algumas fogem, outras lutam, outras protegem o grupo.

Como o sistema é único, qualquer criatura nova automaticamente cria novas experiências.

## Fauna (Ecossistema Vivo)

A fauna existe para o mundo, não para o jogador. Rebanhos pastam, migram, caçam e descansam mesmo quando ninguém está olhando — o jogador apenas testemunha.

Um único sistema de IA parametrizável controla todas as espécies (mesma filosofia da IA dos dragões). Cada espécie é apenas um conjunto de dados: biomas, raridade, organização social, personalidade, migração e cadeia alimentar. Espécie nova = novo asset de dados, zero código.

### Organização Social

- **Solitário** — quase sempre sozinho (urso, alce).
- **Rebanho** — grupos grandes e frouxos (cervos, lebres).
- **Alcateia** — grupos organizados com líder (lobos).
- **Híbrido** — sozinho ou em pequenos grupos, decidido proceduralmente (javali, raposa).

Grupos são coesos de verdade: âncora compartilhada, fuga coletiva, migrações espontâneas cruzando o mundo. Filhotes nunca existem sem os adultos e ficam colados na mãe.

### Espécies

| Espécie | Social | Personalidade |
|---|---|---|
| Lebre | Rebanho frouxo, muito comum | Pânico fácil; dispara e se esconde no mato; filhotes junto dos adultos |
| Raposa | Híbrido (maioria solitária) | Curiosa — chega perto para observar antes de fugir; caça lebres em tocaia |
| Cervo | Rebanho 5–9 | Extremamente tímido, vigília constante, pasta andando; Stag ocasional liderando |
| Cervo solitário | Solitário raro | Stag territorial; pode investir contra dragões jovens |
| Cervo-Real | Rebanho 6–12 nos Campos | Migra longe com frequência — as grandes travessias de vale |
| Alce | Solitário | Corajoso e imponente; ignora dragões pequenos; vive na beira dos lagos; perigoso se provocado |
| Alce com cria | Par raro | A mãe é mais agressiva que o macho |
| Javali | Híbrido 2–6 | Fuça o chão em bando; imprevisível — ora foge, ora ataca |
| Urso | Solitário territorial | Investiga barulhos, levanta em duas patas para intimidar, decide entre ignorar e atacar |
| Ursa com filhotes | Raríssima | Protege as crias com agressividade máxima |
| Lobo | Alcateia 3–6 | Patrulha territórios enormes, uiva em coro, caça cervos e lebres de verdade |

### Interações Emergentes

- Lobos abatem presas que viram carcaças reais — o jogador pode assustá-los e roubar a caça.
- O tamanho do dragão muda tudo: filhotes são ignorados (ou caçados); um Colossal causa pânico geral.
- Pousar perto faz barulho: uns fogem, curiosos investigam.
- A Dominância Territorial (faro) revela a fauna próxima — presas e predadores.

## Sistema de Crescimento

O crescimento depende de: tempo vivido e alimentação. Não existe XP tradicional. Comer bem acelera o crescimento; passar fome desacelera. Isso conecta sobrevivência diretamente à progressão.

## Sistema de Voo

O voo é baseado em energia. O jogador pode: planar, bater asas, mergulhar e utilizar correntes de vento.

Dragões maiores gastam mais energia, mas planam melhor.

## Sistema de Fogo

O fogo evolui naturalmente:

1. **Faíscas** — quase inúteis; assustam pequenas criaturas.
2. **Pequenas Chamas** — permitem caçar animais pequenos.
3. **Jato de Fogo** — principal arma.
4. **Explosão** — grande dano em área; consome muita energia.
5. **Tempestade de Fogo** — apenas Colossais; incendeia grandes áreas. Mais útil para controle territorial do que dano direto.

O fogo também influencia: incêndios, fuga das presas, comportamento da fauna e eventos.

## Sistema Territorial

Todo dragão possui um território. Quanto maior seu Poder, maior sua área de influência. Dentro dela: detecta eventos, percebe invasores, encontra alimento mais facilmente.

Outros dragões podem desafiar sua dominância. Isso gera encontros naturalmente, sem precisar de missões.

## IA dos Dragões

Cada dragão recebe parâmetros aleatórios: peso, idade, fome, coragem, agressividade, território, interesse em reprodução.

Esses poucos parâmetros geram diversos comportamentos:

- Filhote faminto → arrisca lutas.
- Adulto alimentado → ignora o jogador.
- Dragão territorial → ataca imediatamente.
- Fêmea no cio → procura parceiro.

## Reprodução

A reprodução é um marco da vida do dragão, oferecendo um objetivo de longo prazo e um legado, sem transformar o jogo em um simulador de criação de filhotes.

1. **Encontrar Parceiro** — durante períodos específicos, dragões adultos entram no cio. O jogador percebe por comportamentos e vocalizações da IA. Nem todo encontro resulta em acasalamento: agressividade, território e fome influenciam.
2. **Construção do Ninho** — o jogador apenas seleciona uma área adequada (caverna, penhasco ou clareira protegida); o ninho é criado automaticamente.
3. **Postura dos Ovos** — 1 a 3 ovos, reforçando seu valor.
4. **Proteção** — eventos podem ameaçar o ninho: predadores oportunistas, outros dragões, incêndios, tempestades. O jogador pode proteger ou continuar explorando, assumindo o risco.
5. **Nascimento** — filhotes permanecem próximos ao ninho, alimentam-se sozinhos e fogem de perigos. Se sobreviverem, integram a população dinâmica do mundo como dragões de IA.

Cria sensação de legado, aumenta a população de dragões e gera histórias emergentes ("encontrei um dragão enorme que era descendente de um antigo ninho meu").

## Eventos Dinâmicos

Eventos aparecem conforme o jogador explora. Todos utilizam os mesmos sistemas do jogo: migração de herbívoros, outro dragão caçando, disputa territorial, tesouro escondido, incêndio, tempestade, criatura albina, caverna desconhecida, dragão colossal dormindo, ninho abandonado, fêmea no cio, grande predador, presa lendária.

Como compartilham IA, clima, território e fauna, têm baixo custo de desenvolvimento e podem se combinar, produzindo situações imprevisíveis.

## Progressão do Jogador

A progressão não é medida apenas por números, mas pela transformação da experiência:

- **Filhote**: esconder-se, fugir e sobreviver.
- **Adulto**: dominar a caça, explorar e disputar território.
- **Colossal**: moldar o ecossistema, intimidando criaturas e alterando a dinâmica das regiões.

O objetivo final não é "zerar" o jogo, mas completar o ciclo de vida de um dragão e construir sua própria história.

## Escopo Indie

- Apenas 3 atributos evolutivos.
- Apenas 4 fases de vida.
- Um único sistema de IA baseado em parâmetros reutilizáveis.
- Conjunto limitado de biomas modulares reutilizados proceduralmente.
- Eventos construídos pela combinação dos sistemas existentes, sem conteúdo roteirizado.
- Sem árvores de habilidades complexas, crafting, economia ou gerenciamento de dezenas de NPCs.

Esforço concentrado em poucas mecânicas altamente conectadas: um jogo profundo, rejogável e alcançável para uma equipe indie.
