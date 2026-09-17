using System.Collections.Generic;
using UnityEngine;

namespace Procedural2D
{
    /// <summary>
    /// 플레이어의 허리에 매여 바람, 이동 속도, 2단 점프 및 초고속 대시에 맞춰 역동적으로 펄럭이는 절차적 허리 띠(Waist Ribbon / Sash) 물리 시뮬레이터입니다.
    /// 베를레 적분(Verlet Integration) 기반의 로프/천 물리와 듀얼 스트리머(두 갈래 띠)를 통해 고급스러운 비주얼을 제공합니다.
    /// </summary>
    [RequireComponent(typeof(ProceduralCharacterController))]
    public class ProceduralWaistRibbon : MonoBehaviour
    {
        [Header("Waist Attachment (허리 연결점)")]
        [Tooltip("허리 띠가 시작되는 앵커 트랜스폼 (미지정 시 Visual/Body 또는 허리 높이에 자동 생성)")]
        public Transform waistAnchor;
        [Tooltip("허리 앵커 로컬 위치 오프셋")]
        public Vector3 anchorLocalOffset = new Vector3(-0.05f, -0.06f, 0f);

        [Header("Ribbon Appearance (띠 비주얼 & 컬러)")]
        [Tooltip("허리 매듭 쪽 시작 색상")]
        public Color ribbonColorStart = new Color(1f, 0.22f, 0.38f, 1f); // 비비드 크림슨/루비
        [Tooltip("띠 끝자락(꼬리) 쪽 색상")]
        public Color ribbonColorEnd = new Color(1f, 0.55f, 0.25f, 0.9f);   // 화사한 골든 오렌지 페이드
        [Tooltip("보조 띠 색상 틴트 (두 번째 갈래)")]
        public Color secondaryColor = new Color(0.85f, 0.15f, 0.3f, 0.95f);

        [Tooltip("띠 시작 두께 (허리 매듭)")]
        public float startWidth = 0.22f;
        [Tooltip("띠 끝자락 두께 (테이퍼링)")]
        public float endWidth = 0.08f;
        [Tooltip("스프라이트 소팅 오더")]
        public int sortingOrder = 3;

        [Header("Ribbon 1 (메인 긴 띠)")]
        [Tooltip("마디(세그먼트) 개수")]
        [Range(8, 24)]
        public int ribbon1Segments = 16;
        [Tooltip("전체 띠 길이")]
        public float ribbon1TotalLength = 2.2f;

        [Header("Ribbon 2 (보조 갈래 띠)")]
        [Tooltip("마디(세그먼트) 개수")]
        [Range(6, 20)]
        public int ribbon2Segments = 12;
        [Tooltip("전체 띠 길이")]
        public float ribbon2TotalLength = 1.6f;

        [Header("Physics Simulation (물리 & 공기역학)")]
        [Tooltip("중력 가속도 (띠가 아래로 처지는 힘)")]
        public float gravity = 8.5f;
        [Tooltip("이동 시 반대 방향으로 띠가 흩날리는 공기 저항 계수")]
        public float velocityDrag = 0.55f;
        [Tooltip("대시 시 띠가 팽팽하게 수평으로 날아가는 강도")]
        public float dashDrag = 1.2f;
        [Tooltip("바람 펄럭임 주기(속도)")]
        public float waveSpeed = 9.0f;
        [Tooltip("바람 펄럭임 진폭(크기)")]
        public float waveAmplitude = 0.18f;
        [Tooltip("베를레 물리 감쇠력 (관성 억제 및 안정화)")]
        [Range(0.85f, 0.99f)]
        public float damping = 0.92f;
        [Tooltip("제약 조건 반복 해결 횟수 (높을수록 늘어남 없음)")]
        [Range(4, 16)]
        public int solverIterations = 8;

        // 물리 시뮬레이션용 세그먼트 데이터 구조체
        private struct RibbonPoint
        {
            public Vector2 currentPos;
            public Vector2 prevPos;

            public RibbonPoint(Vector2 pos)
            {
                currentPos = pos;
                prevPos = pos;
            }
        }

