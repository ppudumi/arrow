using UnityEngine;

namespace Procedural2D
{
    /// <summary>
    /// 화살 충돌 시 발생하는 스파크 및 파편 파티클을 단일 고성능 World ParticleSystem으로 관리합니다.
    /// - 100% Zero-Allocation (GC 부하 없음)
    /// - 파티클이 공중에 영구히 남는 현상 완벽 방지 (수명 0.16~0.28초 후 자동 소멸)
    /// - 단일 드로우콜로 최적화 극대화
    /// </summary>
    public class ImpactParticleManager : MonoBehaviour
    {
        public static ImpactParticleManager Instance { get; private set; }

        private ParticleSystem ps;
        private ParticleSystem.EmitParams emitParams;
        private Material particleMaterial;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            CleanOldChildren();
            InitParticleSystem();
        }

        private void CleanOldChildren()
        {
            // 기존 씬이나 이전 실행 시 생성된 잔존 파티클 오브젝트들 완전 제거
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying) DestroyImmediate(child.gameObject);
                    else Destroy(child.gameObject);
#else
                    Destroy(child.gameObject);
#endif
                }
            }
        }

        private void InitParticleSystem()
        {
            InitMaterial();

            GameObject psGo = new GameObject("Unified_Impact_ParticleSystem");
            psGo.transform.SetParent(transform, false);

            ps = psGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystemRenderer psr = psGo.GetComponent<ParticleSystemRenderer>();

            // ParticleSystem 메인 설정
            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 1.0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.0f, 8.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            main.stopAction = ParticleSystemStopAction.None;

            // 지속 방출 비활성화 (Emit 호출 시에만 방출)
            var emission = ps.emission;
            emission.enabled = false;

            // Shape 설정
            var shape = ps.shape;
            shape.enabled = false; // 위치/속도를 EmitParams로 직접 정밀 제어

            // 크기 감소 (Size Over Lifetime: 수명 끝에서 0으로 완전 축소)
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.6f, 0.7f),
                new Keyframe(1f, 0f)
            );
            sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // 색상 페이드 (Color Over Lifetime: 수명 끝에서 완전 투명 페이드아웃)
            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = grad;

            // 렌더러 설정
            if (particleMaterial != null) psr.sharedMaterial = particleMaterial;
            psr.sortingOrder = 15;

            // 상시 시뮬레이션 가동 (파티클 수명 감쇠 및 물리 이동 보장)
            ps.Clear();
            ps.Play();

            emitParams = new ParticleSystem.EmitParams();
        }

        private void InitMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            particleMaterial = new Material(shader);
            Texture2D tex = LoadSparkTexture();
            if (tex != null)
            {
                particleMaterial.mainTexture = tex;
            }
        }

        private Texture2D LoadSparkTexture()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Environment/Particle_Spark.png");
#else
            return null;
#endif
        }

        /// <summary>
        /// 화살 충돌 지점에 고성능 스파크 파티클을 방출합니다.
        /// </summary>
        public void PlayImpact(Vector2 position, Vector2 normal, bool isEnemy)
        {
            if (ps == null) return;
            if (!ps.isPlaying) ps.Play();

            float baseAngle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            int count = isEnemy ? 10 : 6; // 파티클 개수 최적화 (가시성 유지 + 부하 최소화)

            for (int i = 0; i < count; i++)
            {
                emitParams.ResetPosition();
                emitParams.position = position;

                // 충돌 법선 반사각 기준 원뿔형 분산
                float spread = Random.Range(-45f, 45f);
                float rad = (baseAngle + spread) * Mathf.Deg2Rad;
                float speed = isEnemy ? Random.Range(4.0f, 8.5f) : Random.Range(3.0f, 6.5f);
                emitParams.velocity = new Vector3(Mathf.Cos(rad) * speed, Mathf.Sin(rad) * speed, 0f);

                // 명중 대상별 색상
                if (isEnemy)
                {
                    float r = Random.value;
                    if (r < 0.5f) emitParams.startColor = new Color(1f, 0.25f, 0.3f, 1f); // 크림슨 레드
                    else if (r < 0.85f) emitParams.startColor = new Color(1f, 0.7f, 0.15f, 1f); // 불꽃 오렌지
                    else emitParams.startColor = new Color(1f, 0.95f, 0.4f, 1f); // 네온 골드
                }
                else
                {
                    float r = Random.value;
                    if (r < 0.55f) emitParams.startColor = new Color(1f, 0.9f, 0.45f, 1f); // 황금 스파크
                    else if (r < 0.85f) emitParams.startColor = new Color(1f, 1f, 1f, 1f); // 화이트 스파크
                    else emitParams.startColor = new Color(0.7f, 0.75f, 0.8f, 0.9f); // 석조 파편
                }

                emitParams.startSize = Random.Range(0.12f, 0.22f);
                emitParams.startLifetime = Random.Range(0.16f, 0.28f); // 0.16~0.28초 후 완전 소멸

                ps.Emit(emitParams, 1);
            }
        }

        /// <summary>
        /// 잔존 파티클 즉시 강제 소멸
        /// </summary>
        public void ClearAllParticles()
        {
            if (ps != null) ps.Clear();
        }
    }
}
