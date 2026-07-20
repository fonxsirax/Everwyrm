using UnityEngine;

/// <summary>
/// Configuração dos ícones do minimapa — edite o asset
/// `Assets/Scriptables/Resources/MinimapIcons.asset` (sem tocar na cena).
/// Sprites vazios = o minimapa usa os gerados em código (seta/X/ponto).
/// O DragonMinimap carrega este asset via Resources.Load("MinimapIcons").
/// </summary>
[CreateAssetMenu(menuName = "Everwyrm/Minimap Icon Set", fileName = "MinimapIcons")]
public class MinimapIconSet : ScriptableObject
{
    [Header("Sprites (vazio = gerado em código)")]
    public Sprite dragonSprite;   // seta do dragão
    public Sprite foodSprite;     // comida (X)
    public Sprite treeSprite;     // árvore (ponto)
    public Sprite animalSprite;   // fauna viva (ponto)
    public Sprite sunSprite;      // relógio: ícone de dia
    public Sprite moonSprite;     // relógio: ícone de noite

    [Header("Cores (tintam o sprite)")]
    public Color dragonColor = Color.white;
    public Color foodColor = new(1f, 0.7f, 0.25f);
    public Color treeColor = new(0.35f, 0.75f, 0.3f, 0.8f);
    public Color animalColor = new(0.55f, 0.85f, 1f, 0.9f);    // presas
    public Color predatorColor = new(1f, 0.4f, 0.35f, 0.95f);  // predadores
    public Color sunColor = new(1f, 0.85f, 0.35f);             // relógio: dia
    public Color moonColor = new(0.75f, 0.82f, 1f);            // relógio: noite

    [Header("Tamanhos (px no minimapa)")]
    public float dragonSize = 24f;
    public float foodSize = 14f;
    public float treeSize = 6f;
    public float animalSize = 8f;
}
