using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Receptor dos AnimationEvents "PlaySound" embutidos nos clips do Unka (Malbers).
/// Os FBX disparam PlaySound("Wing Flap") em toda batida de asa — sem um componente
/// com esse método no mesmo GameObject do Animator, o console enche de
/// "AnimationEvent 'PlaySound' has no receiver".
///
/// Uso: arraste os AudioClips na entrada correspondente ("Wing Flap") no Inspector
/// do prefab. Sem clip atribuído o evento é ignorado em silêncio (sem erros).
/// Vários clips na mesma entrada = variação aleatória a cada batida.
/// </summary>
public class DragonSounds : MonoBehaviour
{
    [Serializable]
    public class SoundEntry
    {
        [Tooltip("Nome enviado pelo AnimationEvent (ex.: \"Wing Flap\")")]
        public string eventName = "Wing Flap";
        [Tooltip("Um clip é sorteado a cada evento")]
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Variação aleatória de pitch (min, max)")]
        public Vector2 pitchRange = new(0.92f, 1.08f);
    }

    // "Wing Flap" já criado — só arrastar o áudio quando ele chegar
    [SerializeField] SoundEntry[] sounds = { new() };

    AudioSource source;
    Dictionary<string, SoundEntry> lookup;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;      // som 3D no mundo
            source.maxDistance = 80f;
        }

        lookup = new Dictionary<string, SoundEntry>();
        foreach (var e in sounds)
            if (!string.IsNullOrEmpty(e.eventName))
                lookup[e.eventName] = e;
    }

    /// <summary>Chamado pelos AnimationEvents dos FBX. Silencioso se não houver áudio.</summary>
    public void PlaySound(string eventName)
    {
        if (string.IsNullOrEmpty(eventName)) return;                 // 2 clips têm o campo vazio
        if (!lookup.TryGetValue(eventName, out var e)) return;
        if (e.clips == null || e.clips.Length == 0) return;          // áudio ainda não atribuído

        var clip = e.clips[UnityEngine.Random.Range(0, e.clips.Length)];
        if (clip == null) return;
        source.pitch = UnityEngine.Random.Range(e.pitchRange.x, e.pitchRange.y);
        source.PlayOneShot(clip, e.volume);
    }
}
