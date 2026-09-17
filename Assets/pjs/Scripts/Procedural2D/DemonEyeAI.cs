using System.Collections;
using UnityEngine;

namespace Procedural2D
{
    /// <summary>
    /// 테라리아의 악마의 눈(Demon Eye) 및 떠돌이 눈(Wandering Eye) 비행 패턴을 재현한 AI 컨트롤러입니다.
    /// - 플레이어 주변 정현파 궤도 부유 및 배회 (Hover Orbiting)
    /// - 조준 전율(Tremble Aim) 후 급강하 돌진(Swoop Dash) 및 유턴(Arc Turn)
    /// - 속도에 따른 오가닉 스쿼시 앤 스트레치(Squash & Stretch)
    /// - 화살 피격 넉백, 피격 플래시, 체력 소진 시 폭발 및 리스폰
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D))]
    public class DemonEyeAI : MonoBehaviour
    {
        public enum EyeState
        {
            HoverOrbit,
            PrepareCharge,
            ChargeDash,
            OvershootTurn,
            Dying
        }

        [Header("Target & References")]
        public Transform targetPlayer;

        [Header("Stats")]
        public int maxHp = 6;
        public int currentHp = 6;
        public float baseSpeed = 4.5f;
        public float dashSpeed = 15f;
        public float orbitRadius = 6.5f;

        [Header("State Timing")]
        public EyeState currentState = EyeState.HoverOrbit;

        private SpriteRenderer spriteRenderer;
        private CircleCollider2D circleCol;
        private Color defaultColor = Color.white;

        private Vector2 velocity = Vector2.zero;
        private Vector2 chargeDirection = Vector2.right;
        private float stateTimer = 0f;
        private float orbitAngle = 0f;
        private float orbitDir = 1f;

        private Coroutine hitRoutine;
        private Vector3 initialScale = Vector3.one;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            circleCol = GetComponent<CircleCollider2D>();
            if (spriteRenderer != null) defaultColor = spriteRenderer.color;
            initialScale = transform.localScale;

            // 콜라이더 설정
            circleCol.radius = 0.95f;
            circleCol.isTrigger = false;
            gameObject.tag = "Enemy";
        }

        private void Start()
        {
            if (targetPlayer == null)
            {
                var playerGo = GameObject.FindWithTag("Player");
                if (playerGo == null) playerGo = GameObject.Find("Player_Character");
                if (playerGo != null) targetPlayer = playerGo.transform;
            }

            orbitAngle = Random.Range(0f, Mathf.PI * 2f);
            orbitDir = Random.value > 0.5f ? 1f : -1f;
            stateTimer = Random.Range(3.5f, 6.0f);
        }

        private void Update()
        {
            if (currentState == EyeState.Dying) return;

            if (targetPlayer == null)
            {
                var playerGo = GameObject.FindWithTag("Player");
                if (playerGo == null) playerGo = GameObject.Find("Player_Character");
                if (playerGo != null) targetPlayer = playerGo.transform;
                if (targetPlayer == null) return;
            }

            stateTimer -= Time.deltaTime;

            switch (currentState)
            {
                case EyeState.HoverOrbit:
                    UpdateHoverOrbit();
                    break;
                case EyeState.PrepareCharge:
                    UpdatePrepareCharge();
                    break;
                case EyeState.ChargeDash:
                    UpdateChargeDash();
                    break;
                case EyeState.OvershootTurn:
                    UpdateOvershootTurn();
                    break;
            }

            // 부드러운 위치 이동
            transform.position += (Vector3)(velocity * Time.deltaTime);

            // 유기적 스쿼시 & 스트레치 (속도가 빠를수록 진행 방향으로 길어짐)
            ApplySquashAndStretch();
        }

        private void UpdateHoverOrbit()
        {
            // 플레이어 주위를 부드러운 타원형 궤도로 유영
            orbitAngle += orbitDir * (0.85f * Time.deltaTime);
            float bobY = Mathf.Sin(Time.time * 2.2f) * 1.1f;

            Vector2 desiredPos = (Vector2)targetPlayer.position + new Vector2(
                Mathf.Cos(orbitAngle) * orbitRadius,
                Mathf.Sin(orbitAngle) * (orbitRadius * 0.65f) + 1.8f + bobY
            );

            Vector2 toDesired = desiredPos - (Vector2)transform.position;
            velocity = Vector2.Lerp(velocity, toDesired * baseSpeed, Time.deltaTime * 3.2f);

            // 동공이 플레이어를 바라보도록 부드럽게 회전
            Vector2 toPlayer = (Vector2)targetPlayer.position - (Vector2)transform.position;
            float targetAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, 0f, targetAngle), Time.deltaTime * 6f);

            // 배회 시간 종료 시 돌진 준비 단계로 전이
            if (stateTimer <= 0f)
            {
                currentState = EyeState.PrepareCharge;
                stateTimer = 0.65f; // 부르르 떠는 시간
                velocity = Vector2.zero;
            }
        }

        private void UpdatePrepareCharge()
        {
            // 플레이어 조준 락온 및 부르르 진동 (Tremble)
            Vector2 toPlayer = (Vector2)targetPlayer.position - (Vector2)transform.position;
            chargeDirection = toPlayer.normalized;
            float targetAngle = Mathf.Atan2(chargeDirection.y, chargeDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, targetAngle);

            // 진동 셰이크
            Vector2 shake = Random.insideUnitCircle * 0.12f;
            transform.position += (Vector3)(shake * Time.deltaTime * 8f);

            if (stateTimer <= 0f)
            {
                currentState = EyeState.ChargeDash;
                stateTimer = 1.1f; // 돌진 지속 시간
                velocity = chargeDirection * dashSpeed;
            }
        }

        private void UpdateChargeDash()
        {
            // 고속 돌진 중 (약간의 타겟 유도 곡선)
            Vector2 toPlayer = (Vector2)targetPlayer.position - (Vector2)transform.position;
            velocity = Vector2.Lerp(velocity, chargeDirection * dashSpeed, Time.deltaTime * 4f);

            float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            if (stateTimer <= 0f)
            {
                currentState = EyeState.OvershootTurn;
                stateTimer = 1.4f;
                // 다음 궤도 방향 전환
                orbitDir = -orbitDir;
            }
        }

        private void UpdateOvershootTurn()
        {
            // 오버슈트 후 크게 반원을 그리며 감속 및 유턴
            velocity = Vector2.Lerp(velocity, velocity.normalized * (baseSpeed * 1.5f), Time.deltaTime * 2.5f);

            Vector2 toPlayer = (Vector2)targetPlayer.position - (Vector2)transform.position;
            float targetAngle = Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, 0f, targetAngle), Time.deltaTime * 3.5f);

            if (stateTimer <= 0f)
            {
                currentState = EyeState.HoverOrbit;
                stateTimer = Random.Range(4.0f, 7.0f);
            }
        }

        private void ApplySquashAndStretch()
        {
            float speedRatio = Mathf.Clamp01(velocity.magnitude / dashSpeed);
            float stretchX = 1f + speedRatio * 0.22f;
            float stretchY = 1f - speedRatio * 0.15f;
            transform.localScale = new Vector3(initialScale.x * stretchX, initialScale.y * stretchY, 1f);
        }

        public void TakeHit(Vector2 impactVelocity)
        {
            if (currentState == EyeState.Dying) return;

            currentHp--;

            // 넉백 (화살 충격 방향)
            velocity = impactVelocity.normalized * 4.5f;

            if (hitRoutine != null) StopCoroutine(hitRoutine);
            hitRoutine = StartCoroutine(HitFlashRoutine());

            if (currentHp <= 0)
            {
                Die();
            }
        }

        private IEnumerator HitFlashRoutine()
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(2.0f, 1.3f, 1.3f, 1f);
            }

            yield return new WaitForSeconds(0.12f);

            if (spriteRenderer != null)
            {
                spriteRenderer.color = defaultColor;
            }
            hitRoutine = null;
        }

        private void Die()
        {
            currentState = EyeState.Dying;
            velocity = Vector2.zero;

            // 1. 박혀있던 자식 화살들을 월드에 떨굼 (회수 가능하도록)
            ArrowProjectile[] childArrows = GetComponentsInChildren<ArrowProjectile>();
            for (int i = 0; i < childArrows.Length; i++)
            {
                childArrows[i].transform.SetParent(null);
            }

            // 2. 피격 파티클 대량 방출
            if (ImpactParticleManager.Instance != null)
            {
                ImpactParticleManager.Instance.PlayImpact(transform.position, Vector2.up, true);
                ImpactParticleManager.Instance.PlayImpact(transform.position, Vector2.left, true);
                ImpactParticleManager.Instance.PlayImpact(transform.position, Vector2.right, true);
            }

            StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            // 페이드아웃 및 충돌 비활성화
            circleCol.enabled = false;
            float t = 0f;
            while (t < 0.4f)
            {
                t += Time.deltaTime;
                float alpha = Mathf.Lerp(1f, 0f, t / 0.4f);
                if (spriteRenderer != null) spriteRenderer.color = new Color(1f, 1f, 1f, alpha);
                transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, t / 0.4f);
                yield return null;
            }

            // 화면 밖에서 4.5초 대기
            yield return new WaitForSeconds(4.5f);

            // 플레이어 머리 위 대각선 상공에서 리스폰
            if (targetPlayer != null)
            {
                transform.position = targetPlayer.position + new Vector3(Random.Range(-12f, 12f), Random.Range(8f, 12f), 0f);
            }
            else
            {
                transform.position = new Vector3(0f, 10f, 0f);
            }

            currentHp = maxHp;
            transform.localScale = initialScale;
            if (spriteRenderer != null) spriteRenderer.color = defaultColor;
            circleCol.enabled = true;
            currentState = EyeState.HoverOrbit;
            stateTimer = Random.Range(3.5f, 6.0f);
        }
    }
}
