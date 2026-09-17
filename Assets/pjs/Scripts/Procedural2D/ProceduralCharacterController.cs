using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Procedural2D
{
    /// <summary>
    /// 캐릭터의 2D 이동, 2단 점프, 공중 360도 덤블링, 쉬프트(Shift) 초고속 대시(Flash Dash) 및 네온 잔상(Ghost Trail) 효과를 제어합니다.
    /// 구르지 않고 레이저처럼 초고속으로 전방 돌진합니다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class ProceduralCharacterController : MonoBehaviour
    {
        [Header("Movement Settings")]
        public float moveSpeed = 11.66f;

        [Header("Jump & Double Jump Settings (2단 점프)")]
        [Tooltip("1단 점프력")]
        public float firstJumpForce = 21.0f;
        [Tooltip("2단 점프력")]
        public float secondJumpForce = 17.0f;
        [Tooltip("최대 점프 가능 횟수")]
        public int maxJumps = 2;

        [Header("Double Jump 360 Spin (공중 360도 덤블링 회전)")]
        [Tooltip("회전시킬 비주얼 트랜스폼 (콜라이더는 유지하고 비주얼만 덤블링)")]
        public Transform visualTransform;
        [Tooltip("360도 회전 완료 시간 (초)")]
        public float spinDuration = 0.42f;

        [Header("Flash Dash Settings (쉬프트 초고속 대시 & 잔상)")]
        [Tooltip("대시 돌진 속도 (구르지 않고 초고속 순간 돌진!)")]
        public float dashSpeed = 28.5f;
        [Tooltip("대시 지속 시간 (초)")]
        public float dashDuration = 0.18f;
        [Tooltip("대시 재사용 쿨타임 (초)")]
        public float dashCooldown = 0.45f;
        [Tooltip("대시 돌진 시 공기역학적 전방 기울기 각도 (도 단위)")]
        public float dashLeanAngle = 12f;
        [Tooltip("돌진 잔상 생성 간격 (초 단위, 낮을수록 촘촘함)")]
        public float ghostInterval = 0.024f;
        [Tooltip("돌진 잔상 페이드 유지 시간 (초)")]
        public float ghostDuration = 0.32f;
        [Tooltip("돌진 잔상 색상 틴트 (선명한 네온 시안/블루)")]
        public Color ghostColor = new Color(0.25f, 0.85f, 1f, 0.75f);

        [Header("Body References")]
        public Transform bodyTransform;
        public Transform headTransform;
        public Transform legsTransform;

        [Header("4-Piece Legs Procedural Animation (4조각 다리 절차적 보행 애니메이션)")]
        [Tooltip("왼쪽 허벅지")]
        public Transform legUpperL;
        [Tooltip("왼쪽 종아리")]
        public Transform legLowerL;
        [Tooltip("오른쪽 허벅지")]
        public Transform legUpperR;
        [Tooltip("오른쪽 종아리")]
        public Transform legLowerR;

        [Tooltip("보행 시 허벅지 최대 스윙 각도 (도)")]
        public float legSwingAngle = 35f;
        [Tooltip("보행 시 무릎 굽힘 각도 (도)")]
        public float kneeBendAngle = 45f;

        [Header("Idle Breathing (대기 중 호흡 모션)")]
        public float breatheSpeed = 2.0f;
        public float breatheAmount = 0.025f;

        [Header("Walking / Hover Motion (이동 시 바운스 모션)")]
        public float walkCycleSpeed = 10f;
        public float walkBounceAmount = 0.035f;

        private Rigidbody2D rb;
        private Procedural2DAim aimCtrl;
        private bool isGrounded = true;
        private int remainingJumps = 2;
        private float walkCycleTimer;
        private Vector3 initialBodyLocalPos;

        // 공중 360도 회전 상태 변수
        private bool isSpinning = false;
        private float spinTimer = 0f;
        private float spinDirection = -1f;

        // 초고속 대시 상태 변수
        private bool isDashing = false;
        private float dashTimer = 0f;
        private float dashDirection = 1f;
        private float nextDashAllowedTime = 0f;
        private float nextGhostSpawnTime = 0f;

        /// <summary>
        /// 현재 공중제비(360도 점프 회전) 중인지 여부
        /// </summary>
        public bool IsSpinning => isSpinning;

        /// <summary>
        /// 현재 초고속 대시 돌진 중인지 여부
        /// </summary>
        public bool IsDashing => isDashing;

        /// <summary>
        /// 하위 호환용 롤링 프로퍼티 (IsDashing 매핑)
        /// </summary>
        public bool IsRolling => isDashing;

        public bool IsGrounded => isGrounded;
        public float WalkCycleTimer => walkCycleTimer;
        public bool IsMoving => (Mathf.Abs(rb != null ? rb.linearVelocity.x : 0f) > 0.15f || Mathf.Abs(GetHorizontalInput()) > 0.05f) && isGrounded;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            aimCtrl = GetComponent<Procedural2DAim>();
            if (visualTransform == null) visualTransform = transform.Find("Visual");
            if (bodyTransform != null) initialBodyLocalPos = bodyTransform.localPosition;
            remainingJumps = maxJumps;
        }

        private void Update()
        {
            float inputX = GetHorizontalInput();
            bool jumpPressed = GetJumpInput();
            bool dashPressed = GetDashInput();

            // 1. 쉬프트 초고속 대시 트리거
            if (dashPressed && !isDashing && Time.time >= nextDashAllowedTime)
            {
                StartDash(inputX);
            }

            // 2. 대시 진행 중인 경우
            if (isDashing)
            {
                UpdateDash();
                return; // 대시 중에는 기본 이동/점프를 덮어씀
            }

            // 3. 2단 점프 및 공중제비 처리
            if (jumpPressed)
            {
                if (isGrounded)
                {
                    // 1단 점프
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, firstJumpForce);
                    isGrounded = false;
                    remainingJumps = maxJumps - 1;
                }
                else if (remainingJumps > 0)
                {
                    // 2단 공중 점프 + 360도 공중제비 발동!
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, secondJumpForce);
                    remainingJumps--;

                    StartDoubleJumpSpin(inputX);
                }
            }

            // 4. 일반 좌우 이동 속도 적용
            rb.linearVelocity = new Vector2(inputX * moveSpeed, rb.linearVelocity.y);

            // 5. 공중 점프 360도 덤블링 애니메이션
            UpdateSpinAnimation();

            // 다리 애니메이션 업데이트 (보행, 점프, 대시)
            bool isMovingNow = Mathf.Abs(inputX) > 0.05f && isGrounded;
            UpdateLegsAnimation(inputX, isMovingNow);

            // 6. 절차적 바운스/호흡 모션 (회전/대시 중이 아닐 때만 적용)
            if (!isSpinning && !isDashing)
            {
                if (isMovingNow)
                {
                    walkCycleTimer += Time.deltaTime * walkCycleSpeed;
                }
                else
                {
                    walkCycleTimer = 0f;
                }
            }
        }

        #region 초고속 대시 & 잔상 시스템 (Flash Dash & Ghost Trail)

        private void StartDash(float inputX)
        {
            isDashing = true;
            dashTimer = 0f;
            nextDashAllowedTime = Time.time + dashCooldown;
            nextGhostSpawnTime = 0f;

            // 이동 입력이 있으면 그 방향으로, 없으면 현재 바라보는 방향으로 대시
            if (Mathf.Abs(inputX) > 0.1f)
            {
                dashDirection = Mathf.Sign(inputX);
            }
            else
            {
                bool facingRight = (aimCtrl != null) ? aimCtrl.IsFacingRight : (visualTransform != null && visualTransform.localScale.x >= 0);
                dashDirection = facingRight ? 1f : -1f;
            }

            // 공중/지면 모두 레이저처럼 초고속 수평 돌진
            rb.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);

            // 점프 회전 중이었다면 똑바로 정렬
            isSpinning = false;
            if (visualTransform != null)
            {
                visualTransform.localRotation = Quaternion.identity;
            }

            // 첫 잔상 즉시 방출
            SpawnGhostTrail();
        }

        private void UpdateDash()
        {
            dashTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(dashTimer / dashDuration);

            // 1. 폭발적인 초고속 수평 속도 유지
            rb.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);

            // 2. 구르지 않고, 속도감 있는 공기역학적 전방 기울기만 살짝 적용
            if (visualTransform != null)
            {
                float lean = (dashDirection > 0f) ? -dashLeanAngle : dashLeanAngle;
                visualTransform.localRotation = Quaternion.Euler(0f, 0f, lean);
            }

            // 3. 촘촘하고 선명한 잔상 생성
            if (Time.time >= nextGhostSpawnTime)
            {
                nextGhostSpawnTime = Time.time + ghostInterval;
                SpawnGhostTrail();
            }

            // 4. 대시 완료 시 자세 복귀 및 부드러운 속도 인계
            if (progress >= 1f)
            {
                isDashing = false;
                if (visualTransform != null)
                {
                    visualTransform.localRotation = Quaternion.identity;
                }
                rb.linearVelocity = new Vector2(dashDirection * moveSpeed, rb.linearVelocity.y);
            }
        }

        private void SpawnGhostTrail()
        {
            if (visualTransform == null) return;

            // 잔상 루트 컨테이너 생성
            GameObject ghostRoot = new GameObject("Ghost_Afterimage");
            ghostRoot.transform.position = visualTransform.position;
            ghostRoot.transform.rotation = visualTransform.rotation;
            ghostRoot.transform.localScale = visualTransform.lossyScale;

            SpriteRenderer[] sourceRenderers = visualTransform.GetComponentsInChildren<SpriteRenderer>();
            List<SpriteRenderer> ghostRenderers = new List<SpriteRenderer>();

            foreach (var sr in sourceRenderers)
            {
                if (sr == null || !sr.enabled || sr.sprite == null) continue;

                GameObject part = new GameObject(sr.gameObject.name + "_Ghost");
                part.transform.SetParent(ghostRoot.transform);
                part.transform.position = sr.transform.position;
                part.transform.rotation = sr.transform.rotation;
                part.transform.localScale = sr.transform.lossyScale;

                SpriteRenderer ghostSr = part.AddComponent<SpriteRenderer>();
                ghostSr.sprite = sr.sprite;
                ghostSr.color = ghostColor;
                ghostSr.sortingLayerID = sr.sortingLayerID;
                ghostSr.sortingOrder = sr.sortingOrder - 1;

                ghostRenderers.Add(ghostSr);
            }

            StartCoroutine(FadeAndDestroyGhost(ghostRoot, ghostRenderers, ghostColor, ghostDuration));
        }

        private IEnumerator FadeAndDestroyGhost(GameObject ghost, List<SpriteRenderer> renderers, Color startColor, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(startColor.a, 0f, elapsed / duration);
                Color c = new Color(startColor.r, startColor.g, startColor.b, alpha);

                foreach (var r in renderers)
                {
                    if (r != null) r.color = c;
                }
                yield return null;
            }

            if (ghost != null)
            {
                Destroy(ghost);
            }
        }

        #endregion

        #region 점프 360도 덤블링 시스템

        private void StartDoubleJumpSpin(float inputX)
        {
            isSpinning = true;
            spinTimer = 0f;

            if (Mathf.Abs(inputX) > 0.1f)
            {
                spinDirection = (inputX > 0f) ? -1f : 1f;
            }
            else
            {
                bool facingRight = (aimCtrl != null) ? aimCtrl.IsFacingRight : (visualTransform != null && visualTransform.localScale.x >= 0);
                spinDirection = facingRight ? -1f : 1f;
            }
        }

        private void UpdateSpinAnimation()
        {
            if (!isSpinning) return;

            spinTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(spinTimer / spinDuration);

            float t = Mathf.SmoothStep(0f, 1f, progress);
            float currentZAngle = t * 360f * spinDirection;

            if (visualTransform != null)
            {
                visualTransform.localRotation = Quaternion.Euler(0f, 0f, currentZAngle);
            }

            if (progress >= 1f)
            {
                isSpinning = false;
                if (visualTransform != null)
                {
                    visualTransform.localRotation = Quaternion.identity;
                }
            }
        }

        #endregion

        private float GetHorizontalInput()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                float x = 0f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) x += 1f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) x -= 1f;
                return x;
            }
            return 0f;
