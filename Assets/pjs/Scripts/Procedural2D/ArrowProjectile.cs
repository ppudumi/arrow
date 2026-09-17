using System.Collections.Generic;
using UnityEngine;

namespace Procedural2D
{
    /// <summary>
    /// 발사된 화살의 비행 궤적, 충돌 고정, 플레이어 사정거리 내 하이라이트 및 G키 자석 회수(Recall)를 처리하는 컴포넌트입니다.
    /// - 화살은 시간 경과로 자동 소멸하지 않으며 영구적으로 필드에 남아 있습니다.
    /// - 플레이어의 회수 반경(Recall Radius) 원 안에 들어오면 밝게 빛나는 펄스로 하이라이트됩니다.
    /// - G키 회수 시 지형, 벽, 적, 콜라이더를 100% 무시하고 플레이어에게 자석처럼 가속 유도 비행합니다.
    /// - 플레이어 품에 도착하면 탄창을 충전하고 경쾌한 캐치 사운드와 함께 소멸합니다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class ArrowProjectile : MonoBehaviour
    {
        /// <summary>현재 씬 내에 발사 또는 드랍되어 존재하는 모든 활성 화살 목록</summary>
        public static readonly List<ArrowProjectile> ActiveArrows = new List<ArrowProjectile>();

        [Header("Shooter Reference")]
        [Tooltip("화살을 발사한 캐릭터의 루트 트랜스폼")]
        public Transform shooterRoot;

        [Header("Recall Parameters (자석 회수 설정)")]
        [Tooltip("현재 플레이어에게 회수 유도 비행 중인지 여부")]
        public bool isRecalling = false;
        public bool HasHit => hasHit;
        public bool CanBeRecalled
        {
            get
            {
                if (isRecalling) return false;
                if (Procedural2DAim.Instance != null)
                {
                    return IsConditionMet(Procedural2DAim.Instance.recallCondition);
                }
                return hasHit || isDropped || (rb != null && (!rb.simulated || rb.linearVelocity.sqrMagnitude < 0.15f));
            }
        }

        /// <summary>
        /// 인스펙터에서 설정한 RecallConditionMode에 부합하는지 판정합니다.
        /// </summary>
        public bool IsConditionMet(RecallConditionMode condition)
        {
            if (isRecalling) return false;
            switch (condition)
            {
                case RecallConditionMode.HitOnly:
                    return hasHit;
                case RecallConditionMode.AllArrows:
                    return true;
                case RecallConditionMode.DroppedOnly:
                    return isDropped;
                case RecallConditionMode.StuckOrStopped:
                default:
                    return hasHit || isDropped || IsLostInSky || (rb != null && (!rb.simulated || rb.linearVelocity.sqrMagnitude < 0.15f));
            }
        }
        [Tooltip("회수 대상 플레이어 트랜스폼")]
        public Transform recallTarget;
        [Tooltip("회수 시작 속도")]
        public float recallSpeed = 25f;
        [Tooltip("회수 가속도")]
        public float recallAcceleration = 65f;
        [Tooltip("플레이어 도달 판정 거리")]
        public float catchRadius = 0.75f;

        [Header("Flight & Gravity (비행 탄도학 및 하늘 회수 설정)")]
        [Tooltip("화살 발사 시 적용될 기본 중력 배율 (자연스러운 포물선 낙하)")]
        public float normalGravityScale = 1.2f;
        [Tooltip("하늘로 날아간 화살이 '빗나감/분실'로 간주되어 회수 가능해지는 체공 시간(초)")]
        public float skyLostTimeout = 1.2f;
        [Tooltip("발사자로부터 이 거리(유닛) 이상 멀어지면 즉시 회수 가능으로 전환")]
        public float skyLostDistance = 20f;

        private float airborneTime = 0f;
        /// <summary>하늘이나 허공으로 빗나가 멀리 날아간 화살인지 여부</summary>
        public bool IsLostInSky => (!hasHit && !isDropped && (airborneTime >= skyLostTimeout || (shooterRoot != null && Vector2.Distance(transform.position, shooterRoot.position) >= skyLostDistance)));

        [Header("Highlight Settings (사정거리 내 하이라이트)")]
        [Tooltip("현재 플레이어의 회수 가능 반경 내에 위치하는지 여부")]
        public bool isInRecallRange = false;
        [Tooltip("회수 가능 시 발광 하이라이트 색상")]
        public Color highlightColor = new Color(0.25f, 1f, 0.92f, 1f);
        public Color normalColor = Color.white;

        private Rigidbody2D rb;
        private Collider2D myCollider;
        private SpriteRenderer spriteRenderer;
        private bool hasHit = false;

        private Vector3 frozenWorldPos;
        private Quaternion frozenWorldRot;

        // 적 몸체에 꽂혀 따라다니기 위한 로컬 오프셋 추적
        private Transform stuckTarget = null;
        private Vector3 stuckLocalPos;
        private Quaternion stuckLocalRot;

        // 드랍(대충 떨구기) 및 텀블링 회전 상태
        private bool isDropped = false;
        private float spinAngularSpeed = 0f;

        private void OnEnable()
        {
            if (!ActiveArrows.Contains(this))
            {
                ActiveArrows.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveArrows.Remove(this);
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            myCollider = GetComponent<Collider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                normalColor = spriteRenderer.color;
            }
        }

        /// <summary>
        /// 우클릭 시 화살을 대충 공중으로 살짝 튕겨 올리며 텀블링 회전과 함께 낙하시킵니다.
        /// </summary>
        public void InitializeDropped(Transform shooter, Vector2 velocity, float spinSpeed)
        {
            shooterRoot = shooter;
            isDropped = true;
            spinAngularSpeed = spinSpeed;

            IgnoreShooterCollisions(shooter);

            if (rb != null)
            {
                rb.gravityScale = 1.9f;
                rb.linearVelocity = velocity;
                rb.angularVelocity = spinSpeed;
            }
        }

        /// <summary>
        /// 화살 발사 시 발사자와의 충돌을 무시하고 속도를 설정합니다.
        /// </summary>
        public void Initialize(Transform shooter, Vector2 velocity)
        {
            shooterRoot = shooter;
            airborneTime = 0f;

            IgnoreShooterCollisions(shooter);

            if (rb != null)
            {
                rb.gravityScale = normalGravityScale;
                rb.linearVelocity = velocity;
                if (velocity.sqrMagnitude > 0.01f)
                {
                    float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.Euler(0f, 0f, angle);
                }
            }
        }

        private void IgnoreShooterCollisions(Transform shooter)
        {
            if (myCollider != null && shooter != null)
            {
                Collider2D[] shooterColliders = shooter.GetComponentsInChildren<Collider2D>();
                foreach (var c in shooterColliders)
                {
                    if (c != null)
                    {
                        Physics2D.IgnoreCollision(myCollider, c, true);
                    }
                }
            }
        }

        /// <summary>
        /// G키 입력 시 호출되어 모든 물리 충돌을 즉시 무시하고 플레이어에게 날아옵니다.
        /// </summary>
        public void StartRecall(Transform targetPlayer)
        {
            if (isRecalling) return;
            if (!CanBeRecalled) return; // 부딪히거나 정지된 화살만 회수 가능!

            isRecalling = true;
            recallTarget = targetPlayer;
            hasHit = false;
            isInRecallRange = false;

            // 모든 물리 충돌 및 시뮬레이션 즉시 중단 (벽/적/장애물 완전 관통)
            if (myCollider != null) myCollider.enabled = false;
            if (rb != null)
            {
                rb.simulated = false;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.color = highlightColor;
            }

            stuckTarget = null;
            // 고정 부모 해제
            transform.SetParent(null);
        }

        private void FixedUpdate()
        {
            if (isRecalling) return; // 회수 중에는 물리 Update 스킵

            if (!hasHit && !isDropped)
            {
                airborneTime += Time.fixedDeltaTime;

                // 하늘 너무 멀리(50유닛 초과) 우주로 날아가는 것 방지 -> 하강 유도
                if (shooterRoot != null && rb != null)
                {
                    float distFromShooter = Vector2.Distance(transform.position, shooterRoot.position);
                    if (distFromShooter > 50f)
                    {
                        rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, new Vector2(rb.linearVelocity.x * 0.4f, -12f), Time.fixedDeltaTime * 2.5f);
                    }
                }
            }

            if (hasHit)
            {
                if (stuckTarget != null && stuckTarget.gameObject.activeInHierarchy)
                {
                    transform.position = stuckTarget.TransformPoint(stuckLocalPos);
                    transform.rotation = stuckTarget.rotation * stuckLocalRot;
                    frozenWorldPos = transform.position;
                    frozenWorldRot = transform.rotation;
                }
                else
                {
                    transform.position = frozenWorldPos;
                    transform.rotation = frozenWorldRot;
                }
                return;
            }

            if (isDropped)
            {
                // 대충 떨어진 화살: 떨어지는 동안 공중에서 빙글빙글 텀블링 회전
                transform.Rotate(0f, 0f, spinAngularSpeed * Time.fixedDeltaTime);
            }
            else
            {
                // 일반 발사된 화살: 날아가는 속도 벡터에 맞춰 화살촉 각도 자동 정렬
                if (rb != null && rb.linearVelocity.sqrMagnitude > 0.1f)
                {
                    float angle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.Euler(0f, 0f, angle);
                }
            }
        }

