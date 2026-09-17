using UnityEngine;

namespace Procedural2D
{
    /// <summary>
    /// Nujabes - Luv (sic) Instrumental BGM 재생 및 화살 사격, 벽 충돌, 적 타격 사운드 효과(SFX), 
    /// 탄창 빈 탄피 클릭 및 회수(Recall) 사운드를 총괄 관리하는 오디오 매니저입니다.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        private static AudioManager instance;
        public static AudioManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<AudioManager>();
                    if (instance == null)
                    {
                        GameObject go = new GameObject("Audio_Manager");
                        instance = go.AddComponent<AudioManager>();
                    }
                }
                return instance;
            }
        }

        [Header("BGM Settings (Nujabes - Luv(sic) Instrumental)")]
        [Tooltip("배경음악 재생 여부")]
        public bool playBgm = true;
        public AudioClip bgmClip;
        [Range(0f, 1f)]
        public float bgmVolume = 0.45f;

        [Header("Arrow Sound Effects (화살 기본 효과음)")]
        public AudioClip arrowShootClip;
        public AudioClip arrowWallHitClip;
        public AudioClip arrowEnemyHitClip;

        [Header("Arrow Magazine & Recall SFX (탄창 및 회수 효과음)")]
        public AudioClip emptyClickClip;
        public AudioClip arrowRecallClip;
        public AudioClip arrowCatchClip;

        [Header("SFX Volumes")]
        [Range(0f, 1f)]
        public float shootVolume = 0.75f;
        [Range(0f, 1f)]
        public float hitWallVolume = 0.85f;
        [Range(0f, 1f)]
        public float hitEnemyVolume = 0.95f;
        [Range(0f, 1f)]
        public float emptyClickVolume = 0.7f;
        [Range(0f, 1f)]
        public float recallVolume = 0.85f;
        [Range(0f, 1f)]
        public float catchVolume = 0.85f;

        private AudioSource bgmSource;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            else if (instance != this)
            {
                Destroy(gameObject);
                return;
            }

            SetupAudio();
        }

        private void SetupAudio()
        {
            LoadClipsIfMissing();

            if (bgmSource == null)
            {
                bgmSource = GetComponent<AudioSource>();
                if (bgmSource == null) bgmSource = gameObject.AddComponent<AudioSource>();
            }

            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmSource.volume = bgmVolume;

            if (playBgm && bgmClip != null)
            {
                bgmSource.clip = bgmClip;
                if (!bgmSource.isPlaying) bgmSource.Play();
            }
        }

        public void LoadClipsIfMissing()
        {
            // Nujabes - Luv(sic) Instrumental 로드
            if (bgmClip == null)
            {
                bgmClip = Resources.Load<AudioClip>("Audio/BGM/Nujabes_Luv_sic_Instrumental");
            }
            if (arrowShootClip == null) arrowShootClip = Resources.Load<AudioClip>("Audio/SFX/Arrow_Shoot");
            if (arrowWallHitClip == null) arrowWallHitClip = Resources.Load<AudioClip>("Audio/SFX/Arrow_Wall_Hit");
            if (arrowEnemyHitClip == null) arrowEnemyHitClip = Resources.Load<AudioClip>("Audio/SFX/Arrow_Enemy_Hit");
            if (emptyClickClip == null) emptyClickClip = Resources.Load<AudioClip>("Audio/SFX/Empty_Click");
            if (arrowRecallClip == null) arrowRecallClip = Resources.Load<AudioClip>("Audio/SFX/Arrow_Recall");
            if (arrowCatchClip == null) arrowCatchClip = Resources.Load<AudioClip>("Audio/SFX/Arrow_Catch");
        }

        public void PlayBgm()
        {
            LoadClipsIfMissing();
            if (playBgm && bgmClip != null && bgmSource != null)
            {
                if (bgmSource.clip != bgmClip || !bgmSource.isPlaying)
                {
                    bgmSource.clip = bgmClip;
                    bgmSource.volume = bgmVolume;
                    bgmSource.Play();
                }
            }
        }

        public void StopBgm()
        {
            if (bgmSource != null)
            {
                bgmSource.Stop();
            }
        }

        public void PlayArrowShoot(Vector3 pos)
        {
            AudioClip clip = arrowShootClip != null ? arrowShootClip : Resources.Load<AudioClip>("Audio/SFX/Arrow_Shoot");
            if (clip != null)
            {
                PlayClipWithPitch(clip, pos, shootVolume, 0.95f, 1.05f);
            }
        }

        public void PlayArrowWallHit(Vector3 pos)
        {
            AudioClip clip = arrowWallHitClip != null ? arrowWallHitClip : Resources.Load<AudioClip>("Audio/SFX/Arrow_Wall_Hit");
            if (clip != null)
            {
                PlayClipWithPitch(clip, pos, hitWallVolume, 0.92f, 1.08f);
            }
        }

        public void PlayArrowEnemyHit(Vector3 pos)
        {
            AudioClip clip = arrowEnemyHitClip != null ? arrowEnemyHitClip : Resources.Load<AudioClip>("Audio/SFX/Arrow_Enemy_Hit");
            if (clip != null)
            {
                PlayClipWithPitch(clip, pos, hitEnemyVolume, 0.96f, 1.04f);
            }
        }

        public void PlayEmptyClick(Vector3 pos)
        {
            AudioClip clip = emptyClickClip != null ? emptyClickClip : Resources.Load<AudioClip>("Audio/SFX/Empty_Click");
            if (clip != null)
            {
                PlayClipWithPitch(clip, pos, emptyClickVolume, 0.95f, 1.05f);
            }
        }

        public void PlayArrowRecall(Vector3 pos)
        {
            AudioClip clip = arrowRecallClip != null ? arrowRecallClip : Resources.Load<AudioClip>("Audio/SFX/Arrow_Recall");
            if (clip != null)
            {
                PlayClipWithPitch(clip, pos, recallVolume, 0.95f, 1.05f);
            }
        }

        public void PlayArrowCatch(Vector3 pos)
        {
            AudioClip clip = arrowCatchClip != null ? arrowCatchClip : Resources.Load<AudioClip>("Audio/SFX/Arrow_Catch");
            if (clip != null)
            {
                PlayClipWithPitch(clip, pos, catchVolume, 0.95f, 1.08f);
            }
        }

        private void PlayClipWithPitch(AudioClip clip, Vector3 pos, float volume, float minPitch, float maxPitch)
        {
            GameObject tempGo = new GameObject("TempAudio_" + clip.name);
            tempGo.transform.position = pos;
            AudioSource aSrc = tempGo.AddComponent<AudioSource>();
            aSrc.clip = clip;
            aSrc.volume = volume;
            aSrc.pitch = Random.Range(minPitch, maxPitch);
            aSrc.spatialBlend = 0f;
            aSrc.Play();
            Destroy(tempGo, clip.length + 0.15f);
        }
    }
}
