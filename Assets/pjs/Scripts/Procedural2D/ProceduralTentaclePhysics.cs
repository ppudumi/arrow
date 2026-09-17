using System.Collections.Generic;
using UnityEngine;

namespace Procedural2D
{
    /// <summary>
    /// 테라리아 눈깔 괴물(Demon Eye)의 뒤편에서 공기 저항, 유영 파동(Undulation), 관성에 의해
    /// 흐물거리며 역동적으로 휘날리는 3가닥 동적 촉수(Dynamic Tentacle) 물리 시스템입니다.
    /// - 런타임 가비지 할당 제로 (Zero-GC Verlet Integration)
    /// - LineRenderer 폭 감쇠(Tapering) 및 살점 그라데이션 적용
    /// </summary>
    public class ProceduralTentaclePhysics : MonoBehaviour
    {
        [System.Serializable]
        public class TentacleChain
        {
            public Vector2 localAnchorOffset;
            public int segmentCount = 6;
            public float segmentLength = 0.22f;
            public float waveFrequency = 6f;
            public float waveAmplitude = 0.12f;
            public float phaseOffset;

            [HideInInspector] public Vector2[] positions;
            [HideInInspector] public Vector2[] prevPositions;
            [HideInInspector] public LineRenderer lineRenderer;
        }

        [Header("Tentacle Settings")]
        [SerializeField] private int tentacleCount = 3;
        [SerializeField] private float damping = 0.88f;
        [SerializeField] private float drag = 0.85f;
        [SerializeField] private Material tentacleMaterial;

        private TentacleChain[] tentacles;
        private Vector3[] lineBuffer;

        private void Awake()
        {
            InitTentacles();
        }

        private void InitTentacles()
        {
            tentacles = new TentacleChain[3];

            // 3가닥 촉수의 앵커 오프셋 (눈알 뒤쪽 소켓)
            Vector2[] offsets = new Vector2[]
            {
                new Vector2(-0.85f, 0.28f),  // 상단 촉수
                new Vector2(-0.95f, 0.0f),   // 중앙 촉수
                new Vector2(-0.85f, -0.28f)  // 하단 촉수
            };

            float[] freqs = new float[] { 5.5f, 7.0f, 6.2f };
            float[] phases = new float[] { 0f, 1.8f, 3.6f };
            int[] segments = new int[] { 6, 7, 6 };
            float[] lengths = new float[] { 0.22f, 0.26f, 0.22f };

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            Material mat = new Material(shader);

            GameObject root = new GameObject("Tentacles_Root");
            root.transform.SetParent(transform, false);

            for (int t = 0; t < 3; t++)
            {
                TentacleChain tc = new TentacleChain();
                tc.localAnchorOffset = offsets[t];
                tc.segmentCount = segments[t];
                tc.segmentLength = lengths[t];
                tc.waveFrequency = freqs[t];
                tc.waveAmplitude = 0.10f;
                tc.phaseOffset = phases[t];

                tc.positions = new Vector2[tc.segmentCount];
                tc.prevPositions = new Vector2[tc.segmentCount];

                GameObject tGo = new GameObject($"Tentacle_{t}");
                tGo.transform.SetParent(root.transform, false);

                LineRenderer lr = tGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = tc.segmentCount;
                lr.startWidth = 0.26f;
                lr.endWidth = 0.05f;

                // 폭 커브 설정 (뿌리는 두껍고 끝은 가늘게)
                AnimationCurve widthCurve = new AnimationCurve(
                    new Keyframe(0f, 0.28f),
                    new Keyframe(0.35f, 0.20f),
                    new Keyframe(0.75f, 0.12f),
                    new Keyframe(1f, 0.04f)
                );
                lr.widthCurve = widthCurve;

                // 살점 그라데이션 (검붉은 핏빛 -> 선홍색 끝자락)
                Gradient grad = new Gradient();
                grad.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(0.45f, 0.06f, 0.12f), 0f),
                        new GradientColorKey(new Color(0.75f, 0.12f, 0.20f), 0.5f),
                        new GradientColorKey(new Color(0.95f, 0.25f, 0.35f), 1f)
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );
                lr.colorGradient = grad;
                lr.sharedMaterial = mat;
                lr.sortingOrder = -1; // 눈알 본체 뒤에서 렌더링

                tc.lineRenderer = lr;

                // 초기 위치 설정
                Vector2 rootPos = transform.TransformPoint(tc.localAnchorOffset);
                Vector2 backward = -transform.right;
                for (int i = 0; i < tc.segmentCount; i++)
                {
                    tc.positions[i] = rootPos + backward * (i * tc.segmentLength);
                    tc.prevPositions[i] = tc.positions[i];
                }

                tentacles[t] = tc;
            }

            lineBuffer = new Vector3[10];
        }

        private void FixedUpdate()
        {
            if (tentacles == null) return;

            float dt = Time.fixedDeltaTime;
            float time = Time.time;

            for (int t = 0; t < tentacles.Length; t++)
            {
                TentacleChain tc = tentacles[t];
                if (tc.positions == null || tc.positions.Length == 0) continue;

                // 1. 뿌리 노드는 눈알 본체의 뒤편 앵커에 고정
                Vector2 anchorWorldPos = transform.TransformPoint(tc.localAnchorOffset);
                tc.positions[0] = anchorWorldPos;

                Vector2 backwardDir = -transform.right;
                Vector2 normalDir = new Vector2(-backwardDir.y, backwardDir.x);

                // 2. Verlet 적분 + 유영 사인파(Undulation) 적용
                for (int i = 1; i < tc.segmentCount; i++)
                {
                    Vector2 current = tc.positions[i];
                    Vector2 prev = tc.prevPositions[i];
                    Vector2 vel = (current - prev) * drag;

                    // 유영 웨이브 파동 힘 (물속이나 공기를 헤엄치는 테라리아 촉수 느낌)
                    float wave = Mathf.Sin(time * tc.waveFrequency + i * 0.8f + tc.phaseOffset);
                    Vector2 waveForce = normalDir * (wave * tc.waveAmplitude * (i / (float)tc.segmentCount));

                    tc.prevPositions[i] = current;
                    tc.positions[i] = current + vel + waveForce * dt;
                }

                // 3. 거리 제약 조건 (Relaxation Constraint - 4회 반복으로 팽팽함 유지)
                for (int iter = 0; iter < 4; iter++)
                {
                    tc.positions[0] = anchorWorldPos;
                    for (int i = 0; i < tc.segmentCount - 1; i++)
                    {
                        Vector2 delta = tc.positions[i + 1] - tc.positions[i];
                        float dist = delta.magnitude;
                        if (dist > 0.0001f)
                        {
                            float diff = (dist - tc.segmentLength) / dist;
                            if (i == 0)
                            {
                                tc.positions[i + 1] -= delta * diff;
                            }
                            else
                            {
                                tc.positions[i] += delta * (diff * 0.4f);
                                tc.positions[i + 1] -= delta * (diff * 0.6f);
                            }
                        }
                    }
                }

                // 4. LineRenderer 버퍼 업데이트
                if (tc.lineRenderer != null)
                {
                    if (lineBuffer.Length < tc.segmentCount) lineBuffer = new Vector3[tc.segmentCount];
                    for (int i = 0; i < tc.segmentCount; i++)
                    {
                        lineBuffer[i] = tc.positions[i];
                    }
                    tc.lineRenderer.SetPositions(lineBuffer);
                }
            }
        }
    }
}