        private RibbonPoint[] points1;
        private RibbonPoint[] points2;
        private LineRenderer line1;
        private LineRenderer line2;

        private Rigidbody2D rb;
        private ProceduralCharacterController charCtrl;
        private Procedural2DAim aim;
        private Vector3 prevPlayerPos;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            charCtrl = GetComponent<ProceduralCharacterController>();
            aim = GetComponent<Procedural2DAim>();
            prevPlayerPos = transform.position;

            EnsureAnchor();
            InitializeRibbons();
        }

        private void EnsureAnchor()
        {
            if (waistAnchor == null)
            {
                Transform body = transform.Find("Visual/Body");
                if (body != null)
                {
                    GameObject anchorGo = new GameObject("Waist_Ribbon_Anchor");
                    anchorGo.transform.SetParent(body);
                    anchorGo.transform.localPosition = anchorLocalOffset;
                    waistAnchor = anchorGo.transform;
                }
                else
                {
                    Transform visual = transform.Find("Visual");
                    GameObject anchorGo = new GameObject("Waist_Ribbon_Anchor");
                    anchorGo.transform.SetParent(visual != null ? visual : transform);
                    anchorGo.transform.localPosition = new Vector3(0f, -0.06f, 0f);
                    waistAnchor = anchorGo.transform;
                }
            }
        }

        private void InitializeRibbons()
        {
            Vector2 startPos = waistAnchor != null ? (Vector2)waistAnchor.position : (Vector2)transform.position;

            // 1. 메인 띠 라인렌더러 생성
            points1 = new RibbonPoint[ribbon1Segments];
            float segLen1 = ribbon1TotalLength / (ribbon1Segments - 1);
            for (int i = 0; i < ribbon1Segments; i++)
            {
                points1[i] = new RibbonPoint(startPos - new Vector2(0f, i * segLen1));
            }
            line1 = CreateLineRenderer("Waist_Ribbon_Main", ribbonColorStart, ribbonColorEnd, startWidth, endWidth, sortingOrder);

            // 2. 보조 띠 라인렌더러 생성
            points2 = new RibbonPoint[ribbon2Segments];
            float segLen2 = ribbon2TotalLength / (ribbon2Segments - 1);
            for (int i = 0; i < ribbon2Segments; i++)
            {
                points2[i] = new RibbonPoint(startPos - new Vector2(0.05f, i * segLen2));
            }
            line2 = CreateLineRenderer("Waist_Ribbon_Sub", secondaryColor, ribbonColorEnd, startWidth * 0.85f, endWidth * 0.8f, sortingOrder - 1);
        }

        private LineRenderer CreateLineRenderer(string name, Color colStart, Color colEnd, float wStart, float wEnd, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.numCapVertices = 5;
            lr.numCornerVertices = 5;
            lr.sortingOrder = order;

            // URP 2D 지원 셰이더 매핑
            Shader s = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            if (s == null) s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (s == null) s = Shader.Find("Sprites/Default");

            Material mat = new Material(s);
            mat.color = Color.white;
            lr.material = mat;

            // 너비 커브 (허리 매듭에서 꼬리로 갈수록 자연스럽게 가늘어짐)
            AnimationCurve widthCurve = new AnimationCurve();
            widthCurve.AddKey(0f, wStart);
            widthCurve.AddKey(0.7f, Mathf.Lerp(wStart, wEnd, 0.6f));
            widthCurve.AddKey(1f, wEnd);
            lr.widthCurve = widthCurve;

            // 색상 그라디언트
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(colStart, 0f), new GradientColorKey(colEnd, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(colStart.a, 0f), new GradientAlphaKey(colEnd.a, 1f) }
            );
            lr.colorGradient = grad;

