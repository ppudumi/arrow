using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// [임시 결정] 효과음은 외부 음원 대신 런타임에 합성한 짧은 소리로 대체한다 (저작권·용량 문제 없음).
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx I { get; private set; }
        public static float Volume = 0.6f;

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly List<AudioSource> sources = new List<AudioSource>();
        private int next;
        private readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();

        private void Awake()
        {
            I = this;
            for (int i = 0; i < 12; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                sources.Add(s);
            }
            Build();
        }

        private void Build()
        {
            const int sr = 22050;
            clips["shoot"] = Synth(sr, 0.12f, t => Noise() * 0.5f * Env(t, 0.12f) + Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(900, 300, t / 0.12f)) * 0.4f * Env(t, 0.12f));
            clips["hit"] = Synth(sr, 0.10f, t => (Noise() * 0.6f + Mathf.Sin(t * 2 * Mathf.PI * 180) * 0.5f) * Env(t, 0.10f));
            clips["wall"] = Synth(sr, 0.06f, t => Noise() * 0.4f * Env(t, 0.06f));
            clips["pick"] = Synth(sr, 0.09f, t => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(700, 1300, t / 0.09f)) * 0.45f * Env(t, 0.09f));
            clips["recall"] = Synth(sr, 0.35f, t => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(300, 900, t / 0.35f)) * 0.35f * Env(t, 0.35f));
            clips["hurt"] = Synth(sr, 0.22f, t => (Mathf.Sign(Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(320, 120, t / 0.22f))) * 0.3f + Noise() * 0.2f) * Env(t, 0.22f));
            clips["boom"] = Synth(sr, 0.45f, t => (Noise() * 0.8f + Mathf.Sin(t * 2 * Mathf.PI * 60) * 0.5f) * Env(t, 0.45f));
            clips["zap"] = Synth(sr, 0.25f, t => (Noise() * (Mathf.Sin(t * 400) > 0 ? 0.7f : 0.1f)) * Env(t, 0.25f));
            clips["ui"] = Synth(sr, 0.05f, t => Mathf.Sin(t * 2 * Mathf.PI * 1200) * 0.3f * Env(t, 0.05f));
            clips["coin"] = Synth(sr, 0.14f, t => Mathf.Sin(t * 2 * Mathf.PI * (t < 0.05f ? 988 : 1319)) * 0.35f * Env(t, 0.14f));
            clips["roar"] = Synth(sr, 0.8f, t => (Noise() * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(90, 50, t / 0.8f)) * 0.6f) * Env(t, 0.8f));
            clips["swing"] = Synth(sr, 0.18f, t => Noise() * 0.5f * Mathf.Sin(t / 0.18f * Mathf.PI));
            clips["empty"] = Synth(sr, 0.05f, t => Mathf.Sin(t * 2 * Mathf.PI * 220) * 0.3f * Env(t, 0.05f));
            clips["buy"] = Synth(sr, 0.3f, t => Mathf.Sin(t * 2 * Mathf.PI * (t < 0.1f ? 660 : t < 0.2f ? 880 : 1320)) * 0.3f * Env(t, 0.3f));
            clips["enemyshot"] = Synth(sr, 0.1f, t => Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(500, 200, t / 0.1f)) * 0.3f * Env(t, 0.1f));
        }

        private static readonly System.Random noiseRng = new System.Random(7);
        private static float Noise() => (float)(noiseRng.NextDouble() * 2.0 - 1.0);
        private static float Env(float t, float len) => Mathf.Clamp01(1f - t / len) * Mathf.Clamp01(t * 200f);

        private static AudioClip Synth(int sr, float len, System.Func<float, float> f)
        {
            int n = Mathf.CeilToInt(sr * len);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)sr), -1f, 1f);
            var clip = AudioClip.Create("sfx", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static void Play(string id, float vol = 1f, float pitch = 1f)
        {
            if (I == null) return;
            I.PlayInternal(id, vol, pitch);
        }

        private void PlayInternal(string id, float vol, float pitch)
        {
            if (!clips.TryGetValue(id, out var clip)) return;
            float now = Time.unscaledTime;
            if (lastPlay.TryGetValue(id, out float lp) && now - lp < 0.03f) return;
            lastPlay[id] = now;
            var s = sources[next];
            next = (next + 1) % sources.Count;
            s.pitch = pitch * Random.Range(0.94f, 1.06f);
            s.volume = vol * Volume;
            s.clip = clip;
            s.Play();
        }
    }
}