        private void Update()
        {
            // 1. 회수 비행 처리
            if (isRecalling)
            {
                if (recallTarget == null)
                {
                    Destroy(gameObject);
                    return;
                }

                Vector3 targetPos = recallTarget.position + Vector3.up * 0.2f;
                Vector3 toTarget = targetPos - transform.position;
                float dist = toTarget.magnitude;

                // 플레이어 품에 도착!
                if (dist <= Mathf.Max(catchRadius, 1.0f))
                {
                    if (Procedural2DAim.Instance != null)
                    {
                        Procedural2DAim.Instance.RefillAmmo(1);
                    }
                    if (AudioManager.Instance != null)
                    {
                        AudioManager.Instance.PlayArrowCatch(targetPos);
                    }
                    Destroy(gameObject);
                    return;
                }

                // 가속 비행 (자석 효과 - 거리가 멀면 초고속 유도 복귀)
                recallSpeed += recallAcceleration * Time.deltaTime;
                float effectiveSpeed = Mathf.Max(recallSpeed, dist * 3.2f);
                transform.position = Vector3.MoveTowards(transform.position, targetPos, effectiveSpeed * Time.deltaTime);

                // 날아오는 방향으로 화살촉 정렬
                if (dist > 0.05f)
                {
                    float angle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.Euler(0f, 0f, angle);
                }
                return;
            }

            // 2. 플레이어 회수 사정거리 체크 및 동적 하이라이트 처리
            Procedural2DAim aim = Procedural2DAim.Instance;
            if (aim != null && hasHit) // 어디에 부딪혀 멈춘 화살만 회수 범위 하이라이트
            {
                float distToPlayer = Vector2.Distance(transform.position, aim.transform.position);
                isInRecallRange = (distToPlayer <= aim.recallRadius);
            }
            else
            {
                isInRecallRange = false;
            }

            if (spriteRenderer != null)
            {
                if (isInRecallRange)
                {
                    // 원 안에 들어왔을 때: 선명하고 아름다운 에메랄드 펄스 하이라이트
                    float pulse = Mathf.PingPong(Time.time * 6f, 1f);
                    spriteRenderer.color = Color.Lerp(highlightColor * 0.8f, new Color(1f, 1f, 1f, 1f), pulse * 0.6f);
                }
                else
                {
                    // 원 밖: 기본 화살 색상으로 복구
                    spriteRenderer.color = normalColor;
                }
            }

            // 3. 박힌 상태 고정 및 적 몸체 추적 (Stuck Following)
            if (hasHit)
            {
                if (stuckTarget != null && stuckTarget.gameObject.activeInHierarchy)
                {
                    transform.position = stuckTarget.TransformPoint(stuckLocalPos);
                    transform.rotation = stuckTarget.rotation * stuckLocalRot;
                    frozenWorldPos = transform.position;
                    frozenWorldRot = transform.rotation;
                }
                else
                {
                    transform.position = frozenWorldPos;
                    transform.rotation = frozenWorldRot;
                }
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (hasHit || isRecalling) return;

            // 1. 발사자(플레이어 본인) 및 플레이어의 자식 오브젝트 충돌 완전 무시
            if (collision.gameObject.CompareTag("Player") ||
                (shooterRoot != null && (collision.transform == shooterRoot || collision.transform.IsChildOf(shooterRoot))))
            {
                if (myCollider != null && collision.collider != null)
                {
                    Physics2D.IgnoreCollision(myCollider, collision.collider, true);
                }
                return;
            }

            // 2. 다른 화살과의 충돌 무시 (화살끼리 부딪혀 엉키는 현상 방지)
            if (collision.gameObject.GetComponent<ArrowProjectile>() != null)
            {
                if (myCollider != null && collision.collider != null)
                {
                    Physics2D.IgnoreCollision(myCollider, collision.collider, true);
                }
                return;
            }

            // 3. 벽, 바닥, 장애물, 적 등에 명중 시 그 자리에 그대로 100% 완전 정지!
            hasHit = true;

            // 명중 사운드 효과 (적/타겟 vs 벽/바닥)
            bool isEnemy = (collision.gameObject.tag == "Enemy") ||
                           collision.gameObject.name.IndexOf("Target", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                           collision.gameObject.name.IndexOf("Dummy", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                           collision.gameObject.name.IndexOf("Enemy", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (AudioManager.Instance != null)
            {
                if (isEnemy) AudioManager.Instance.PlayArrowEnemyHit(transform.position);
                else AudioManager.Instance.PlayArrowWallHit(transform.position);
            }

            EnemyDummy dummy = collision.gameObject.GetComponent<EnemyDummy>();
            if (dummy != null)
            {
                dummy.TakeHit(collision.relativeVelocity);
            }

            DemonEyeAI eye = collision.gameObject.GetComponent<DemonEyeAI>();
            if (eye != null)
            {
                eye.TakeHit(collision.relativeVelocity);
            }

            // 고성능 풀링 파티클 이펙트 방출 (Zero-GC Pooling)
            if (ImpactParticleManager.Instance != null)
            {
                Vector2 contactPoint = collision.contactCount > 0 ? collision.GetContact(0).point : (Vector2)transform.position;
                Vector2 contactNormal = collision.contactCount > 0 ? collision.GetContact(0).normal : (Vector2)(-transform.right);
                ImpactParticleManager.Instance.PlayImpact(contactPoint, contactNormal, isEnemy);
            }

            // 물리 충돌체 즉시 비활성화
            if (myCollider != null) myCollider.enabled = false;

            // Rigidbody2D 물리 연산 완전 중단 (튕김/미끄러짐/낙하 완벽 차단)
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.constraints = RigidbodyConstraints2D.FreezeAll;
                rb.simulated = false;
            }

            // 적에게 명중한 경우 몸체에 살짝 박힌 채로 적의 이동/회전을 완벽 추적
            if (isEnemy && collision.transform != null)
            {
                stuckTarget = collision.transform;
                // 화살촉이 살점/장갑에 살짝 박히도록 진행 방향으로 미세 전진
                transform.position += transform.right * 0.12f;
                stuckLocalPos = stuckTarget.InverseTransformPoint(transform.position);
                stuckLocalRot = Quaternion.Inverse(stuckTarget.rotation) * transform.rotation;
                frozenWorldPos = transform.position;
                frozenWorldRot = transform.rotation;
            }
            else
            {
                stuckTarget = null;
                frozenWorldPos = transform.position;
                frozenWorldRot = transform.rotation;
            }
            transform.SetParent(null);
        }
    }
}