            return lr;
        }

        private void LateUpdate()
        {
            if (waistAnchor == null) EnsureAnchor();
            if (waistAnchor == null) return;

            float dt = Mathf.Min(Time.deltaTime, 0.033f);
            if (dt <= 0.0001f) return;

            // 플레이어 속도 및 방향 계산
            Vector2 playerVel = Vector2.zero;
            if (rb != null)
            {
                playerVel = rb.linearVelocity;
            }
            else
            {
                playerVel = (Vector2)(transform.position - prevPlayerPos) / dt;
            }
            prevPlayerPos = transform.position;

            bool isDashing = charCtrl != null && charCtrl.IsDashing;
            float currentDrag = isDashing ? dashDrag : velocityDrag;

            // 바라보는 방향 (플립)
            float facingDir = 1f;
            if (aim != null) facingDir = aim.IsFacingRight ? 1f : -1f;
            else if (transform.localScale.x < 0) facingDir = -1f;

            Vector2 anchorPos = waistAnchor.position;

            // 1. 메인 띠 시뮬레이션
            SimulateRibbon(points1, ribbon1TotalLength, anchorPos, playerVel, currentDrag, facingDir, 0f, dt);
            RenderRibbon(line1, points1);

            // 2. 보조 띠 시뮬레이션 (위상차와 미세한 각도 분기)
            Vector2 subAnchorPos = anchorPos + new Vector2(-facingDir * 0.06f, 0.02f);
            SimulateRibbon(points2, ribbon2TotalLength, subAnchorPos, playerVel, currentDrag, facingDir, 0.75f, dt);
            RenderRibbon(line2, points2);
        }

        private void SimulateRibbon(RibbonPoint[] pts, float totalLen, Vector2 rootPos, Vector2 playerVel, float drag, float facing, float phaseOffset, float dt)
        {
            int count = pts.Length;
            float segLen = totalLen / (count - 1);

            // 1단계: 베를레 이동 및 외력(중력, 공기저항, 바람 파동) 적용
            float time = Time.time * waveSpeed + phaseOffset;

            for (int i = 1; i < count; i++)
            {
                Vector2 pos = pts[i].currentPos;
                Vector2 prev = pts[i].prevPos;
                Vector2 velocity = (pos - prev) * damping;

                // 중력
                Vector2 force = new Vector2(0f, -gravity);

                // 이동 속도에 의한 관성 드래그 (반대 방향으로 흩날림)
                force += -playerVel * (drag * (1f + (float)i / count));

                // 걷기/대기 시 은은한 사인파 펄럭임
                float wave = Mathf.Sin(time + i * 0.45f) * waveAmplitude;
                float waveY = Mathf.Cos(time * 0.85f + i * 0.35f) * (waveAmplitude * 0.6f);
                force += new Vector2(-facing * wave * 5f, waveY * 5f);

                pts[i].prevPos = pos;
                pts[i].currentPos = pos + velocity + force * (dt * dt);
            }

            // 2단계: 앵커 고정
            pts[0].currentPos = rootPos;
            pts[0].prevPos = rootPos;

            // 3단계: 거리 제약 조건(Distance Constraints) 반복 해결 - 로프 비신축성 보장
            for (int iter = 0; iter < solverIterations; iter++)
            {
                pts[0].currentPos = rootPos;

                for (int i = 0; i < count - 1; i++)
                {
                    Vector2 delta = pts[i + 1].currentPos - pts[i].currentPos;
                    float dist = delta.magnitude;
                    if (dist > 0.0001f)
                    {
                        float diff = (dist - segLen) / dist;
                        if (i == 0)
                        {
                            // 앵커와 연결된 첫 마디는 뒤쪽 점만 전적으로 이동
                            pts[i + 1].currentPos -= delta * diff;
                        }
                        else
                        {
                            pts[i].currentPos += delta * (diff * 0.5f);
                            pts[i + 1].currentPos -= delta * (diff * 0.5f);
                        }
                    }
                }
            }
        }

        private void RenderRibbon(LineRenderer lr, RibbonPoint[] pts)
        {
            if (lr == null) return;
            lr.positionCount = pts.Length;
            for (int i = 0; i < pts.Length; i++)
            {
                lr.SetPosition(i, pts[i].currentPos);
            }
        }
    }
}
