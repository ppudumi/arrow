using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Procedural2D
{
    /// <summary>
    /// 플레이어 캐릭터를 부드럽고 민첩하게 추적하면서, 마우스 조준 방향으로 시야를 능동적으로 확장(Aim Lookahead)해주는 2D 카메라 컨트롤러입니다.
    /// Y축(수직)은 플레이어 점프/상승/낙하를 최우선으로 즉각 추적하며, 지형 높낮이에 제약받지 않고 시야를 확보합니다.
    /// </summary>
    public class CameraFollow2D : MonoBehaviour
    {
        [Tooltip("추적할 대상 (플레이어)")]
        public Transform target;

        [Header("Tracking Speed (추적 속도)")]
        [Tooltip("X축(좌우) 추적 스무딩 속도")]
        public float smoothSpeedX = 6.5f;

        [Tooltip("Y축(상하) 추적 스무딩 속도 - 플레이어 점프/착지를 최우선으로 민첩하게 추적")]
        public float smoothSpeedY = 9.0f;

        [Tooltip("기존 호환용 기본 스무딩 속도")]
        public float smoothSpeed = 7.5f;

        [Tooltip("카메라 기본 위치 오프셋 (플레이어 대비)")]
        public Vector3 offset = new Vector3(0f, 1.8f, -10f);

        [Header("Vertical Player Priority (Y축 플레이어 우선 추적)")]
        [Tooltip("Y축 추적 활성화")]
        public bool followPlayerY = true;

        [Tooltip("Y축 경계 제한 여부 (false 시 맵 높이에 구애받지 않고 플레이어 높이를 자유롭게 추적)")]
        public bool clampYBounds = false;

        [Header("Mouse Aim Lookahead (마우스 조준 방향 시야 쏠림)")]
        [Tooltip("마우스 방향으로 시야를 능동적으로 쏠리게 할지 여부")]
        public bool enableMouseLook = true;

        [Tooltip("마우스 방향 시야 이동 최대 거리 (X: 좌우 최대 이동, Y: 상하 최대 이동)")]
        public Vector2 maxMouseOffset = new Vector2(5.5f, 2.5f);

        [Tooltip("마우스 민감도/반응성")]
        [Range(0.2f, 2.5f)]
        public float mouseSensitivity = 1.0f;

        [Tooltip("마우스 시야 쏠림 추적 부드러움 (Lerp 스피드)")]
        public float mouseSmoothSpeed = 6.0f;

        [Header("Camera Bounds (맵 영역 제한)")]
        [Tooltip("좌우 수평 경계 제한 사용")]
        public bool useBounds = true;
        public Vector2 minBounds = new Vector2(-25f, -20f);
        public Vector2 maxBounds = new Vector2(25f, 60.0f);

        private Camera cam;
        private Vector3 currentMouseOffset = Vector3.zero;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                GameObject p = GameObject.FindWithTag("Player");
                if (p != null) target = p.transform;
                else return;
            }

            if (cam == null) cam = Camera.main;

            Vector3 targetMouseOffset = Vector3.zero;

            // 1. 마우스 조준 방향 시야 확장 연산
            if (enableMouseLook && cam != null)
            {
                Vector2 mouseScreenPos = GetMouseScreenPosition();

                Vector3 playerScreenPos = cam.WorldToScreenPoint(target.position);
                Vector2 delta = mouseScreenPos - (Vector2)playerScreenPos;

                float screenRadius = Mathf.Min(Screen.width, Screen.height) * 0.45f;
                if (screenRadius > 0.001f)
                {
                    float normX = Mathf.Clamp((delta.x / screenRadius) * mouseSensitivity, -1f, 1f);
                    float normY = Mathf.Clamp((delta.y / screenRadius) * mouseSensitivity, -1f, 1f);

                    targetMouseOffset.x = normX * maxMouseOffset.x;
                    targetMouseOffset.y = normY * maxMouseOffset.y;
                }
            }

            currentMouseOffset = Vector3.Lerp(currentMouseOffset, targetMouseOffset, Time.deltaTime * mouseSmoothSpeed);

            // 2. X축(수평) 위치 연산
            float targetPosX = target.position.x + offset.x + currentMouseOffset.x;
            if (useBounds)
            {
                targetPosX = Mathf.Clamp(targetPosX, minBounds.x, maxBounds.x);
            }
            float speedX = (smoothSpeedX > 0.1f) ? smoothSpeedX : smoothSpeed;
            float newX = Mathf.Lerp(transform.position.x, targetPosX, Time.deltaTime * speedX);

            // 3. Y축(수직) 위치 연산 - 플레이어의 Y 위치를 최우선으로 즉각 추적!
            float newY = transform.position.y;
            if (followPlayerY)
            {
                float targetPosY = target.position.y + offset.y + currentMouseOffset.y;
                if (useBounds && clampYBounds)
                {
                    targetPosY = Mathf.Clamp(targetPosY, minBounds.y, maxBounds.y);
                }
                float speedY = (smoothSpeedY > 0.1f) ? smoothSpeedY : Mathf.Max(speedX * 1.3f, 8.5f);
                newY = Mathf.Lerp(transform.position.y, targetPosY, Time.deltaTime * speedY);
            }

            // 4. 카메라 위치 갱신 (Z축 고정)
            transform.position = new Vector3(newX, newY, offset.z);
        }

        private Vector2 GetMouseScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }
#endif
            return Input.mousePosition;
        }
    }
}
