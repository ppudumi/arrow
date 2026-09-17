using UnityEngine;

namespace Procedural2D
{
    /// <summary>
    /// 5개의 관절(Skirt_1 ~ Skirt_5)로 구성된 묵직하고 안정적인 치마/드레스 물리 시뮬레이터입니다.
    /// 팔랑거림을 억제하고 묵직한 천(원단)의 무게감과 탄성을 구현합니다.
    /// </summary>
    public class ProceduralSkirtPhysics : MonoBehaviour
    {
        [Header("Skirt 5-Joint Bones (5단 관절 체인)")]
        public Transform joint1;
        public Transform joint2;
        public Transform joint3;
        public Transform joint4;
        public Transform joint5;

        [Header("Weighted Sway & Inertia (묵직한 무게감 & 관성)")]
        [Tooltip("이동 속도에 따른 치마 들림 강도 (낮출수록 묵직함)")]
        public float moveSwayFactor = 3.5f;
        [Tooltip("관절 단계별 각도 증폭 계수")]
        public float jointMultiplier = 1.18f;
        [Tooltip("최대 펄럭임 각도 제한 (과도하게 뒤집히는 것 방지)")]
        public float maxAngle = 32f;

        [Header("Heavy Fabric Wave (안정적인 원단 파동)")]
        [Tooltip("달릴 때 치마가 물결치는 주기 (낮을수록 천천히 묵직하게 움직임)")]
        public float waveFrequency = 8.0f;
        [Tooltip("달릴 때 치마가 물결치는 진폭 (작을수록 펄럭거림 억제)")]
        public float waveAmplitude = 4.5f;
        [Tooltip("관절 간 파동 시차")]
        public float phaseOffset = 0.35f;

        [Header("Spring Physics (탄성 및 감쇠력 강화)")]
        [Tooltip("원래 형태로 돌아오려는 복원력 (높을수록 짱짱함)")]
        public float springStiffness = 38f;
        [Tooltip("펄럭거림을 잡아주는 공기/원단 저항 (높을수록 빨리 안정됨)")]
        public float springDamping = 7.5f;

        [Header("Vertical Reaction (점프 & 낙하 기류)")]
        [Tooltip("점프/낙하 시 반응 강도 (과도한 부풀림 억제)")]
        public float verticalDrag = 1.4f;

        [Header("Idle Floating (대기 중 은은한 호흡)")]
        public float idleFloatSpeed = 2.0f;
        public float idleFloatAmount = 1.5f;

        private Rigidbody2D rb;
        private Procedural2DAim aim;
        private Vector3 prevPos;

        // 5개 관절의 실시간 각도 및 각속도
        private float a1, a2, a3, a4, a5;
        private float v1, v2, v3, v4, v5;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            aim = GetComponent<Procedural2DAim>();
            prevPos = transform.position;
        }

