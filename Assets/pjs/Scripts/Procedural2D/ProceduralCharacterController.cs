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

        [Header("Wall Climb (벽타기) — 기본 꺼짐. 테스트 씬의 프리팹 Variant 에서만 켠다")]
        [Tooltip("켜면 벽에 닿은 상태에서 W(또는 ↑)를 누르고 있는 동안 벽을 타고 올라간다")]
        public bool wallClimbEnabled = false;
        [Tooltip("벽을 오르는 속도 (유닛/초)")]
        public float climbSpeed = 6f;
        [Tooltip("벽 꼭대기를 넘었을 때 위로 살짝 올려 주는 속도 (모서리에 걸리지 않고 윗면에 올라서도록). 0 이면 없음")]
        public float climbTopBoost = 5f;
        [Tooltip("벽 꼭대기를 넘었을 때 벽 쪽으로 밀어 주는 가로 속도 (윗면 위로 올라서게)")]
        public float climbTopPush = 4f;
        [Tooltip("벽 감지 거리 (콜라이더 옆면에서 바깥쪽, 유닛)")]
        public float wallCheckDistance = 0.08f;
        [Tooltip("벽으로 인정할 레이어 (트리거·한쪽 통과 발판·자기 자신은 제외)")]
        public LayerMask climbMask = ~0;

        [Header("Wall Cling (벽 매달리기) — 기본 꺼짐. wallClimbEnabled 와 함께 켠다 (튜토리얼 보스맵 Variant 전용)")]
        [Tooltip("켜면 W/↑ 벽타기 대신: 공중에서 벽에 닿으면 붙어서 멈추고, 벽 쪽 방향키로 오르고, 반대 방향키로 떨어지고, 반대 방향키+점프로 벽점프")]
        public bool wallCling = false;
        [Tooltip("벽점프 세로 속도")]
        public float wallJumpForce = 11f;
        [Tooltip("벽점프 가로 속도 (벽 반대쪽)")]
        public float wallJumpPush = 9f;
        [Tooltip("벽점프 직후 이 시간 동안은 벽 반대쪽으로 최소 wallJumpPush 만큼 날아간다 (키를 떼도 벽을 차고 나가 보이게)")]
        public float wallJumpLockTime = 0.15f;
        [Tooltip("반대 방향키로 떨어질 때 벽에서 살짝 떨어지는 가로 속도")]
        public float clingDetachPush = 3f;
        [Tooltip("반대 방향키로 떨어질 때 살짝 떨어지는 시간 (그동안 가로 속도 고정, 이후 일반 이동)")]
        public float clingDetachTime = 0.12f;
        [Tooltip("반대 방향키로 먼저 떨어진 뒤 이 시간 안에 점프를 눌러도 벽점프로 친다 (두 키를 동시에 누르기 어렵지 않게)")]
        public float wallJumpGrace = 0.12f;
        [Tooltip("붙어 있을 때 반대 방향키 없이 점프: 벽을 따라 위로 짧게 뛰었다가 다시 붙는 세로 속도. 0 이면 점프 무시")]
        public float clingHopForce = 8f;
        [Tooltip("벽에서 떨어진 뒤(반대 방향·벽점프) 같은 벽에 다시 붙지 않는 최소 시간 (초). 그 벽과의 접촉이 한 번 끊기거나 착지해야 다시 붙는다")]
        public float regrabDelay = 0.3f;

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

        [Header("Gameplay Gates (게임 진행 상태에 따른 제어)")]
        [Tooltip("false이면 이동·점프·대시 입력을 받지 않습니다 (대화·메뉴 중)")]
        [HideInInspector] public bool inputEnabled = true;
        [Tooltip("false이면 대시(Shift)를 사용할 수 없습니다 (유니크 스킬 미보유)")]
        [HideInInspector] public bool dashUnlocked = true;

        /// <summary>값이 있으면 키보드 대신 이 입력을 사용합니다 (자동 검증용). x=좌우, y>0.5=점프 누름, z>0.5=대시 누름</summary>
        public System.Func<Vector3> InputOverride;
        /// <summary>값이 있으면 키보드 대신 이 값으로 벽타기(W/↑ 누르고 있음) 입력을 받습니다 (자동 검증용)</summary>
        public System.Func<bool> ClimbInputOverride;

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

        // 벽타기 상태 변수
        private bool isClimbing = false;
        private int climbSide = 0;           // -1 왼쪽 벽, +1 오른쪽 벽
        private float topAssistTimer = 0f;   // 꼭대기를 넘은 직후 벽 쪽으로 올라서게 밀어 주는 시간
        private int topAssistSide = 0;
        private float baseGravityScale = 1f;
        private Collider2D bodyCollider;
        private readonly Collider2D[] wallHits = new Collider2D[8];
        private int regrabBlockedSide = 0;   // 자동 벽타기: 방금 떨어진 벽 쪽 (접촉이 끊기거나 착지하면 풀림)
        private float regrabBlockedUntil = 0f;
        private bool regrabContactLost = false;
        private float moveLockTimer = 0f;    // 벽에서 떨어질 때·벽점프 직후 가로 속도 고정 시간
        private float moveLockVx = 0f;
        private bool moveLockIsMinimum = false;
        private int detachSide = 0;          // 반대 방향키로 방금 떨어진 벽 (벽점프 유예용)
        private float detachTime = -1f;
        private bool hopRising = false;      // 붙은 채 위로 뛴 뒤 올라가는 동안은 다시 붙지 않음

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
        /// <summary>현재 벽을 타고 있는지 여부</summary>
        public bool IsClimbing => isClimbing;
        public float WalkCycleTimer => walkCycleTimer;
        public bool IsMoving => (Mathf.Abs(rb != null ? rb.linearVelocity.x : 0f) > 0.15f || Mathf.Abs(GetHorizontalInput()) > 0.05f) && isGrounded;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            aimCtrl = GetComponent<Procedural2DAim>();
            if (visualTransform == null) visualTransform = transform.Find("Visual");
            if (bodyTransform != null) initialBodyLocalPos = bodyTransform.localPosition;
            remainingJumps = maxJumps;
            baseGravityScale = rb.gravityScale;
            bodyCollider = GetComponent<Collider2D>();
        }

        private void Update()
        {
            float inputX = GetHorizontalInput();
            bool jumpPressed = GetJumpInput();
            bool dashPressed = GetDashInput();
            if (InputOverride != null)
            {
                Vector3 o = InputOverride();
                inputX = o.x;
                jumpPressed = o.y > 0.5f;
                dashPressed = o.z > 0.5f;
            }
            if (!inputEnabled)
            {
                inputX = 0f;
                jumpPressed = false;
                dashPressed = false;
            }
            if (!dashUnlocked) dashPressed = false;

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

            // 2.5 벽타기: 벽에 닿아 있고 W/↑ 를 누르고 있는 동안만 위로 오른다 (점프 입력은 무시 → 벽점프 없음)
            if (wallClimbEnabled && UpdateWallClimb(inputX, ref jumpPressed))
            {
                UpdateLegsAnimation(0f, false);
                walkCycleTimer = 0f;
                return;
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
            float vx = inputX * moveSpeed;
            if (moveLockTimer > 0f)
            {
                // 벽에서 떨어지는 중(살짝만 이동) 또는 벽점프 직후(최소 이만큼은 벽 반대쪽으로)
                moveLockTimer -= Time.deltaTime;
                float dir = Mathf.Sign(moveLockVx);
                if (moveLockIsMinimum) vx = dir * Mathf.Max(Mathf.Abs(moveLockVx), inputX * dir * moveSpeed);
                else vx = moveLockVx;
                if (isGrounded) moveLockTimer = 0f;
            }
            if (topAssistTimer > 0f)
            {
                // 벽 꼭대기를 막 넘었으면 반대쪽 입력이 없는 한 윗면 쪽으로 계속 밀어 준다 (W 만 누르고 있어도 올라선다)
                topAssistTimer -= Time.deltaTime;
                if (inputX * topAssistSide >= 0f && Mathf.Abs(vx) < climbTopPush) vx = topAssistSide * climbTopPush;
                if (isGrounded) topAssistTimer = 0f;
            }
            rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);

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

        /// <summary>
        /// 방 이동·부활처럼 플레이어를 순간이동시킬 때 대시·공중제비·속도를 정리합니다.
        /// </summary>
        public void ResetMotionState()
        {
            if (isClimbing) StopClimb(false);
            topAssistTimer = 0f;
            regrabBlockedSide = 0;
            moveLockTimer = 0f;
            detachSide = 0;
            hopRising = false;
            isDashing = false;
            isSpinning = false;
            remainingJumps = maxJumps;
            if (visualTransform != null) visualTransform.localRotation = Quaternion.identity;
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }

        #region 초고속 대시 & 잔상 시스템 (Flash Dash & Ghost Trail)

        private void StartDash(float inputX)
        {
            if (isClimbing) StopClimb(false);
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

        #region 벽타기 (Wall Climb)

        /// <summary>
        /// 벽타기 상태를 갱신한다. 벽에 붙어 있으면 true (이번 프레임의 일반 이동·점프를 대신함).
        /// W/↑ 모드(기본): 벽에 닿은 채 W/↑ 를 누르고 있는 동안 오른다. 떼거나 반대 방향키·꼭대기에서 끝. 점프 무시
        /// 매달리기 모드(wallCling): UpdateWallCling 참고
        /// </summary>
        private bool UpdateWallClimb(float inputX, ref bool jumpPressed)
        {
            if (wallCling) return UpdateWallCling(inputX, ref jumpPressed);
            bool held = inputEnabled && GetClimbInput();
            int side = DetectWall();
            if (isClimbing)
            {
                bool away = climbSide != 0 && inputX * climbSide < -0.1f;
                if (!held || away) { StopClimb(false); return false; }
                if (side != climbSide) { StopClimb(true); return false; } // 벽이 끝남 = 꼭대기를 넘음
            }
            else
            {
                if (!held || side == 0 || inputX * side < -0.1f) return false;
                StartClimb(side);
            }
            // 벽 쪽으로 살짝 밀어 접촉을 유지하며 일정한 속도로 오른다 (중력 없음)
            rb.linearVelocity = new Vector2(climbSide * 0.5f, climbSpeed);
            return true;
        }

        /// <summary>
        /// 벽 매달리기:
        /// - 붙기: 공중에서 벽 옆면에 닿으면 붙어서 멈춘다(미끄러지지 않음). 땅에 서 있거나 반대 방향키를 누르고 있으면 붙지 않는다
        /// - 오르기: 벽 쪽 방향키를 누르고 있는 동안 climbSpeed 로 오른다. 꼭대기를 넘으면 윗면으로 올려 준다. 천장에 막히면 그 자리에 붙어 있다
        /// - 떨어지기: 반대 방향키 → 벽에서 살짝 떨어진 뒤(clingDetachPush × clingDetachTime) 바로 떨어진다
        /// - 벽점프: 반대 방향키 + 점프 (반대 방향키로 막 떨어진 직후 wallJumpGrace 안의 점프도 포함) → 벽 반대쪽 위로 뛴다. 공중 점프 1회가 다시 생긴다
        /// - 붙은 채 점프(반대 방향키 없이) → 벽을 따라 위로 짧게 뛰었다가 내려오기 시작하면 다시 붙는다
        /// - 떨어지거나 벽점프한 벽에는 그 벽과 접촉이 끊기거나 착지한 뒤 regrabDelay 가 지나야 다시 붙는다
        /// </summary>
        private bool UpdateWallCling(float inputX, ref bool jumpPressed)
        {
            int side = DetectWall();
            if (regrabBlockedSide != 0)
            {
                if (side != regrabBlockedSide || isGrounded) regrabContactLost = true;
                if (regrabContactLost && Time.time >= regrabBlockedUntil) regrabBlockedSide = 0;
            }
            if (hopRising && (rb.linearVelocity.y <= 0f || side == 0 || isGrounded)) hopRising = false;

            // 반대 방향키로 막 떨어진 직후의 점프 = 벽점프
            if (!isClimbing && jumpPressed && detachSide != 0 && Time.time - detachTime <= wallJumpGrace && !IsStandingOnGround())
            {
                WallJump(-detachSide);
                detachSide = 0;
                jumpPressed = false;
                return false;
            }

            if (isClimbing)
            {
                int s = climbSide;
                if (!inputEnabled) { StopClimb(false); return false; }
                bool away = inputX * s < -0.1f;
                bool toward = inputX * s > 0.1f;
                if (jumpPressed)
                {
                    jumpPressed = false;
                    if (away) { BlockRegrab(s); StopClimb(false); WallJump(-s); return false; }
                    if (clingHopForce > 0f)
                    {
                        StopClimb(false);
                        rb.linearVelocity = new Vector2(s * 0.5f, clingHopForce);
                        hopRising = true;
                        return false;
                    }
                }
                if (away)
                {
                    BlockRegrab(s);
                    StopClimb(false);
                    rb.linearVelocity = new Vector2(-s * clingDetachPush, 0f);
                    moveLockTimer = clingDetachTime; moveLockVx = -s * clingDetachPush; moveLockIsMinimum = false;
                    detachSide = s; detachTime = Time.time;
                    return false;
                }
                if (side != s) { StopClimb(toward); return false; } // 오르다 벽이 끝남 = 꼭대기
                float vy = toward && !HitsCeiling() ? climbSpeed : 0f;
                rb.linearVelocity = new Vector2(s * 0.5f, vy);
                return true;
            }

            if (!inputEnabled || side == 0 || hopRising) return false;
            if (inputX * side < -0.1f) return false;          // 벽 반대쪽으로 가는 중에는 붙지 않는다
            if (side == regrabBlockedSide) return false;
            if (IsStandingOnGround()) return false;            // 지상에서 벽 옆에 서 있기만 한 경우는 아님 (발밑 직접 검사)
            StartClimb(side);
            rb.linearVelocity = new Vector2(side * 0.5f, 0f);
            return true;
        }

        private void WallJump(int dir)
        {
            rb.gravityScale = baseGravityScale;
            rb.linearVelocity = new Vector2(dir * wallJumpPush, wallJumpForce);
            moveLockTimer = wallJumpLockTime; moveLockVx = dir * wallJumpPush; moveLockIsMinimum = true;
            isGrounded = false;
            remainingJumps = maxJumps - 1;
            topAssistTimer = 0f;
        }

        private void BlockRegrab(int side)
        {
            if (!wallCling || side == 0) return;
            regrabBlockedSide = side;
            regrabContactLost = false;
            regrabBlockedUntil = Time.time + regrabDelay;
        }

        /// <summary>발 바로 아래에 디딜 곳이 있는지 (벽 매달리기가 지상에서 시작되지 않게)</summary>
        private bool IsStandingOnGround()
        {
            if (bodyCollider == null) return false;
            var b = bodyCollider.bounds;
            return ProbeSolid(new Vector2(b.center.x, b.min.y - wallCheckDistance * 0.5f), new Vector2(b.size.x * 0.8f, wallCheckDistance));
        }

        /// <summary>머리 바로 위가 막혀 있는지 (천장)</summary>
        private bool HitsCeiling()
        {
            if (bodyCollider == null) return false;
            var b = bodyCollider.bounds;
            return ProbeSolid(new Vector2(b.center.x, b.max.y + wallCheckDistance * 0.5f), new Vector2(b.size.x * 0.6f, wallCheckDistance));
        }

        private bool ProbeSolid(Vector2 center, Vector2 size)
        {
            int n = Physics2D.OverlapBoxNonAlloc(center, size, 0f, wallHits, climbMask);
            for (int i = 0; i < n; i++)
            {
                var c = wallHits[i];
                if (c == null || c.isTrigger) continue;
                if (c.attachedRigidbody == rb || c.transform.IsChildOf(transform)) continue;
                return true;
            }
            return false;
        }

        private void StartClimb(int side)
        {
            isClimbing = true;
            climbSide = side;
            isGrounded = false;
            remainingJumps = Mathf.Min(remainingJumps, maxJumps - 1); // 땅에서 뛰어오른 것과 같게 (벽타기로 공중 점프 횟수가 늘지 않음)
            isSpinning = false;
            if (visualTransform != null) visualTransform.localRotation = Quaternion.identity;
            rb.gravityScale = 0f;
        }

        private void StopClimb(bool reachedTop)
        {
            isClimbing = false;
            rb.gravityScale = baseGravityScale;
            if (reachedTop && rb != null)
            {
                // 꼭대기: 모서리에 걸리지 않게 위·벽 쪽으로 살짝 올려 윗면에 올라서게 한다
                rb.linearVelocity = new Vector2(climbSide * climbTopPush, Mathf.Max(rb.linearVelocity.y, climbTopBoost));
                topAssistTimer = 0.25f;
                topAssistSide = climbSide;
            }
            climbSide = 0;
        }

        /// <summary>몸 옆에 벽이 닿아 있으면 그 방향(-1 왼쪽, +1 오른쪽), 없으면 0</summary>
        private int DetectWall()
        {
            if (bodyCollider == null) return 0;
            var b = bodyCollider.bounds;
            // 발끝·머리 끝은 빼고 몸통 높이만 본다 (바닥·천장 모서리를 벽으로 오인하지 않게)
            float h = b.size.y * 0.7f;
            var size = new Vector2(wallCheckDistance, h);
            float cy = b.center.y;
            for (int s = -1; s <= 1; s += 2)
            {
                if (climbSide != 0 && s != climbSide && isClimbing) continue;
                var center = new Vector2(s < 0 ? b.min.x - wallCheckDistance * 0.5f : b.max.x + wallCheckDistance * 0.5f, cy);
                int n = Physics2D.OverlapBoxNonAlloc(center, size, 0f, wallHits, climbMask);
                for (int i = 0; i < n; i++)
                {
                    var c = wallHits[i];
                    if (c == null || c.isTrigger || c.usedByEffector) continue;
                    if (c.attachedRigidbody == rb || c.transform.IsChildOf(transform)) continue;
                    return s;
                }
            }
            return 0;
        }

        private bool GetClimbInput()
        {
            if (ClimbInputOverride != null) return ClimbInputOverride();
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && (k.wKey.isPressed || k.upArrowKey.isPressed);
#else
            return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
#endif
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
