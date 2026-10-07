using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Procedural2D
{
    /// <summary>
    /// 마우스 커서를 추적하여 360도 절차적 조준(Aiming), 좌우 반전, 팔·활 자세와 사격 반동(Recoil) 연출을 담당하는 컴포넌트입니다.
    /// 사격·회수·화살통 같은 전투 규칙은 이 컴포넌트가 아니라 게임 쪽 전투 시스템(Laurel.Combat)이 담당합니다.
    /// </summary>
    public class Procedural2DAim : MonoBehaviour
    {
        public static Procedural2DAim Instance { get; private set; }

        [Header("Aim Targets & Pivots")]
        [Tooltip("활을 쥐고 있는 왼팔의 어깨 회전축")]
        public Transform leftArmPivot;
        [Tooltip("활시위를 당기는 오른팔의 어깨 회전축")]
        public Transform rightArmPivot;
        [Tooltip("좌우 반전을 담당하는 루트 트랜스폼")]
        public Transform visualRoot;

        [Header("Two-Segment String Arm (시위 당기는 2단 팔)")]
        [Tooltip("오른팔 상완 (팔뚝)")]
        public Transform rightUpperArm;
        [Tooltip("오른팔 전완 (팔목 / 시위 당기는 손)")]
        public Transform rightForearm;
        [Tooltip("팔꿈치 굽힘 기본 각도")]
        public float elbowBaseAngle = 65f;

        [Header("Bow & Hand Separation (분리된 활과 손)")]
        [Tooltip("활 본체 트랜스폼")]
        public Transform bowTransform;
        [Tooltip("활을 잡은 손 트랜스폼")]
        public Transform bowHandTransform;
        public SpriteRenderer bowRenderer;
        public SpriteRenderer bowHandRenderer;

        [Header("Aiming Parameters")]
        [Tooltip("조준 회전 추적 속도 (부드러운 에이밍)")]
        public float aimSmoothSpeed = 25f;
        [Tooltip("조준 가능한 상하 각도 제한 (도 단위, -85 ~ 85)")]
        public Vector2 angleClamp = new Vector2(-85f, 85f);

        [Header("Muzzle (화살이 나가는 위치)")]
        [Tooltip("화살이 발사되는 총구/활 위치")]
        public Transform muzzlePoint;

        [Header("Dynamic Sorting (레이어 순서)")]
        public SpriteRenderer leftArmBowRenderer;
        [Tooltip("위를 쏠 때 왼팔의 Sorting Order")]
        public int armOrderHigh = 5;
        [Tooltip("아래를 쏠 때 왼팔의 Sorting Order")]
        public int armOrderLow = 4;

        /// <summary>false이면 마우스 조준을 갱신하지 않고 마지막 자세를 유지합니다 (대화·메뉴 중).</summary>
        [HideInInspector] public bool inputEnabled = true;

        /// <summary>값이 있으면 마우스 대신 이 월드 좌표를 조준합니다 (자동 검증용).</summary>
        public System.Func<Vector2?> AimWorldOverride;

        // 내부 상태 프로퍼티
        public float CurrentAimAngle { get; private set; }
        public bool IsFacingRight { get; private set; } = true;
        public Vector2 AimDirection { get; private set; } = Vector2.right;

        private Camera mainCam;
        private float currentArmAngle;
        private float currentRecoilAngle = 0f;
        private ProceduralBodyShift bodyShift;
        private ProceduralCharacterController charCtrl;

        private void Awake()
        {
            Instance = this;
            mainCam = Camera.main;
            bodyShift = GetComponent<ProceduralBodyShift>();
            charCtrl = GetComponent<ProceduralCharacterController>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (mainCam == null)
            {
                mainCam = Camera.main;
                if (mainCam == null) return;
            }
            if (!inputEnabled) return;

            Vector2? overrideAim = AimWorldOverride != null ? AimWorldOverride() : null;
            Vector3 mouseWorld;
            if (overrideAim.HasValue)
            {
                mouseWorld = overrideAim.Value;
            }
            else
            {
                Vector2 mouseScreen = GetMouseScreenPosition();
                mouseWorld = mainCam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -mainCam.transform.position.z));
            }
            HandleAiming(mouseWorld);
        }

        /// <summary>
        /// 발사·찌르기 시 사격 반동 연출을 재생합니다.
        /// </summary>
        public void PlayShotRecoil(Vector2 direction)
        {
            if (bodyShift != null) bodyShift.ApplyRecoil(direction);
            currentRecoilAngle = 22f;
        }

        /// <summary>바라보는 방향을 즉시 지정합니다 (방 입장 시 초기 자세용).</summary>
        public void SetFacing(bool right)
        {
            IsFacingRight = right;
            if (visualRoot != null)
            {
                Vector3 scale = visualRoot.localScale;
                scale.x = right ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
                visualRoot.localScale = scale;
            }
        }

        private Vector2 GetMouseScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }
            return Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        private void HandleAiming(Vector3 mouseWorld)
        {
            Vector3 pivotPos = leftArmPivot != null ? leftArmPivot.position : transform.position;
            Vector2 dirToMouse = (mouseWorld - pivotPos);
            if (dirToMouse.sqrMagnitude < 0.0001f) return;
            AimDirection = dirToMouse.normalized;

            bool shouldFaceRight = mouseWorld.x >= transform.position.x;
            if (shouldFaceRight != IsFacingRight)
            {
                if (charCtrl == null || (!charCtrl.IsSpinning && !charCtrl.IsRolling))
                {
                    SetFacing(shouldFaceRight);
                }
            }

            float rawAngle = Mathf.Atan2(dirToMouse.y, Mathf.Abs(dirToMouse.x)) * Mathf.Rad2Deg;
            rawAngle = Mathf.Clamp(rawAngle, angleClamp.x, angleClamp.y);
            CurrentAimAngle = rawAngle;

            currentArmAngle = Mathf.LerpAngle(currentArmAngle, rawAngle, Time.deltaTime * aimSmoothSpeed);
            if (leftArmPivot != null)
            {
                leftArmPivot.localRotation = Quaternion.Euler(0f, 0f, currentArmAngle);
            }

            // 오른팔 2마디 관절 회전 (상완 + 전완 팔꿈치 굽힘)
            currentRecoilAngle = Mathf.Lerp(currentRecoilAngle, 0f, Time.deltaTime * 12f);
            if (rightUpperArm != null)
            {
                float upperAngle = -20f + (currentArmAngle * 0.35f);
                rightUpperArm.localRotation = Quaternion.Euler(0f, 0f, upperAngle);

                if (rightForearm != null)
                {
                    float elbowAngle = elbowBaseAngle + (currentRecoilAngle * 0.4f);
                    rightForearm.localRotation = Quaternion.Euler(0f, 0f, elbowAngle);
                }
            }
            else if (rightArmPivot != null)
            {
                float rightAngle = -15f + (currentArmAngle * 0.35f);
                rightArmPivot.localRotation = Quaternion.Euler(0f, 0f, rightAngle);
            }

            // 왼팔 및 분리된 활/손 소팅 오더 갱신
            int sortingOrder = (rawAngle > 30f) ? armOrderHigh : armOrderLow;
            if (leftArmBowRenderer != null)
            {
                leftArmBowRenderer.sortingOrder = sortingOrder;
            }
            if (bowRenderer != null)
            {
                bowRenderer.sortingOrder = sortingOrder;
            }
            if (bowHandRenderer != null)
            {
                bowHandRenderer.sortingOrder = sortingOrder + 1;
            }
        }
    }
}
