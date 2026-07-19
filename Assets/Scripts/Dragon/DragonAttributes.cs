using System;
using UnityEngine;

/// <summary>
/// Atributos evolutivos do GDD: Velocidade, Poder e Resistência.
///
/// Sem XP tradicional: o LEVEL vem do CRESCIMENTO (tempo vivido + alimentação).
/// Cada nível concede 1 ponto, gasto com as teclas 1 / 2 / 3.
///
///  1 VELOCIDADE  — velocidade máxima (chão e voo), aceleração, mergulho.
///  2 PODER       — dano físico, tamanho/dano da chama e DOMINÂNCIA TERRITORIAL:
///                  o raio do "faro" em que o dragão detecta alimento (e, no futuro,
///                  eventos, perigos e outros dragões no minimapa).
///  3 RESISTÊNCIA — vida, energia total (duração do voo), aceleração e
///                  resistência à fome.
///
/// Observer: OnChanged / OnLevelUp para HUD e outros sistemas.
/// </summary>
[RequireComponent(typeof(DragonGrowth))]
public class DragonAttributes : MonoBehaviour
{
    [Header("Level (derivado do crescimento)")]
    [SerializeField] int maxLevel = 10;
    [SerializeField] int startingPoints = 5;   // pontos liberados no início do jogo

    [Header("Ganho por ponto")]
    [SerializeField] float speedPerPoint = 0.06f;        // +6% vel. máxima
    [SerializeField] float accelSpeedPerPoint = 0.08f;   // aceleração (velocidade)
    [SerializeField] float accelResistPerPoint = 0.03f;  // aceleração (resistência)
    [SerializeField] float damagePerPoint = 0.10f;
    [SerializeField] float flamePerPoint = 0.08f;
    [SerializeField] float dominanceBase = 30f;          // raio base do faro (m)
    [SerializeField] float dominancePerPoint = 12f;
    [SerializeField] float healthPerPoint = 0.08f;
    [SerializeField] float energyPerPoint = 0.10f;       // duração do voo
    [SerializeField] float hungerResistPerPoint = 0.04f; // fome cai mais devagar
    [SerializeField] float takeoffClimbPerPoint = 0.12f; // duração da subida de decolagem

    DragonGrowth growth;

    int velocidade, poder, resistencia;

    public int Level { get; private set; } = 1;
    public int Unspent { get; private set; }
    public int Velocidade => velocidade;
    public int Poder => poder;
    public int Resistencia => resistencia;

    // ---- Multiplicadores consumidos pelos outros sistemas
    public float SpeedMul => 1f + Velocidade * speedPerPoint;
    public float AccelMul => 1f + Velocidade * accelSpeedPerPoint
                                + Resistencia * accelResistPerPoint;
    public float DamageMul => 1f + Poder * damagePerPoint;          // (futuro combate)
    public float FlameSizeMul => 1f + Poder * flamePerPoint;        // (futuro fogo)
    public float DominanceRadius => dominanceBase + Poder * dominancePerPoint;
    public float MaxHealthMul => 1f + Resistencia * healthPerPoint;
    public float MaxEnergyMul => 1f + Resistencia * energyPerPoint;
    public float HungerDecayMul => Mathf.Max(0.4f, 1f - Resistencia * hungerResistPerPoint);
    /// <summary>Resistência estica a fase de subida contínua da decolagem (DragonFlight).</summary>
    public float TakeoffClimbMul => 1f + Resistencia * takeoffClimbPerPoint;

    // ---- Observer
    public event Action<DragonAttributes> OnChanged;
    public event Action<int> OnLevelUp;

    void Awake()
    {
        growth = GetComponent<DragonGrowth>();
    }

    void OnEnable() => growth.OnGrowthChanged += OnGrowth;
    void OnDisable() => growth.OnGrowthChanged -= OnGrowth;

    void Start()
    {
        OnGrowth(growth);              // nível inicial
        if (startingPoints > 0)
        {
            Unspent += startingPoints; // pontos iniciais liberados
            OnChanged?.Invoke(this);
        }
    }

    public enum Attribute { Velocidade, Poder, Resistencia }

    void Update()
    {
        if (Unspent <= 0) return;
        if (Input.GetKeyDown(KeyCode.Alpha1)) SpendPoint(Attribute.Velocidade);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SpendPoint(Attribute.Poder);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SpendPoint(Attribute.Resistencia);
    }

    /// <summary>Gasta 1 ponto no atributo (usado pelas teclas e pelo menu).</summary>
    public bool SpendPoint(Attribute a)
    {
        if (Unspent <= 0) return false;
        switch (a)
        {
            case Attribute.Velocidade: Spend(ref velocidade); break;
            case Attribute.Poder: Spend(ref poder); break;
            case Attribute.Resistencia: Spend(ref resistencia); break;
        }
        return true;
    }

    // valores por ponto expostos para o menu descrever os ganhos
    public float SpeedPerPoint => speedPerPoint;
    public float AccelSpeedPerPoint => accelSpeedPerPoint;
    public float AccelResistPerPoint => accelResistPerPoint;
    public float DamagePerPoint => damagePerPoint;
    public float FlamePerPoint => flamePerPoint;
    public float DominancePerPoint => dominancePerPoint;
    public float HealthPerPoint => healthPerPoint;
    public float EnergyPerPoint => energyPerPoint;
    public float HungerResistPerPoint => hungerResistPerPoint;
    public float TakeoffClimbPerPoint => takeoffClimbPerPoint;

    void OnGrowth(DragonGrowth g)
    {
        // crescimento 0..1 mapeia para nível 1..maxLevel
        int target = 1 + Mathf.FloorToInt(g.Growth01 * (maxLevel - 1) + 0.0001f);
        while (Level < target)
        {
            Level++;
            Unspent++;
            OnLevelUp?.Invoke(Level);
            OnChanged?.Invoke(this);
        }
    }

    void Spend(ref int attribute)
    {
        attribute++;
        Unspent--;
        OnChanged?.Invoke(this);
    }
}
