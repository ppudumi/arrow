using UnityEngine;

namespace Procedural2D
{
    /// <summary>
    /// 플레이어 이동 시 머리 높이 제어(Run Head Dip), 상체/머리 안정화,
    /// 조준 각도에 따른 상체 기울기 및 사격 반동을 제어합니다.
    /// 달릴 때 위아래/좌우 흔들림 없이 머리가 4픽셀 가량 자연스럽게 낮아지고,
    /// 멈추면 원래 높이로 즉시 복귀합니다.
    /// </summary>
    public class ProceduralBodyShift : MonoBehaviour
    {
        [Header("Body & Head Transforms")]
        public Transform bodyTransform;
        public Transform headTransform;

        [Header("Fine-Tuning Offsets (부위별 미세 기본 오프셋)")]
        [Tooltip("머리 기본 추가 위치 오프셋")]
        public Vector3 headOffset = new Vector3(0f, 0.15f, 0f);
        [Tooltip("상체 기본 추가 위치 오프셋")]
        public Vector3 bodyOffset = Vector3.zero;

        [Header("Run Posture & Head Dip (달리기 시 자세 및 머리 높이)")]
        [Tooltip("뛸 때(이동 시) 머리가 아래로 낮아지는 높이 (0.12f = 약 4픽셀 하강)")]
        public float runHeadDipAmount = 0.12f;
        [Tooltip("머리 높이 전환 추적 속도")]
        public float headDipSpeed = 14f;

        [Header("Locomotion Bob & Tilt (흔들림 제거: 기본 0)")]
        [Tooltip("이동 시 몸통 상하 바운스 강도 (0: 흔들림 없음)")]
        public float walkBobAmount = 0f;
        [Tooltip("이동 시 머리 상하 바운스 강도 (0: 흔들림 없음)")]
        public float headBobAmount = 0f;
        [Tooltip("이동 시 발걸음에 맞춘 좌우 체중 이동 틸트 각도 (0: 흔들림 없음)")]
        public float walkTiltAmount = 0f;
        [Tooltip("머리 탄성 지연 추적 속도")]
        public float headLagSpeed = 18f;
        [Tooltip("정지 대기 중 가슴/머리 호흡 모션 강도")]
        public float idleBreatheAmount = 0.02f;

        [Header("Aim Lean Settings (조준 자세 보정)")]
        [Tooltip("조준 각도에 따른 상체 회전량")]
        public float bodyLeanFactor = 0.12f;
        [Tooltip("조준 각도에 따른 머리 회전량")]
        public float headLookFactor = 0.25f;
        [Tooltip("상체가 숙여지거나 젖혀질 때의 위치 오프셋 강도")]
        public float bodyShiftAmount = 0.04f;

        [Header("Recoil (사격 반동)")]
        [Tooltip("반동 넉백 강도")]
        public float recoilKickbackForce = 0.18f;
        [Tooltip("반동 복원 스프링 속도")]
        public float recoilRecoverySpeed = 16f;

        private Procedural2DAim aimController;
        private ProceduralCharacterController charCtrl;
        private Rigidbody2D rb;

        private Vector3 initialBodyLocalPos;
        private Vector3 initialHeadLocalPos;
        private Vector3 currentRecoilOffset;

        private float currentHeadBobY = 0f;
        private float currentHeadDipY = 0f;
        private float walkWeight = 0f;
        private float currentWalkTilt = 0f;

        private void Awake()
        {
            aimController = GetComponent<Procedural2DAim>();
            charCtrl = GetComponent<ProceduralCharacterController>();
            rb = GetComponent<Rigidbody2D>();

            if (bodyTransform != null) initialBodyLocalPos = bodyTransform.localPosition;
            if (headTransform != null) initialHeadLocalPos = headTransform.localPosition;
        }

