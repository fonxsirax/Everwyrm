using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base de TODO asset de BALANCEAMENTO — os números de tuning do gameplay, separados
/// das ENTIDADES (dragões, skins, ataques, animais), que vivem em Scriptables/Entities.
///
/// A regra da refatoração (jul/2026): nenhum número de gameplay mora mais num
/// [SerializeField] de componente. Quem consome guarda só uma REFERÊNCIA opcional ao
/// profile e cai no asset padrão de <c>Resources/Balance</c> quando ela está vazia —
/// o mesmo padrão que o FlightProfile já usava, agora valendo para todos.
///
/// Ajustar o jogo = editar um asset em <b>Assets/Scriptables/Resources/Balance</b>.
/// Nunca mais mexer nos scripts do Unka Realistic.
///
/// ESCALA: uma espécie/upgrade futuro é um asset NOVO do mesmo tipo, arrastado no
/// campo `profile` do componente (ou apontado por um DragonRecord) — sem tocar em código.
/// </summary>
public abstract class BalanceProfile : ScriptableObject
{
}

/// <summary>
/// Onde vive o asset PADRÃO de um profile, relativo a uma pasta Resources
/// (ex.: "Balance/DragonMovement" → Assets/Scriptables/Resources/Balance/DragonMovement.asset).
/// É o que o <see cref="Balance"/> usa para resolver o fallback sem que cada
/// componente repita a string.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BalanceAssetAttribute : Attribute
{
    public readonly string resourcePath;
    public BalanceAssetAttribute(string resourcePath) { this.resourcePath = resourcePath; }
}

/// <summary>
/// Resolvedor dos profiles padrão. Um Resources.Load por tipo na vida do processo
/// (cache), então dá para chamar no Awake de qualquer componente sem custo.
///
/// Uso no consumidor:
/// <code>
/// [SerializeField] DragonMovementProfile profile;   // vazio = o padrão
/// void Awake() =&gt; profile = Balance.Resolve(profile);
/// </code>
/// </summary>
public static class Balance
{
    static readonly Dictionary<Type, ScriptableObject> cache = new();

    /// <summary>Devolve `explicitProfile` se ele existir; senão o asset padrão do tipo.
    /// Loga uma vez (por tipo) se o asset padrão sumiu — sem isso a falha viraria um
    /// NullReference solto, longe da causa.</summary>
    public static T Resolve<T>(T explicitProfile) where T : ScriptableObject =>
        explicitProfile != null ? explicitProfile : Default<T>();

    /// <summary>Como <see cref="Default{T}"/>, mas SILENCIOSO: devolve null sem logar se o
    /// asset (ou o atributo) faltar. Para profiles OPCIONAIS — ex.: o modo balanceamento
    /// (DragonBalanceOverride), cuja ausência significa "comportamento normal", não erro.
    /// Compartilha o mesmo cache do Default.</summary>
    public static T TryDefault<T>() where T : ScriptableObject
    {
        var type = typeof(T);
        if (cache.TryGetValue(type, out var cached)) return (T)cached;

        var attr = (BalanceAssetAttribute)Attribute.GetCustomAttribute(type, typeof(BalanceAssetAttribute));
        var loaded = attr != null ? Resources.Load<T>(attr.resourcePath) : null;
        cache[type] = loaded;
        return loaded;
    }

    /// <summary>O asset padrão do tipo (Resources/&lt;BalanceAsset.resourcePath&gt;).</summary>
    public static T Default<T>() where T : ScriptableObject
    {
        var type = typeof(T);
        if (cache.TryGetValue(type, out var cached)) return (T)cached;

        var attr = (BalanceAssetAttribute)Attribute.GetCustomAttribute(type, typeof(BalanceAssetAttribute));
        if (attr == null)
        {
            Debug.LogError($"{type.Name} não tem [BalanceAsset(\"...\")] — sem isso não há " +
                           "asset padrão para carregar. Acrescente o atributo na classe.");
            cache[type] = null;
            return null;
        }

        var loaded = Resources.Load<T>(attr.resourcePath);
        if (loaded == null)
            Debug.LogError($"Balanceamento AUSENTE: Resources/{attr.resourcePath} " +
                           $"({type.Name}). Recrie com Tools > Everwyrm > Balanceamento — " +
                           "Recriar Assets Faltantes.");
        cache[type] = loaded;
        return loaded;
    }

    /// <summary>Esquece os profiles carregados — o editor chama ao recriar/reimportar
    /// assets para que o Play seguinte não sirva um asset morto do cache.</summary>
    public static void ClearCache() => cache.Clear();
}
