using UnityEngine;

/// <summary>
/// Camada única de input do dragão: bindings num lugar só + BUFFER de comandos.
///
///  BINDINGS (rework hack and slash):
///   WASD mover (relativo à câmera no chão) · Ctrl SEGURADO = Stealth
///   Space = asa/decolar · Shift SEGURADO (ar) = mergulho · Wing Boost = Shift + W
///   (chord fresco, ver ConsumeBoost) · esquiva lateral = Shift + A/D (chão e ar,
///   ver ConsumeDashLeft/Right) — Shift PURO não acelera mais
///   LMB combo físico · RMB (ou F) fogo · Q cauda · P asas · T rugir
///   E = liga/desliga MODO MIRA (retícula segue o mouse, ver DragonAim)
///   R descansar · G comer · 1-4 habilidades · Tab ficha
///
///  BUFFER: apertar um pouco ANTES de poder agir enfileira o comando — quem
///  consome (DragonController) chama Consume() no primeiro frame permitido e a
///  ação sai sozinha. Elimina o "o jogo ignorou meu comando" dos action games.
///
/// Centralizado para o suporte a gamepad (GDD, em aberto) trocar só esta classe.
/// </summary>
public static class DragonInput
{
    public enum Act { Flap, Burst, Melee, Fire, Tail, Wing, Roar, Rest, Eat }

    /// <summary>Janela padrão do buffer (s) — padrão de action games: 0.15–0.25.</summary>
    public const float BufferWindow = 0.2f;

    /// <summary>Janela do CHORD da esquiva lateral (Shift+A/D): as duas teclas
    /// precisam ter sido apertadas (KeyDown FRESCO, não segurado de antes)
    /// dentro desse intervalo uma da outra.</summary>
    const float ChordWindow = 0.15f;

    static readonly float[] pressTime = new float[9];
    static int sampledFrame = -1;

    static float shiftDownTime = -99f, leftDownTime = -99f, rightDownTime = -99f;
    static float forwardDownTime = -99f;   // W/↑ fresco — p/ o chord do Wing Boost

    static DragonInput()
    {
        for (int i = 0; i < pressTime.Length; i++) pressTime[i] = -99f;
    }

