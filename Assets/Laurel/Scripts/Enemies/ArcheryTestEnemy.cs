using UnityEngine;

namespace Laurel
{
    /// <summary>아처리 테스트 씬용 기본 적(Enemy 상속). 미노타우르 대기 이미지를 표시한다.</summary>
    public class ArcheryTestEnemy : Enemy
    {
        public Sprite idleSprite;
        public Sprite attackReadySprite;
        public Sprite attackSprite;
        public float maxHp = 100f;
        public float attackRange = 3.8f;
        public float attackDamage = 15f;
        public float attackReadyTime = 0.36f;
        public float attackTime = 0.32f;
        public float attackCooldown = 0.8f;
        public Vector2 attackVisualOffset = new Vector2(-0.35f, 0f);
        public Vector2 hitboxOffset = new Vector2(0f, 0.85f);
        public Vector2 hitboxSize = new Vector2(1.6f, 1.7f);
        public Vector2 headHitboxOffset = new Vector2(0f, 2.05f);
        public Vector2 headHitboxSize = new Vector2(1.1f, 0.7f);
        public float headCritMultiplier = 3f;
        public Vector2 hpBarOffset = new Vector2(0f, 2.65f);
        public Vector2 hpBarSize = new Vector2(2.2f, 0.22f);

        private enum State { Idle, AttackReady, Attack }

        private State enemyState = State.Idle;
        private float stateTimer;
        private float cd;
        private BoxCollider2D headCol;
        private SpriteRenderer visualSr;
        private SpriteRenderer hpFillSr, hpTrailSr;
        private float hpTrailRatio = 1f;
        private float hpTrailDelay;
        private bool initialized;

        public override void Knockback(Vector2 v) { }

        public bool IsHeadHit(Transform part, Vector2 worldPoint)
        {
            if (part != null && headCol != null && part == headCol.transform) return true;
            if (headCol != null)
            {
                Bounds b = headCol.bounds;
                return b.Contains(new Vector3(worldPoint.x, worldPoint.y, b.center.z));
            }
            return false;
        }

        private void Start()
        {
            EnsureInitialized();
        }

        protected override void BuildVisual(Color c, bool elite)
        {
            gameObject.layer = Layers.Enemy;
            var box = GetComponent<BoxCollider2D>();
            if (box == null) box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.offset = hitboxOffset;
            box.size = hitboxSize;
            Col = box;
            halfSize = hitboxSize * 0.5f;

            var rb = GetComponent<Rigidbody2D>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var existingHead = transform.Find("HeadHitbox");
            if (existingHead == null)
            {
                var headGo = new GameObject("HeadHitbox");
                headGo.layer = Layers.Enemy;
                headGo.transform.SetParent(transform, false);
                headCol = headGo.AddComponent<BoxCollider2D>();
            }
            else
            {
                headCol = existingHead.GetComponent<BoxCollider2D>() ?? existingHead.gameObject.AddComponent<BoxCollider2D>();
            }
            headCol.isTrigger = true;
            headCol.offset = headHitboxOffset;
            headCol.size = headHitboxSize;

            var visGo = new GameObject("Visual");
            visGo.transform.SetParent(transform, false);
            visGo.transform.localPosition = Vector3.zero;
            visualSr = visGo.AddComponent<SpriteRenderer>();
            visualSr.sprite = idleSprite;
            visualSr.sortingOrder = 10;
            bodySr = visualSr;
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            gameObject.layer = Layers.Enemy;
            gameObject.tag = "Enemy";

            var room = GameRoot.I != null ? GameRoot.I.Room : FindAnyObjectByType<Room>();
            var def = new EnemyDef
            {
                id = "archery_test_enemy",
                name = "미노타우르",
                ai = "dummy",
                hp = maxHp,
                speed = 0,
                damage = 0,
                w = hitboxSize.x,
                h = hitboxSize.y,
                color = "#FFFFFF",
                gold = 0
            };
            def.tint = Color.white;
            Init(room, def, 1f, 1f, false);

            ContactDamage = 0f;
            goldValue = 0;
            usesGravity = true;

            if (room != null && !room.Enemies.Contains(this))
                room.Enemies.Add(this);

            CreateHpBar();
        }

        private void CreateHpBar()
        {
            var oldBar = transform.Find("HpBar");
            if (oldBar != null) Destroy(oldBar.gameObject);

            var barRoot = new GameObject("HpBar").transform;
            barRoot.SetParent(transform, false);
            barRoot.localPosition = hpBarOffset;

            Art.Quad(barRoot, "Bg", Art.Square, new Color(0.08f, 0.08f, 0.08f, 0.85f),
                new Vector2(hpBarSize.x + 0.08f, hpBarSize.y + 0.08f), 15, Vector3.zero);
            hpTrailSr = Art.Quad(barRoot, "TrailFill", Art.Square, new Color(1f, 0.92f, 0.25f, 1f),
                hpBarSize, 16, Vector3.zero);
            hpFillSr = Art.Quad(barRoot, "Fill", Art.Square, new Color(0.92f, 0.22f, 0.22f, 1f),
                hpBarSize, 17, Vector3.zero);
            hpTrailRatio = 1f;
            UpdateHpBar();
        }