        private void LateUpdate()
        {
            float aimAngle = aimController != null ? aimController.CurrentAimAngle : 0f;

            // 1. 반동 감쇠 복귀 (Damped Spring)
            currentRecoilOffset = Vector3.Lerp(currentRecoilOffset, Vector3.zero, Time.deltaTime * recoilRecoverySpeed);

            // 2. 이동 상태 및 보행 사이클 감지
            bool isMoving = false;
            bool isGrounded = true;
            float walkCycle = 0f;

            if (charCtrl != null)
            {
                isMoving = charCtrl.IsMoving;
                isGrounded = charCtrl.IsGrounded;
                walkCycle = charCtrl.WalkCycleTimer;
            }

            // 3. 이동 블렌딩 가중치 (Idle <-> Walk 전환을 부드럽게)
            walkWeight = Mathf.MoveTowards(walkWeight, (isMoving && isGrounded) ? 1f : 0f, Time.deltaTime * 12f);

            // 4. 달리기 시 머리 높이 하강 (Run Head Dip)
            // 뛸 때는 머리 높이가 4픽셀 가량 아래로 확실히 내려가고, 정지(Idle) 시에는 원래 높이로 부드럽게 복귀
            float targetHeadDip = -runHeadDipAmount * walkWeight;
            currentHeadDipY = Mathf.Lerp(currentHeadDipY, targetHeadDip, Time.deltaTime * headDipSpeed);

            // 상하 바운스 및 틸트 (설정값이 0이면 흔들림 완전 차단)
            float bodyBobY = (walkBobAmount > 0.001f) ? Mathf.Sin(walkCycle * 2f) * walkBobAmount * walkWeight : 0f;
            float targetHeadBobY = (headBobAmount > 0.001f) ? Mathf.Sin(walkCycle * 2f - 0.45f) * headBobAmount * walkWeight : 0f;
            currentHeadBobY = Mathf.Lerp(currentHeadBobY, targetHeadBobY, Time.deltaTime * headLagSpeed);

            float targetTilt = (walkTiltAmount > 0.001f) ? Mathf.Sin(walkCycle) * walkTiltAmount * walkWeight : 0f;
            currentWalkTilt = Mathf.Lerp(currentWalkTilt, targetTilt, Time.deltaTime * 12f);

            // 정지 시 부드러운 유기적 호흡 (이동 중에는 호흡 흔들림 제거)
            float idleBreathe = Mathf.Sin(Time.time * 2.4f) * idleBreatheAmount * (1f - walkWeight);

            // 6. 상체(Body) 위치 및 각도 적용
            if (bodyTransform != null)
            {
                float bodyRotZ = (aimAngle * bodyLeanFactor) + currentWalkTilt;
                bodyTransform.localRotation = Quaternion.Euler(0f, 0f, bodyRotZ);

                Vector3 targetBodyPos = initialBodyLocalPos + bodyOffset;
                targetBodyPos.y += (Mathf.Sin(aimAngle * Mathf.Deg2Rad) * bodyShiftAmount) + bodyBobY + idleBreathe;
                targetBodyPos.x += (aimAngle > 0 ? -1f : 1f) * Mathf.Abs(aimAngle / 90f) * (bodyShiftAmount * 0.5f);
                bodyTransform.localPosition = targetBodyPos + currentRecoilOffset;
            }

            // 7. 머리(Head) 위치 및 각도 적용
            if (headTransform != null)
            {
                // 머리는 조준 시선 유지 (틸트가 0이면 머리 각도 흔들림 없음)
                float headRotZ = (aimAngle * headLookFactor) - (currentWalkTilt * 0.35f);
                headTransform.localRotation = Quaternion.Euler(0f, 0f, headRotZ);

                Vector3 targetHeadPos = initialHeadLocalPos + headOffset;
                targetHeadPos.y += (Mathf.Sin(aimAngle * Mathf.Deg2Rad) * (bodyShiftAmount * 0.5f)) 
                                 + currentHeadDipY 
                                 + currentHeadBobY 
                                 + (idleBreathe * 0.6f);
                headTransform.localPosition = targetHeadPos + (currentRecoilOffset * 0.7f);
            }
        }

        /// <summary>
        /// 사격 시 반동 넉백 벡터를 적용합니다.
        /// </summary>
        public void ApplyRecoil(Vector2 shootDirection)
        {
            bool facingRight = aimController != null ? aimController.IsFacingRight : true;
            float dirX = facingRight ? -shootDirection.x : shootDirection.x;
            Vector3 kickback = new Vector3(dirX, -shootDirection.y * 0.5f, 0f).normalized * recoilKickbackForce;

            currentRecoilOffset = Vector3.ClampMagnitude(currentRecoilOffset + kickback, recoilKickbackForce * 1.5f);
        }
    }
}
