using UnityEditor;
using UnityEngine;

/// <summary>
/// Cria/religa as três habilidades de PROJÉTIL COM MIRA (aimBeforeFire) — Bola da
/// Natureza, Bola de Fogo e Bola de Gelo — reutilizando o visual dos prefabs Hovl
/// "AAA Projectiles Vol 2". Menu: Tools > Everwyrm > Habilidades de Projétil.
///
/// Cada uma é só um asset DragonAttackData em Resources/Entities/Attacks (mesmo padrão
/// data-driven dos outros ataques): usesProjectile + aimBeforeFire fazem a tecla ABRIR
/// o modo mira (DragonAim) e esperar o clique — o disparo, dano, cooldown e energia
/// passam pela MESMA infra de <see cref="DragonAbilities"/> / <see cref="AttackProjectile"/>.
/// O prefab Hovl entra só como VISUAL; o AttackProjectile neutraliza o mover/colisor dele.
///
/// Idempotente: rerodar não sobrescreve tuning (dano/cooldown/energia) de assets já
/// existentes — só garante que o prefab visual e as flags de mira/voo estejam ligados.
/// </summary>
public static class ProjectileAbilitiesSetup
{
    const string Dir = "Assets/Scriptables/Resources/Entities/Attacks";
    const string PrefabDir = "Assets/Hovl Studio/AAA Projectiles Vol 2/Prefabs";

    [MenuItem("Tools/Everwyrm/Habilidades de Projétil (Nature/Fire/Ice)")]
    public static void Setup()
    {
        EnsureFolder();

        // BOLA DA NATUREZA — Projétil 1 (nature): projétil físico de alvo único.
        Wire("NatureBall", "Projectile 1 nature.prefab", a =>
        {
            a.attackName = "Bola da Natureza";
            a.description = "Concentra energia da natureza e lança uma esfera veloz " +
                            "que atinge UM alvo na direção da mira.";
            a.unlockLevel = 2;
            a.energyCost = 10f;
            a.baseDamage = 16f;
            a.cooldown = 2.2f;
            a.isFire = false;
            a.appliesBurn = false;
        });

        // BOLA DE FOGO — Projétil 4 (fire): reaproveita o exemplo já existente, agora
        // com mira e o visual Hovl; incendeia o alvo. Tuning preservado se já existia.
        Wire("FireBall", "Projectile 4 fire.prefab", a =>
        {
            a.attackName = "Bola de Fogo";
            a.description = "Cospe uma bola de fogo veloz que atinge UM alvo na direção " +
                            "da mira e o incendeia.";
            a.unlockLevel = 2;
            a.energyCost = 12f;
            a.baseDamage = 18f;
            a.cooldown = 2.5f;
            a.isFire = true;
            a.appliesBurn = true;
            a.burnDamagePerSecond = 4f;
            a.burnDuration = 4f;
        });

        // BOLA DE GELO — Projétil 5 (ice): esfera gélida de alvo único.
        Wire("IceBall", "Projectile 5 ice.prefab", a =>
        {
            a.attackName = "Bola de Gelo";
            a.description = "Forma uma esfera de gelo e a arremessa na direção da mira, " +
                            "atingindo UM alvo.";
            a.unlockLevel = 3;
            a.energyCost = 11f;
            a.baseDamage = 17f;
            a.cooldown = 2.6f;
            a.isFire = false;
            a.appliesBurn = false;
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("<b>Habilidades de projétil prontas!</b> Bola da Natureza, Bola de Fogo e " +
                  "Bola de Gelo criadas/religadas em " + Dir + ". Elas abrem o modo mira ao " +
                  "acionar a tecla — clique esquerdo dispara, direito cancela. Chão e voo.");
    }

    // Carrega (ou cria) o asset, aplica a config de PROJÉTIL COM MIRA comum, roda o
    // 'tune' específico só quando o asset é NOVO (preserva tuning manual do que já existe)
    // e SEMPRE garante o prefab visual + as flags de projétil/mira/voo ligados.
    static void Wire(string file, string prefabName, System.Action<DragonAttackData> tune)
    {
        string path = $"{Dir}/{file}.asset";
        var a = AssetDatabase.LoadAssetAtPath<DragonAttackData>(path);
        bool isNew = a == null;
        if (isNew)
        {
            a = ScriptableObject.CreateInstance<DragonAttackData>();
            // defaults de projétil de alvo único (só p/ assets NOVOS)
            a.animation = DragonAttackAnimation.FireBreath;   // fallback se a layer mascarada faltar
            a.castTime = 0.4f;                                // sobrescrito p/ 0.1 abaixo
            a.animationLock = 0.7f;                           // sustentação do gesto mascarado
            a.range = 60f;
            a.projectileSpeed = 34f;
            a.projectileHitRadius = 1.2f;
            a.explodesOnImpact = false;
            a.areaDamage = false;
            a.isPhysical = true;
            AssetDatabase.CreateAsset(a, path);
        }

        // flags do fluxo — sempre garantidas (mesmo em asset antigo): projétil, mira e voo
        a.usesProjectile = true;
        a.aimBeforeFire = true;
        a.usableInFlight = true;

        // cuspe MASCARADO (Unka Fire Mask): gesto de pescoço/cabeça por cima do voo, sem
        // travar o dragão. E o release do projétil 0,1s após o início da animação (castTime
        // = "segundos entre o início da animação e o efeito disparar") para ficar condizente.
        a.fireMaskCast = DragonFireMaskCast.FireBall;
        a.castTime = 0.1f;

        if (isNew) tune(a);

        var vis = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{prefabName}");
        if (vis != null) a.projectilePrefab = vis;
        else Debug.LogWarning($"Prefab de projétil não encontrado: {PrefabDir}/{prefabName} " +
                              $"— {file} vai usar o glow gerado por código.");

        EditorUtility.SetDirty(a);
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scriptables"))
            AssetDatabase.CreateFolder("Assets", "Scriptables");
        if (!AssetDatabase.IsValidFolder("Assets/Scriptables/Resources"))
            AssetDatabase.CreateFolder("Assets/Scriptables", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Scriptables/Resources/Entities"))
            AssetDatabase.CreateFolder("Assets/Scriptables/Resources", "Entities");
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets/Scriptables/Resources/Entities", "Attacks");
    }
}