#else
            return Input.GetAxisRaw("Horizontal");
#endif
        }

        private bool GetJumpInput()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                return k.spaceKey.wasPressedThisFrame;
            }
            return false;
#else
            return Input.GetButtonDown("Jump");
#endif
        }

        private bool GetDashInput()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                return k.leftShiftKey.wasPressedThisFrame || k.rightShiftKey.wasPressedThisFrame;
            }
            return false;
#else
            return Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
#endif
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            foreach (ContactPoint2D contact in collision.contacts)
            {
                if (contact.normal.y > 0.5f)
                {
                    isGrounded = true;
                    remainingJumps = maxJumps;

                    // 착지 시 공중 덤블링 중이었다면 회전 정렬
                    if (isSpinning)
                    {
                        isSpinning = false;
                        if (visualTransform != null)
                        {
                            visualTransform.localRotation = Quaternion.identity;
                        }
                    }
                    break;
                }
            }
        }
        private void UpdateLegsAnimation(float inputX, bool isMoving)
        {
            if (legUpperL == null && legUpperR == null) return;

            if (isDashing)
            {
                // 대시 중: 역동적으로 다리가 뒤로 뻗어짐
                if (legUpperL != null) legUpperL.localRotation = Quaternion.Euler(0f, 0f, -25f);
                if (legLowerL != null) legLowerL.localRotation = Quaternion.Euler(0f, 0f, 30f);

                if (legUpperR != null) legUpperR.localRotation = Quaternion.Euler(0f, 0f, -40f);
                if (legLowerR != null) legLowerR.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
            else if (!isGrounded)
            {
                // 공중/점프 중: 다리를 살짝 접어 올림
                if (legUpperL != null) legUpperL.localRotation = Quaternion.Euler(0f, 0f, 20f);
                if (legLowerL != null) legLowerL.localRotation = Quaternion.Euler(0f, 0f, -35f);

                if (legUpperR != null) legUpperR.localRotation = Quaternion.Euler(0f, 0f, 10f);
                if (legLowerR != null) legLowerR.localRotation = Quaternion.Euler(0f, 0f, -25f);
            }
            else if (isMoving)
            {
                // 지상 보행: 180도 위상차의 자연스러운 걷기 사이클
                float phaseL = Mathf.Sin(walkCycleTimer);
                float phaseR = Mathf.Sin(walkCycleTimer + Mathf.PI);

                // 왼쪽 다리
                if (legUpperL != null)
                {
                    float swingL = phaseL * legSwingAngle;
                    legUpperL.localRotation = Quaternion.Euler(0f, 0f, swingL);
                    if (legLowerL != null)
                    {
                        float bendL = (phaseL > 0f) ? Mathf.Sin(walkCycleTimer) * kneeBendAngle : 0f;
                        legLowerL.localRotation = Quaternion.Euler(0f, 0f, -bendL);
                    }
                }

                // 오른쪽 다리
                if (legUpperR != null)
                {
                    float swingR = phaseR * legSwingAngle;
                    legUpperR.localRotation = Quaternion.Euler(0f, 0f, swingR);
                    if (legLowerR != null)
                    {
                        float bendR = (phaseR > 0f) ? Mathf.Sin(walkCycleTimer + Mathf.PI) * kneeBendAngle : 0f;
                        legLowerR.localRotation = Quaternion.Euler(0f, 0f, -bendR);
                    }
                }
            }
            else
            {
                // 정지 (Idle): 자연스럽게 안정적인 스탠스
                if (legUpperL != null) legUpperL.localRotation = Quaternion.identity;
                if (legLowerL != null) legLowerL.localRotation = Quaternion.identity;
                if (legUpperR != null) legUpperR.localRotation = Quaternion.identity;
                if (legLowerR != null) legLowerR.localRotation = Quaternion.identity;
            }
        }
    }
}