    /// <summary>Colhe os KeyDown do frame. Chamar 1× por frame (DragonController)
    /// ANTES de qualquer Consume — idempotente dentro do mesmo frame.</summary>
    public static void Sample()
    {
        if (Time.frameCount == sampledFrame) return;
        sampledFrame = Time.frameCount;

        float now = Time.time;
        if (Input.GetKeyDown(KeyCode.Space)) pressTime[(int)Act.Flap] = now;
        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
        {
            pressTime[(int)Act.Burst] = now;
            shiftDownTime = now;
        }
        if (Input.GetMouseButtonDown(0)) pressTime[(int)Act.Melee] = now;
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.F))
            pressTime[(int)Act.Fire] = now;
        if (Input.GetKeyDown(KeyCode.Q)) pressTime[(int)Act.Tail] = now;
        if (Input.GetKeyDown(KeyCode.P)) pressTime[(int)Act.Wing] = now;  // asa saiu do E (agora mira)
        if (Input.GetKeyDown(KeyCode.T)) pressTime[(int)Act.Roar] = now;
        if (Input.GetKeyDown(KeyCode.R)) pressTime[(int)Act.Rest] = now;
        if (Input.GetKeyDown(KeyCode.G)) pressTime[(int)Act.Eat] = now;

        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) leftDownTime = now;
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) rightDownTime = now;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) forwardDownTime = now;
    }

    /// <summary>true 1× se a ação foi apertada dentro da janela — e a gasta.
    /// Só chamar no frame/estado em que a ação PODE executar.</summary>
    public static bool Consume(Act a, float window = BufferWindow)
    {
        if (Time.time - pressTime[(int)a] > window) return false;
        pressTime[(int)a] = -99f;
        return true;
    }

    /// <summary>Peek: há um comando 'a' bufferizado AGORA (sem gastá-lo)? Usado
    /// para detectar interrupção (cancelar a esquiva) antes de quem consome agir.</summary>
    public static bool Pending(Act a, float window = BufferWindow) =>
        Time.time - pressTime[(int)a] <= window;

    /// <summary>Descarta um comando enfileirado (ex.: trocar de estado invalida o buffer).</summary>
    public static void Clear(Act a) => pressTime[(int)a] = -99f;

    /// <summary>Zera os comandos de AÇÃO bufferizados (golpe/fogo/rugido/comer/asa/
    /// cauda/decolar). A esquiva chama ao começar: só um toque NOVO durante ela a
    /// cancela — um comando enfileirado de ANTES não a "come" no primeiro frame.</summary>
    public static void ClearActionBuffers()
    {
        Clear(Act.Flap); Clear(Act.Melee); Clear(Act.Fire); Clear(Act.Tail);
        Clear(Act.Wing); Clear(Act.Roar); Clear(Act.Eat);
    }

    /// <summary>Alguma tecla de MOVIMENTO (WASD/setas) foi apertada NESTE frame?
    /// KeyDown = fresco: segurar de antes não conta (cancela a esquiva só com um
    /// toque novo, como o usuário pediu).</summary>
    public static bool MovePressedFresh() =>
        Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) ||
        Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D) ||
        Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
        Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow);

    /// <summary>Alguma habilidade (1-4) foi apertada NESTE frame?</summary>
    public static bool AbilityAnyDown() =>
        Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2) ||
        Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Alpha4);

    public static bool Held(Act a) => a switch
    {
        Act.Flap => Input.GetKey(KeyCode.Space),
        Act.Burst => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
        Act.Melee => Input.GetMouseButton(0),
        Act.Fire => Input.GetMouseButton(1) || Input.GetKey(KeyCode.F),
        _ => false,
    };

    /// <summary>Tecla E: liga/desliga o modo mira (toggle — sem buffer, é modal).</summary>
    public static bool AimToggleDown => Input.GetKeyDown(KeyCode.E);

    /// <summary>Ctrl segurado = passo de caçada (Stealth).</summary>
    public static bool StealthHeld =>
        Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    /// <summary>Esquiva lateral no chão: Shift + A/D precisam ser um CHORD —
    /// ambas as teclas com KeyDown fresco, próximas no tempo. Se a direcional
    /// já estava segurada de antes (parte de andar), o timestamp dela é velho
    /// e não conta: precisa soltar e apertar de novo perto do Shift.</summary>
    public static bool ConsumeDashRight() => ConsumeChord(ref rightDownTime);
    public static bool ConsumeDashLeft() => ConsumeChord(ref leftDownTime);

    /// <summary>Wing Boost em voo: Shift + W precisam ser um CHORD FRESCO (ambos
    /// KeyDown recentes, próximos no tempo) — mesma regra da esquiva. Shift puro,
    /// ou W já segurado quando o Shift chega, NÃO conta: some o boost fantasma que
    /// se misturava com a esquiva lateral (Shift+A/D).</summary>
    public static bool ConsumeBoost() => ConsumeChord(ref forwardDownTime);

    static bool ConsumeChord(ref float dirDownTime)
    {
        float now = Time.time;
        if (now - shiftDownTime > ChordWindow || now - dirDownTime > ChordWindow) return false;
        shiftDownTime = -99f;
        dirDownTime = -99f;
        return true;
    }

    // eixos CRUS: a resposta instantânea é nossa; suavização só onde o visual pede
    public static float Horizontal => Input.GetAxisRaw("Horizontal");
    public static float Vertical => Input.GetAxisRaw("Vertical");

    public static bool AbilityDown(int slot) => slot switch
    {
        0 => Input.GetKeyDown(KeyCode.Alpha1),
        1 => Input.GetKeyDown(KeyCode.Alpha2),
        2 => Input.GetKeyDown(KeyCode.Alpha3),
        3 => Input.GetKeyDown(KeyCode.Alpha4),
        4 => Input.GetKeyDown(KeyCode.Alpha5),   // slot extra (mutação — raro)
        _ => false,
    };

    public static bool RespawnDown => Input.GetKeyDown(KeyCode.Return);
}
