using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 다크 판타지 / 블라스퍼머스 스타일 효과음 시스템.
    /// - Assets/Resources/Sfx/{id}.wav (또는 .ogg/.mp3) 파일이 있으면 우선 로드하여 재생한다.
    /// - 외부 음원이 없는 항목은 8비트 레트로 비프음 대신 44.1kHz 필터링 노이즈·금속성 배음·서브베이스·잔향으로 합성해 사용한다.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx I { get; private set; }
        public static float Volume = 0.65f;

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly List<AudioSource> sources = new List<AudioSource>();
        private int next;
        private readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();

        private void Awake()
        {
            I = this;
            for (int i = 0; i < 16; i++)
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
            const int sr = 44100;

            // 활시위 + 바람 가르는 화살 (탄성 있는 저역 튕김 + 고역 공기 마찰)
            clips["shoot"] = SynthFiltered(sr, 0.16f, (t, n) =>
            {
                float snap = Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(210f, 85f, t / 0.16f)) * Mathf.Exp(-t * 28f) * 0.55f;
                float air = n * Mathf.Exp(-t * 22f) * Mathf.Clamp01(t * 140f) * 0.45f;
                return snap + air;
            }, lowpassHz: 3200f);

            // 묵직한 타격 (살/갑옷 충격음 + 저역 펀치 + 짧은 금속/뼈 크랙)
            clips["hit"] = SynthFiltered(sr, 0.14f, (t, n) =>
            {
                float thud = Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(145f, 48f, t / 0.14f)) * Mathf.Exp(-t * 32f) * 0.75f;
                float crunch = n * Mathf.Exp(-t * 42f) * 0.65f;
                float metal = Mathf.Sin(t * 2f * Mathf.PI * 620f + Mathf.Sin(t * 2f * Mathf.PI * 940f) * 1.4f) * Mathf.Exp(-t * 45f) * 0.22f;
                return thud + crunch + metal;
            }, lowpassHz: 2600f);

            // 돌/벽 적중 (단단한 석재 충격 + 짧은 잔향)
            clips["wall"] = SynthFiltered(sr, 0.11f, (t, n) =>
            {
                float stone = Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(240f, 95f, t / 0.11f)) * Mathf.Exp(-t * 38f) * 0.45f;
                float grit = n * Mathf.Exp(-t * 48f) * 0.5f;
                return stone + grit;
            }, lowpassHz: 2200f);

            // 화살 회수 (부드러운 가죽/금속 스침)
            clips["pick"] = SynthFiltered(sr, 0.11f, (t, n) =>
            {
                float brush = n * Mathf.Exp(-t * 32f) * Mathf.Clamp01(t * 90f) * 0.4f;
                float chime = Mathf.Sin(t * 2f * Mathf.PI * 520f) * Mathf.Exp(-t * 36f) * 0.25f;
                return brush + chime;
            }, lowpassHz: 2400f);

            // 회수 파동 (성스러운 쇠/종 울림 + 바람 훑기)
            clips["recall"] = SynthFiltered(sr, 0.42f, (t, n) =>
            {
                float bell = (Mathf.Sin(t * 2f * Mathf.PI * 220f) * 0.4f
                            + Mathf.Sin(t * 2f * Mathf.PI * 330f) * 0.25f
                            + Mathf.Sin(t * 2f * Mathf.PI * 528f) * 0.15f) * Mathf.Exp(-t * 6f);
                float wind = n * Mathf.Sin(Mathf.Clamp01(t / 0.42f) * Mathf.PI) * 0.25f;
                return bell + wind;
            }, lowpassHz: 2000f);

            // 피격 (묵직한 둔탁함 + 어두운 공명)
            clips["hurt"] = SynthFiltered(sr, 0.26f, (t, n) =>
            {
                float impact = Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(130f, 42f, t / 0.26f)) * Mathf.Exp(-t * 16f) * 0.8f;
                float tear = n * Mathf.Exp(-t * 22f) * 0.55f;
                return impact + tear;
            }, lowpassHz: 1800f);

            // 대형 폭발 / 내려찍기 (깊은 서브베이스 + 묵직한 파열음)
            clips["boom"] = SynthFiltered(sr, 0.55f, (t, n) =>
            {
                float sub = Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(78f, 28f, t / 0.55f)) * Mathf.Exp(-t * 6.5f) * 0.85f;
                float rumble = n * Mathf.Exp(-t * 7.5f) * 0.7f;
                return sub + rumble;
            }, lowpassHz: 1400f);

            // 번개 / 스태틱 (고전압 파지직 + 저역 스파크)
            clips["zap"] = SynthFiltered(sr, 0.24f, (t, n) =>
            {
                float crackle = n * (Mathf.Abs(Mathf.Sin(t * 190f)) > 0.4f ? 0.85f : 0.2f) * Mathf.Exp(-t * 12f);
                float hum = Mathf.Sin(t * 2f * Mathf.PI * 110f) * Mathf.Exp(-t * 14f) * 0.3f;
                return crackle + hum;
            }, lowpassHz: 3800f);

            // UI 클릭 (무거운 석판/철제 딸깍)
            clips["ui"] = SynthFiltered(sr, 0.06f, (t, n) =>
            {
                float click = Mathf.Sin(t * 2f * Mathf.PI * 380f) * Mathf.Exp(-t * 75f) * 0.35f;
                float stone = n * Mathf.Exp(-t * 90f) * 0.25f;
                return click + stone;
            }, lowpassHz: 1800f);

            // 코인/보상 (어두운 금화 금속성 울림)
            clips["coin"] = SynthFiltered(sr, 0.22f, (t, n) =>
            {
                float m1 = Mathf.Sin(t * 2f * Mathf.PI * 660f + Mathf.Sin(t * 2f * Mathf.PI * 990f) * 0.8f) * Mathf.Exp(-t * 14f) * 0.35f;
                float m2 = Mathf.Sin(t * 2f * Mathf.PI * 880f) * Mathf.Exp(-Mathf.Max(0f, t - 0.04f) * 12f) * (t > 0.04f ? 0.3f : 0f);
                return m1 + m2;
            }, lowpassHz: 3200f);

            // 보스 포효 (낮은 괴수 울림 + 거친 숨결)
            clips["roar"] = SynthFiltered(sr, 0.85f, (t, n) =>
            {
                float growl = Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(75f, 44f, t / 0.85f) + Mathf.Sin(t * 2f * Mathf.PI * 37f) * 1.8f) * 0.6f;
                float breath = n * 0.45f;
                float env = Mathf.Clamp01(t * 12f) * Mathf.Exp(-Mathf.Max(0f, t - 0.15f) * 2.8f);
                return (growl + breath) * env;
            }, lowpassHz: 1200f);

            // 휘두르기 (무거운 대검/망치 가르기)
            clips["swing"] = SynthFiltered(sr, 0.22f, (t, n) =>
            {
                float whoosh = n * Mathf.Sin(t / 0.22f * Mathf.PI) * 0.65f;
                float low = Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(160f, 70f, t / 0.22f)) * Mathf.Sin(t / 0.22f * Mathf.PI) * 0.3f;
                return whoosh + low;
            }, lowpassHz: 1900f);

            // 빈 화살통 (무딘 나무/가죽 틱)
            clips["empty"] = SynthFiltered(sr, 0.06f, (t, n) =>
                (Mathf.Sin(t * 2f * Mathf.PI * 140f) * 0.3f + n * 0.2f) * Mathf.Exp(-t * 65f), lowpassHz: 1200f);

            // 구매 / 성당 종소리
            clips["buy"] = SynthFiltered(sr, 0.55f, (t, n) =>
            {
                float bell = (Mathf.Sin(t * 2f * Mathf.PI * 293.66f) * 0.45f
                            + Mathf.Sin(t * 2f * Mathf.PI * 440f) * 0.28f
                            + Mathf.Sin(t * 2f * Mathf.PI * 587.33f) * 0.16f) * Mathf.Exp(-t * 4.5f);
                return bell;
            }, lowpassHz: 2400f);

            // 발소리 (돌바닥 위 부츠 소리)
            clips["step"] = SynthFiltered(sr, 0.07f, (t, n) =>
                (Mathf.Sin(t * 2f * Mathf.PI * 110f) * 0.35f + n * 0.45f) * Mathf.Exp(-t * 52f) * Mathf.Clamp01(t * 500f), lowpassHz: 1400f);
            clips["step2"] = SynthFiltered(sr, 0.065f, (t, n) =>
                (Mathf.Sin(t * 2f * Mathf.PI * 98f) * 0.32f + n * 0.42f) * Mathf.Exp(-t * 58f) * Mathf.Clamp01(t * 500f), lowpassHz: 1300f);

            // 적 원거리 공격 (어두운 마법/투사체 발사)
            clips["enemyshot"] = SynthFiltered(sr, 0.16f, (t, n) =>
                (Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(260f, 90f, t / 0.16f)) * 0.45f + n * 0.35f) * Mathf.Exp(-t * 18f), lowpassHz: 2000f);

            // Assets/Resources/Sfx/{id} 에 실제 음원 파일이 있으면 우선 적용
            var keys = new List<string>(clips.Keys);
            foreach (var key in keys)
            {
                var ext = Resources.Load<AudioClip>("Sfx/" + key);
                if (ext != null) clips[key] = ext;
            }
        }

        private static readonly System.Random noiseRng = new System.Random(17);
        private static float Noise() => (float)(noiseRng.NextDouble() * 2.0 - 1.0);

        private static AudioClip SynthFiltered(int sr, float len, System.Func<float, float, float> f, float lowpassHz = 2400f)
        {
            int n = Mathf.CeilToInt(sr * len);
            var data = new float[n];
            float rc = 1f / (2f * Mathf.PI * Mathf.Max(200f, lowpassHz));
            float dt = 1f / sr;
            float alpha = dt / (rc + dt);
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float raw = f(i * dt, Noise());
                prev += alpha * (raw - prev);
                data[i] = Mathf.Clamp(prev, -1f, 1f);
            }
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