        private void UpdateHpBar()
        {
            if (hpFillSr == null) return;
            float ratio = MaxHp > 0f ? Mathf.Clamp01(Hp / MaxHp) : 0f;
            ApplyBarRatio(hpFillSr, ratio);
            if (ratio < hpTrailRatio) hpTrailDelay = 0.25f;
            else hpTrailRatio = ratio;
            ApplyBarRatio(hpTrailSr, hpTrailRatio);
        }

        private void ApplyBarRatio(SpriteRenderer barSr, float ratio)
        {
            if (barSr == null) return;
            float w = hpBarSize.x * Mathf.Clamp01(ratio);
            barSr.transform.localScale = new Vector3(w, hpBarSize.y, 1f);
            barSr.transform.localPosition = new Vector3((w - hpBarSize.x) * 0.5f, 0f, 0f);
        }

        private void UpdateHpBarTrail(float dt)
        {
            if (hpTrailSr == null) return;
            float target = MaxHp > 0f ? Mathf.Clamp01(Hp / MaxHp) : 0f;
            if (hpTrailRatio <= target)
            {
                hpTrailRatio = target;
                ApplyBarRatio(hpTrailSr, hpTrailRatio);
                return;
            }
            if (hpTrailDelay > 0f) { hpTrailDelay -= dt; return; }
            float speed = Mathf.Max(0.28f, (hpTrailRatio - target) * 3.0f);
            hpTrailRatio = Mathf.MoveTowards(hpTrailRatio, target, speed * dt);
            ApplyBarRatio(hpTrailSr, hpTrailRatio);
        }

        protected override void Think(float dt)
        {
            EnsureInitialized();
            velocity.x = 0f;
            UpdateHpBarTrail(dt);
            if (cd > 0f) cd -= dt;

            if (Room == null || Room.IsPlayerHidden || Room.Player == null)
            {
                if (enemyState != State.Idle) SetVisualState(State.Idle);
                return;
            }

            Vector2 playerCenter = (Vector2)Room.Player.transform.position + Vector2.up * 0.9f;
            float dist = Vector2.Distance((Vector2)Center, playerCenter);

            switch (enemyState)
            {
                case State.Idle:
                    float dx = playerCenter.x - Center.x;
                    if (Mathf.Abs(dx) > 0.05f)
                    {
                        facing = dx >= 0f ? 1f : -1f;
                        if (visualSr != null) visualSr.flipX = facing > 0f;
                        if (headCol != null) headCol.offset = new Vector2(headHitboxOffset.x * facing, headHitboxOffset.y);
                    }
                    if (cd <= 0f && dist <= attackRange)
                    {
                        stateTimer = attackReadyTime;
                        SetVisualState(State.AttackReady);
                    }
                    break;

                case State.AttackReady:
                    stateTimer -= dt;
                    if (stateTimer <= 0f)
                    {
                        stateTimer = attackTime;
                        SetVisualState(State.Attack);
                        Vector2 strikePos = (Vector2)Center + new Vector2(facing * 1.4f, 0.1f);
                        Fx.Burst(strikePos, new Color(1f, 0.35f, 0.25f), 10, 6f, 0.16f, 0.28f, 0f);
                        Sfx.Play("swing", 0.7f, 0.8f);
                        if (dist <= attackRange + 0.6f)
                            Room.Player.Hurt(attackDamage, DisplayName);
                    }
                    break;

                case State.Attack:
                    stateTimer -= dt;
                    if (stateTimer <= 0f)
                    {
                        cd = attackCooldown;
                        SetVisualState(State.Idle);
                    }
                    break;
            }
        }

        private void SetVisualState(State next)
        {
            enemyState = next;
            if (visualSr == null) return;
            switch (next)
            {
                case State.Idle:
                    visualSr.sprite = idleSprite;
                    visualSr.transform.localPosition = Vector3.zero;
                    break;
                case State.AttackReady:
                    visualSr.sprite = attackReadySprite != null ? attackReadySprite : idleSprite;
                    visualSr.transform.localPosition = Vector3.zero;
                    break;
                case State.Attack:
                    visualSr.sprite = attackSprite != null ? attackSprite : idleSprite;
                    visualSr.transform.localPosition = new Vector3(attackVisualOffset.x * facing, attackVisualOffset.y, 0f);
                    break;
            }
        }

        protected override void OnDamaged(float amount)
        {
            if (Hp < 1f) Hp = 1f;
            UpdateHpBar();
        }
    }
}
