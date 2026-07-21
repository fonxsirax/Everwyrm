# Do skybox estático ao céu vivo: uma jornada de engenharia gráfica no HDRP

*(cópia do artigo para LinkedIn — os marcadores [IMAGEM N] indicam onde inserir cada screenshot da sessão, na ordem)*

---

Passei o dia inteiro em uma única missão no **Everwyrm**, meu sandbox survival de dragões em Unity/HDRP: transformar um céu HDRI estático — uma foto colada na esfera do mundo — em um sistema atmosférico completo. Ciclo dia/noite real, céu noturno procedural, lua com fases, nuvens dinâmicas e água que reflete tudo isso. O resultado me lembrou uma lição que a gente esquece com frequência: **qualidade visual em tempo real não vem de uma técnica mágica — vem da integração entre dezenas de sistemas que precisam concordar entre si.**

Este é o relato honesto dessa jornada, incluindo os bugs que quase me venceram.

## O ponto de partida: um céu que não escurece

O plano parecia simples: um dia completo em 30 minutos reais, com relógio determinístico (`double` acumulando horas — nada de perder precisão em sessões longas) servindo de fonte única de verdade para clima, fauna e gameplay futuros. Girar o sol, animar exposição e névoa por curvas, pronto.

Só que girar o sol não escurecia nada. O céu era um **HDRI estático** — a iluminação estava pintada na textura. A solução certa no HDRP é o **Physically Based Sky**: com ele, a posição real do sol tinge a atmosfera, o ambiente escurece sozinho e o pôr do sol sai "de graça". Montei tudo em um Volume global criado em runtime, com exposição e névoa dirigidas por curvas expostas ao designer.

E aí a primeira noite chegou — e foi um desastre.

[IMAGEM 1 — a primeira noite: breu absoluto, com árvores distantes misteriosamente claras]

## A saga da noite quebrada

A foto acima resume o problema: um breu ilegível... com árvores ao longe **brilhando como se fosse dia**. Nas rodadas seguintes, cada correção revelava a próxima camada do problema:

[IMAGEM 2 — segunda tentativa: noite ainda escura, faixa de vegetação clara à média distância]

Foram **quatro causas empilhadas**, cada uma com sua lição:

1. **Billboards unlit.** O LOD distante das árvores usava um material HDRP/Unlit com a luz do dia *assada na textura* — imune a qualquer iluminação. À noite, viravam lanternas.
2. **A fila Transparent que sobrevive à troca de shader.** Corrigi o shader do billboard... e nada mudou. O material carregava um `renderQueue = 3000` herdado — e **transparentes no HDRP não recebem sombras**. Trocar o shader não reseta a fila. Duas horas de caçada por um campo serializado invisível.
3. **Sombra da lua em baixa resolução.** Arbustos "acendiam" ao longe e escureciam de perto: a luz da lua, criada em runtime, vinha com shadow map default — objetos pequenos perdiam a própria sombra à distância. Bastou herdar a configuração de sombra do sol calibrado da cena.
4. **Meu próprio excesso.** Para matar luz baked residual, eu tinha zerado a iluminação indireta noturna — e o primeiro plano virou breu, fazendo qualquer coisa iluminada parecer "clara demais" por contraste. O ajuste fino: 0.3, não 0.02.

[IMAGEM 3 — pós-correção dos billboards: as copas pálidas restantes]
[IMAGEM 4 — os arbustos denunciando a sombra de baixa resolução]

Auditei então **os 74 materiais** de todos os prefabs do mundo procedural (floresta, deserto, tundra) com scripts de varredura — e converti o processo em correções idempotentes no auto-setup do projeto: se um asset futuro chegar com os mesmos defeitos, ele é consertado sozinho na recompilação.

[IMAGEM 5 — a noite finalmente coerente: escura, mas legível, sem vegetação-lanterna]

## O céu noturno: estudo antes de código

Com a noite estável, veio a parte que eu mais queria: um céu digno de parar o voo para olhar. Antes de codar, estudei **o código-fonte do HDRP 17** para usar o máximo do pipeline nativo — e a descoberta que definiu a arquitetura foi o `spaceEmissionTexture` do PBS: um cubemap renderizado **atrás da atmosfera**. Isso significa que as estrelas somem no amanhecer **por física** (a atmosfera iluminada as encobre — sem fade artificial), entram automaticamente nos reflexos, e giram com o `spaceRotation` (o firmamento nasce e se põe, custo zero).

As estrelas são 100% procedurais, geradas por shader em um cubemap usando **a seed do mundo** — cada mundo tem seu próprio céu, determinístico: três camadas de estrelas com brilho em lei de potência e cor por temperatura de corpo negro, mais uma Via Láctea de fBm. Estrelas cadentes raras (agendadas por processo de Poisson — nunca viram cadência) completaram o pacote.

E aí, ao dar Play... **céu preto. Nada.**

## O bug mais profundo: uma flag de configuração

A depuração virou um funil metódico: um *probe* que lê pixels do cubemap gerado (estava perfeito — 42 em HDR no zênite), um toggle de debug que mostrava o cubemap cru como HDRI (funcionou na primeira — provando que o problema era o caminho do PBS), simulação offline da matemática do shader em C# para validar o gerador, e leitura do log do editor para diagnosticar sem depender de prints.

[IMAGEM 6 — o modo debug: o cubemap cru provando que as estrelas existiam]

