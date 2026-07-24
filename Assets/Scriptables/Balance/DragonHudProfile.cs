using UnityEngine;

/// <summary>
/// Layout e cadência da interface do dragão (minimapa e auto-criação do HUD).
/// Era o corpo de [SerializeField]s do DragonMinimap, mais os `autoCreateHud` que
/// estavam soltos no DragonVitals e no DragonAbilities.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/DragonHud.asset</b>.
/// Os ÍCONES do minimapa são ENTIDADE, não balanceamento: vivem no MinimapIconSet
/// (Resources/UI/MinimapIcons.asset).
/// </summary>
[BalanceAsset("Balance/DragonHud")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/HUD do Dragão", fileName = "DragonHud")]
public class DragonHudProfile : BalanceProfile
{
    [Header("Minimapa")]
    public float size = 220f;
    public float margin = 30f;
    [Tooltip("Alcance fixo do minimapa (m). 0 = usa o faro (Dominância do Instinto).")]
    public float rangeOverride = 0f;
    public float updateInterval = 0.1f;

    [Header("Auto-criação")]
    [Tooltip("Sem HUD na cena, o DragonVitals cria as barras sozinho no Play.")]
    public bool autoCreateVitalsHud = true;
    [Tooltip("Sem HUD de ataques na cena, o DragonAbilities cria os slots 1-4 sozinho.")]
    public bool autoCreateAttackHud = true;
}
