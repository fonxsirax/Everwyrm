using UnityEngine;

/// <summary>
/// Regras de LOADOUT do combate — quantos slots o dragão usa e de onde vêm os ataques.
/// Era o corpo de [SerializeField]s do DragonAbilities.
///
/// Os ATAQUES em si são ENTIDADES (DragonAttackData, em Resources/Entities/Attacks);
/// aqui fica só o que é tuning: quantos cabem e qual é o loadout inicial.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/DragonCombat.asset</b>.
/// </summary>
[BalanceAsset("Balance/DragonCombat")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Combate do Dragão", fileName = "DragonCombat")]
public class DragonCombatProfile : BalanceProfile
{
    [Header("Slots")]
    [Tooltip("Slots ativos por padrão, sem mutação. O teto RÍGIDO é " +
             "DragonAbilities.MaxSlotCount (dimensiona os arrays) — subir além dele " +
             "exige mexer na constante.")]
    public int baseSlotCount = 4;

    [Header("Loadout inicial (vazio — tudo vem por desbloqueio de maturidade)")]
    [Tooltip("Ataques já equipados no começo. Deixe vazio para que o dragão destrave " +
             "tudo crescendo, que é o desenho do GDD.")]
    public DragonAttackData[] startingSlots = new DragonAttackData[0];

    [Tooltip("Ataques extras além dos carregados de Resources/Entities/Attacks — para " +
             "um golpe que não deva entrar no catálogo geral (protótipo, boss, teste).")]
    public DragonAttackData[] extraAttacks = new DragonAttackData[0];
}