A causa raiz estava onde ninguém olha: o projeto tinha uma **cópia local do pacote de configuração do HDRP** com `PrecomputedAtmosphericAttenuation = 1` — uma flag que, silenciosamente, **desliga todo o modo "espaço" do Physically Based Sky**. Uma linha, num pacote embutido que veio com o template do projeto. Flag corrigida (nos dois arquivos — o C# e o HLSL gerado precisam bater), shaders recompilados e:

[IMAGEM 7 — as primeiras estrelas atravessando a atmosfera do PBS]

## A lua que não queria aparecer

Faltava a protagonista. A luz da lua funcionava (dava para ver o reflexo especular na água!), mas o disco... nada. Mais duas armadilhas de HDRP para a coleção:

[IMAGEM 8 — Scene view: o campo estelar e o reflexo da lua na água — a luz existia, o disco não]

1. **`diameterMultiplerMode`**: com o valor default, o renderer ignora o `angularDiameter` que você seta e usa um `diameterOverride` de 0.5°. Minha lua de 7° era na verdade uma lasca invisível.
2. **A ordem de registro do `AddHDLight`**: o próprio helper do HDRP registra a luz no banco de dados *antes* de definir o tipo Directional — e a classificação acontece no registro. Resultado: a lua nunca entrava na lista de corpos celestes. Criar a `Light` direcional *antes* do componente HD resolveu. (Essa merece bug report.)

[IMAGEM 9 — a lua apareceu... estourada, um farol branco]
[IMAGEM 10 — mas o outro lado da noite já mostrava o potencial: crepúsculo azul com estrelas]

## Fotometria: calibrando com números, não com chutes

A lua estourava; depois, com a textura real da NASA (albedo de rocha escura, ~12%), virou um **"eclipse"** — um disco mais escuro que o próprio céu, com o halo dominando. A essa altura, adotei o método que salvou todas as calibrações: **calcular as luminâncias em cd/m²** de cada elemento contra a exposição, e definir razões de contraste alvo — céu ~4.6, nuvens ~22 (5× o céu), estrelas médias ~9 (2× o céu), halo ~104, disco ~250 (50× o céu, 2.4× o halo). Cada "chute" virou uma equação.

[IMAGEM 11 — dragão sob o céu com nuvens ao luar e a lua-eclipse: bonito, mas ainda errado]
[IMAGEM 12 — a astrofoto que escolhemos para a superfície do disco]
[IMAGEM 13 — o close do problema: o halo engolindo o disco escuro]
[IMAGEM 14 — deserto noturno com a calibração quase lá: estrelas vivas, lua ainda em anel]

No meio do caminho, mais um clássico: **"No more space in the 2D Cookie Texture Atlas"** — a textura da lua vai para o atlas de cookies, precisa ser potência de 2, e o atlas dos assets de qualidade estava em 512. Detalhes que nenhum tutorial menciona.

E então, com disco, halo, céu e exposição finalmente concordando:

[IMAGEM 15 — a noite final: lua com crateras e glow suave, céu azul-profundo, estrelas coloridas, nuvens prata]

## O fechamento: céu na água

O último ato foi fazer a água **pertencer** ao céu. A base já estava certa — a WaterSurface nativa do HDRP reflete o céu por design, com Fresnel físico (espelho no rasante, transparente de cima). Mas três bloqueios seguravam tudo: **SSR desligado** no projeto inteiro, um perfil herdado multiplicando reflexos por 0.7, e — de novo eu — meu dim noturno de indireta cortando os reflexos para 30% junto com a luz difusa. Separar os dois canais (difusa segue a noite; reflexo fica sempre em 100%) reconectou a água ao mundo.

[IMAGEM 16 — o oásis à noite: primeiro escuro demais (a física do Fresnel visto de cima mostra o fundo, não o céu)]
[IMAGEM 17 — com a limpidez calibrada: degradê de profundidade, corpo azul-petróleo, specular da lua]
[IMAGEM 18 — e de dia: o Fresnel completo em uma foto — céu espelhado no rasante, água límpida de cima, areia dourada no raso]

## O que fica

Se eu tivesse que resumir o dia em uma frase para outros devs: **o céu bonito não foi uma feature — foi um acordo entre uns quinze sistemas.** O PBS não brilha se uma flag de config o sabota; as estrelas não aparecem se a exposição não as respeita; a lua não existe se o banco de luzes a classificou errado no registro; a água não reflete se dois multiplicadores herdados a apagam. Nenhuma dessas peças é difícil sozinha. O trabalho — e a qualidade — está em fazê-las conversar.

Três práticas que fizeram diferença: **estudar o código-fonte do pipeline antes de implementar** (cada API que usei foi verificada no fonte do HDRP instalado, não em docs genéricas); **depurar com instrumentos, não com fé** (probes de pixel, toggles de isolamento, simulação offline, logs); e **calibrar com fotometria** (razões de contraste em cd/m² transformam "está feio" em "o disco precisa de 50× o céu").

O Everwyrm agora tem dias dourados, crepúsculos de fogo e noites que fazem o jogador — e o desenvolvedor — pararem de voar só para olhar para cima.

*(Desenvolvido em pair-programming com IA — o processo de investigação a quatro mãos, com a IA lendo o código-fonte do HDRP e os logs do editor enquanto eu validava visualmente cada rodada, merece um artigo próprio.)*

#gamedev #unity3d #hdrp #graphics #indiedev #madewithunity
