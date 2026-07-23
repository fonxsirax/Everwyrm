using UnityEngine;

/// <summary>
/// Camada única de input do dragão: bindings num lugar só + BUFFER de comandos.
///
///  BINDINGS (rework hack and slash):
///   WASD mover (relativo à câmera no chão) · Ctrl SEGURADO = Stealth
///   Space = asa/decolar · Shift = Wing Boost/mergulho (ar) — no CHÃO, dash
///   lateral é Shift + A ou Shift + D (ver ChordDashLeft/Right)
///   LMB combo físico · RMB (ou F) fogo · Q cauda · E asas · T rugir
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

    /// <summary>Janela do CHORD do dash lateral (Shift+A/D): as duas teclas
    /// precisam ter sido apertadas (KeyDown FRESCO, não segurado de antes)
    /// dentro desse intervalo uma da outra.</summary>
    const float ChordWindow = 0.15f;

    static readonly float[] pressTime = new float[9];
    static int sampledFrame = -1;

    static float shiftDownTime = -99f, leftDownTime = -99f, rightDownTime = -99f;

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
        if (Input.GetKeyDown(KeyCode.E)) pressTime[(int)Act.Wing] = now;
        if (Input.GetKeyDown(KeyCode.T)) pressTime[(int)Act.Roar] = now;
        if (Input.GetKeyDown(KeyCode.R)) pressTime[(int)Act.Rest] = now;
        if (Input.GetKeyDown(KeyCode.G)) pressTime[(int)Act.Eat] = now;

        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) leftDownTime = now;
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) rightDownTime = now;
    }

    /// <summary>true 1× se a ação foi apertada dentro da janela — e a gasta.
    /// Só chamar no frame/estado em que a ação PODE executar.</summary>
    public static bool Consume(Act a, float window = BufferWindow)
    {
        if (Time.time - pressTime[(int)a] > window) return false;
        pressTime[(int)a] = -99f;
        return true;
    }

    /// <summary>Descarta um comando enfileirado (ex.: trocar de estado invalida o buffer).</summary>
    public static void Clear(Act a) => pressTime[(int)a] = -99f;

    public static bool Held(Act a) => a switch
    {
        Act.Flap => Input.GetKey(KeyCode.Space),
        Act.Burst => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
        Act.Melee => Input.GetMouseButton(0),
        Act.Fire => Input.GetMouseButton(1) || Input.GetKey(KeyCode.F),
        _ => false,
    };

    /// <summary>Ctrl segurado = passo de caçada (Stealth).</summary>
    public static bool StealthHeld =>
        Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    /// <summary>Dash lateral no chão: Shift + A/D precisam ser um CHORD —
    /// ambas as teclas com KeyDown fresco, próximas no tempo. Se a direcional
    /// já estava segurada de antes (parte de andar), o timestamp dela é velho
    /// e não conta: precisa soltar e apertar de novo perto do Shift.</summary>
    public static bool ConsumeDashRight() => ConsumeChord(ref rightDownTime);
    public static bool ConsumeDashLeft() => ConsumeChord(ref leftDownTime);

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
        _ => false,
    };

    public static bool RespawnDown => Input.GetKeyDown(KeyCode.Return);
}
