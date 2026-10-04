using System.Collections.Generic;
using UnityEngine;

namespace Archery
{
    /// <summary>
    /// 궁술 테스트용 적/표적. 체력, 실제 받은 피해, 박힌 화살을 관리한다.
    /// 콜라이더는 트리거라서 플레이어 이동을 막지 않는다.
    /// </summary>
    public class ArcheryEnemy : MonoBehaviour
    {
        public enum MovePattern { Static, Patrol, Hover }

        public string displayName = "표적";
        public float maxHp = 60f;
        public MovePattern pattern = MovePattern.Static;
        [Tooltip("Patrol/Hover: 좌우 이동 폭")]
        public float moveRange = 3f;
        public float moveSpeed = 1.5f;
        [Tooltip("Hover: 상하 흔들림 폭")]
        public float bobAmplitude = 0.8f;

        public float Hp { get; private set; }
        public bool IsAlive { get; private set; } = true;
        public float LastDamage { get; private set; }
        public float LastDamageTime { get; private set; } = -99f;
        public string LastDamageSource { get; private set; } = "";
        public float TotalDamage { get; private set; }
        public int HitCount { get; private set; }
        public int KnockbackCount { get; private set; }
        public Collider2D Col { get; private set; }

        private readonly List<ArcheryArrow> stuck = new List<ArcheryArrow>();
        public IReadOnlyList<ArcheryArrow> StuckArrows => stuck;

        private ArcheryCombatContext ctx;
        private SpriteRenderer sr;
        private Color baseColor = Color.white;
        private Vector3 home;
        private Vector2 knockOffset;
        private Vector2 knockVelocity;
        private float lastKnockTime = -99f;
        private float phase;
        private float flashTimer;
        private float respawnTimer;

        public void Setup(ArcheryCombatContext context, Sprite sprite, Vector2 size, Color color)
        {
            ctx = context;
            sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 6;
            baseColor = color;
            sr.color = color;
            if (sprite != null)
            {
                Vector2 spriteSize = sprite.bounds.size;
                transform.localScale = new Vector3(size.x / Mathf.Max(0.01f, spriteSize.x), size.y / Mathf.Max(0.01f, spriteSize.y), 1f);
            }
            var box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            if (sprite != null) box.size = sprite.bounds.size;
            Col = box;
            home = transform.position;
            Hp = maxHp;
            phase = Random.Range(0f, 10f);
        }

        public void RegisterStuck(ArcheryArrow a)
        {
            if (!stuck.Contains(a)) stuck.Add(a);
        }

        public void UnregisterStuck(ArcheryArrow a) => stuck.Remove(a);

        /// <summary>실제 피해 적용. 모든 피해는 이 메서드를 통한다.</summary>
        public void ApplyDamage(float amount, string sourceLabel)
        {
            if (!IsAlive) return;
            Hp -= amount;
            LastDamage = amount;
            LastDamageTime = Time.time;
            LastDamageSource = sourceLabel;
            TotalDamage += amount;
            HitCount++;
            flashTimer = 0.12f;
            ctx?.SpawnDamagePopup(transform.position + Vector3.up * (Col != null ? Col.bounds.extents.y : 0.5f), amount);
            if (Hp <= 0.0001f)
            {
                Hp = 0f;
                Die();
            }
        }

        public void ApplyKnockback(Vector2 direction, float distance)
        {
            if (!IsAlive || distance <= 0f) return;
            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            if (pattern != MovePattern.Hover) dir = new Vector2(Mathf.Sign(dir.x == 0f ? 1f : dir.x), 0f);
            // 감쇠 계수 8 기준: 총 이동거리 ≈ 초기속도 / 8
            knockVelocity += dir * distance * 8f;
            KnockbackCount++;
            lastKnockTime = Time.time;
        }

        private void Die()
        {
            IsAlive = false;
            ctx?.Log($"{displayName} 쓰러짐 → {ctx.Config.enemyRespawnDelay:0.#}초 후 부활");
            var copy = new List<ArcheryArrow>(stuck);
            for (int i = 0; i < copy.Count; i++) copy[i].OnEnemyLost();
            stuck.Clear();
            if (Col != null) Col.enabled = false;
            if (sr != null) sr.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.15f);
            respawnTimer = ctx != null ? ctx.Config.enemyRespawnDelay : 2.5f;
        }

        private void Respawn()
        {
            IsAlive = true;
            Hp = maxHp;
            knockOffset = Vector2.zero;
            knockVelocity = Vector2.zero;
            if (Col != null) Col.enabled = true;
            if (sr != null) sr.color = baseColor;
        }

        /// <summary>테스트 보조: 체력과 기록 초기화</summary>
        public void ResetStats()
        {
            if (!IsAlive) Respawn();
            Hp = maxHp;
            TotalDamage = 0f;
            HitCount = 0;
            LastDamage = 0f;
            LastDamageSource = "";
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (!IsAlive)
            {
                respawnTimer -= dt;
                if (respawnTimer <= 0f) Respawn();
                return;
            }

            phase += dt;
            Vector2 patrolOffset = Vector2.zero;
            switch (pattern)
            {
                case MovePattern.Patrol:
                    patrolOffset.x = Mathf.Sin(phase * moveSpeed / Mathf.Max(0.1f, moveRange)) * moveRange;
                    break;
                case MovePattern.Hover:
                    patrolOffset.x = Mathf.Sin(phase * moveSpeed / Mathf.Max(0.1f, moveRange)) * moveRange;
                    patrolOffset.y = Mathf.Sin(phase * 2.1f) * bobAmplitude;
                    break;
            }

            // 넉백: 감쇠하며 밀려난 뒤, 일정 시간 후 제자리로 천천히 복귀
            knockOffset += knockVelocity * dt;
            knockVelocity = Vector2.Lerp(knockVelocity, Vector2.zero, 1f - Mathf.Exp(-8f * dt));
            float recoverDelay = ctx != null ? ctx.Config.enemyKnockbackRecoverDelay : 1.2f;
            if (Time.time - lastKnockTime > recoverDelay)
            {
                knockOffset = Vector2.MoveTowards(knockOffset, Vector2.zero, 2.5f * dt);
            }

            transform.position = home + (Vector3)(patrolOffset + knockOffset);

            if (sr != null)
            {
                if (flashTimer > 0f)
                {
                    flashTimer -= dt;
                    sr.color = Color.Lerp(baseColor, new Color(1f, 0.35f, 0.3f, 1f), 0.75f);
                }
                else sr.color = baseColor;
            }
        }

        /// <summary>플레이어 위치에서 이 적 콜라이더 가장자리까지 거리</summary>
        public float DistanceFrom(Vector2 p)
        {
            if (Col == null || !Col.enabled) return Vector2.Distance(p, transform.position);
            Vector2 c = Col.ClosestPoint(p);
            return Vector2.Distance(p, c);
        }
    }
}