        private void LateUpdate()
        {
            if (joint1 == null) return;
            // 1. 월드 이동 속도 계산
            Vector2 currentVelocity;
            if (rb != null && rb.linearVelocity.sqrMagnitude > 0.01f)
            {
                currentVelocity = rb.linearVelocity;
            }
            else
            {
                float dt = Mathf.Max(Time.deltaTime, 0.001f);
                currentVelocity = (transform.position - prevPos) / dt;
            }
            prevPos = transform.position;

            // 2. 캐릭터 바라보는 방향 기준 상대 속도
            bool facingRight = aim != null ? aim.IsFacingRight : (transform.localScale.x >= 0);
            float forwardSpeed = facingRight ? currentVelocity.x : -currentVelocity.x;
            float verticalSpeed = currentVelocity.y;

            // 묵직한 기본 치마 들림 각도
            float baseAngle = -forwardSpeed * moveSwayFactor;
            baseAngle += -verticalSpeed * verticalDrag;

            float moveMag = Mathf.Abs(forwardSpeed);
            float t = Time.time;

            // 3. 5개 관절 목표 각도 연산
            float t1, t2, t3, t4, t5;
            if (moveMag > 0.2f)
            {
                float speedNorm = Mathf.Clamp01(moveMag / 4f);
                float w1 = Mathf.Sin(t * waveFrequency) * (waveAmplitude * speedNorm);
                float w2 = Mathf.Sin(t * waveFrequency - phaseOffset) * (waveAmplitude * speedNorm * jointMultiplier);
                float w3 = Mathf.Sin(t * waveFrequency - phaseOffset * 2f) * (waveAmplitude * speedNorm * Mathf.Pow(jointMultiplier, 2));
                float w4 = Mathf.Sin(t * waveFrequency - phaseOffset * 3f) * (waveAmplitude * speedNorm * Mathf.Pow(jointMultiplier, 3));
                float w5 = Mathf.Sin(t * waveFrequency - phaseOffset * 4f) * (waveAmplitude * speedNorm * Mathf.Pow(jointMultiplier, 4));

                t1 = baseAngle * 0.35f + w1;
                t2 = (baseAngle * 0.55f + w2) - t1 * 0.15f;
                t3 = (baseAngle * 0.75f + w3) - t2 * 0.15f;
                t4 = (baseAngle * 0.95f + w4) - t3 * 0.15f;
                t5 = (baseAngle * 1.15f + w5) - t4 * 0.15f;
            }
            else
            {
                // 서 있을 때 차분하고 은은한 숨결
                t1 = Mathf.Sin(t * idleFloatSpeed) * idleFloatAmount;
                t2 = Mathf.Sin(t * idleFloatSpeed - 0.25f) * (idleFloatAmount * 1.1f);
                t3 = Mathf.Sin(t * idleFloatSpeed - 0.5f) * (idleFloatAmount * 1.25f);
                t4 = Mathf.Sin(t * idleFloatSpeed - 0.75f) * (idleFloatAmount * 1.4f);
                t5 = Mathf.Sin(t * idleFloatSpeed - 1.0f) * (idleFloatAmount * 1.55f);
            }

            t1 = Mathf.Clamp(t1, -maxAngle * 0.45f, maxAngle * 0.45f);
            t2 = Mathf.Clamp(t2, -maxAngle * 0.6f, maxAngle * 0.6f);
            t3 = Mathf.Clamp(t3, -maxAngle * 0.75f, maxAngle * 0.75f);
            t4 = Mathf.Clamp(t4, -maxAngle * 0.88f, maxAngle * 0.88f);
            t5 = Mathf.Clamp(t5, -maxAngle, maxAngle);

            // 4. 높은 댐핑의 스프링 물리 연산 (팔랑거림 완벽 차단)
            SimulateJoint(t1, ref a1, ref v1, 1.0f);
            SimulateJoint(t2, ref a2, ref v2, 0.92f);
            SimulateJoint(t3, ref a3, ref v3, 0.85f);
            SimulateJoint(t4, ref a4, ref v4, 0.78f);
            SimulateJoint(t5, ref a5, ref v5, 0.72f);

            // 5. 로컬 회전 적용
            if (joint1 != null) joint1.localRotation = Quaternion.Euler(0f, 0f, a1);
            if (joint2 != null) joint2.localRotation = Quaternion.Euler(0f, 0f, a2);
            if (joint3 != null) joint3.localRotation = Quaternion.Euler(0f, 0f, a3);
            if (joint4 != null) joint4.localRotation = Quaternion.Euler(0f, 0f, a4);
            if (joint5 != null) joint5.localRotation = Quaternion.Euler(0f, 0f, a5);
        }

        private void SimulateJoint(float target, ref float currentAngle, ref float currentVel, float stiffnessScale)
        {
            float force = (target - currentAngle) * (springStiffness * stiffnessScale) - currentVel * springDamping;
            currentVel += force * Time.deltaTime;
            currentAngle += currentVel * Time.deltaTime;
        }
    }
}
