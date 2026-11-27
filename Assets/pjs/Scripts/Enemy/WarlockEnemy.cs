using UnityEngine;
using Procedural2D;

namespace Laurel
{
    /// <summary>
    /// 워록(Warlock) 적:
    /// - 원거리에서 플레이어 위쪽을 천천히 포물선으로 가로지르는 영혼(ghost) 투사체를 발사한다.
    /// - ghost 비행 중 뒤에 trail0~2 연기 잔상을 남긴다.
    /// - 피격 시 hit_fx 이펙트가 터지며, 사망 시 flesh0~6 파편으로 분해된다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class WarlockEnemy : Enemy, ILaserTarget
    {
        [Header("Sprites (Assets/pjs/Sprites/Enemy/warlock.png)")]
        public Sprite idleMoveSprite;
        public Sprite castingSprite;
        public Sprite ghostSprite;
        public Sprite[] trailSprites;
        public Sprite hitFxSprite;
        public Sprite[] fleshSprites;

        [Header("Visual & Hitbox")]
        public float visualScale = 1.0f;
        public int sortingOrder = 10;
        public Vector2 hitboxSize = new Vector2(0.95f, 1.75f);
        public Vector2 hitboxOffset = new Vector2(0f, 0.88f);
        public Vector2 headHitboxSize = new Vector2(0.6f, 0.55f);
        public Vector2 headHitboxOffset = new Vector2(0f, 1.48f);
        public float headCritMultiplier = 2.0f;

        [Header("HP Bar")]
        public Vector2 hpBarOffset = new Vector2(0f, 2.25f);
        public Vector2 hpBarSize = new Vector2(1.35f, 0.14f);

        [Header("Combat & Movement")]
        public float maxHp = 42.5f;
        public int dropGold = 18;
        public int dropExp = 28;
        public LayerMask groundLayer = 1 << 8;
        public float aggroRange = 28f;
        public float retreatRange = 4.5f;
        public float moveSpeed = 3.2f;
        public float castCooldown = 1.9f;
        public float castReadyTime = 0.45f;
        public float castFollowTime = 0.25f;
        public float ghostDamage = 14f;

        private enum State { Idle, Retreat, CastReady, CastFollow }

        private State warlockState = State.Idle;
        private float stateTimer;
        private float cooldownTimer = 0.8f;

        private BoxCollider2D col;
        private BoxCollider2D headCol;
        private Transform visual;
        private SpriteRenderer sr;
        private SpriteRenderer hpFillSr, hpTrailSr;
        private float hpTrailRatio = 1f;
        private float hpTrailDelay;
        private float flashT = 1f;
        private bool pendingNextHitCritical;
        private Vector2? pendingHitPoint;
        private Vector2 lastHitDirection = Vector2.right;

        public void MarkHitPoint(Vector2 point, Vector2 dir)
        {
            pendingHitPoint = point;
            if (dir.sqrMagnitude > 0.001f) lastHitDirection = dir.normalized;
        }

        private Transform player;
        private PlayerAvatar playerAvatar;
        private ProceduralCharacterController playerCtrl;

        private static readonly float[] Blink = { 0.045f, 0.035f, 0.035f };

        private void Awake()
        {
            gameObject.layer = Layers.Enemy;
            gravity = 36f;
            facing = -1;

            col = GetComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.offset = hitboxOffset;
            col.size = hitboxSize;
            Col = col;
            halfSize = hitboxSize * 0.5f;
            MaxHp = maxHp;
            Hp = maxHp;
            DisplayName = "워록";
            baseColor = Color.white;

            var existingHead = transform.Find("HeadHitbox");
            if (existingHead != null && existingHead.TryGetComponent(out BoxCollider2D existingHeadCol))
            {
                headCol = existingHeadCol;
                headCol.gameObject.layer = Layers.Enemy;
                headCol.isTrigger = true;
            }
            else
            {
                var headGo = new GameObject("HeadHitbox");
                headGo.layer = Layers.Enemy;
                headGo.transform.SetParent(transform, false);
                headCol = headGo.AddComponent<BoxCollider2D>();
                headCol.isTrigger = true;
                headCol.offset = headHitboxOffset;
                headCol.size = headHitboxSize;
            }

            var old = transform.Find("Visual");
            if (old != null) Destroy(old.gameObject);

            var visGo = new GameObject("Visual");
            visual = visGo.transform;
            visual.SetParent(transform, false);
            visual.localScale = new Vector3(visualScale, visualScale, 1f);

            sr = visGo.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;
            sr.sprite = idleMoveSprite;

            BuildHpBar();
        }

        protected override void BuildVisual(Color c, bool elite) { }

        private void BuildHpBar()
        {
            var oldBar = transform.Find("HpBar");
            if (oldBar != null) Destroy(oldBar.gameObject);

            var barRoot = new GameObject("HpBar").transform;
            barRoot.SetParent(transform, false);
            barRoot.localPosition = hpBarOffset;

            Art.Quad(barRoot, "Bg", Art.Square, new Color(0.08f, 0.08f, 0.08f, 0.85f),
                new Vector2(hpBarSize.x + 0.08f, hpBarSize.y + 0.08f), sortingOrder + 5, Vector3.zero);
            hpTrailSr = Art.Quad(barRoot, "TrailFill", Art.Square, new Color(1f, 0.92f, 0.25f, 1f),
                hpBarSize, sortingOrder + 6, Vector3.zero);
            hpFillSr = Art.Quad(barRoot, "Fill", Art.Square, new Color(0.92f, 0.22f, 0.22f, 1f),
                hpBarSize, sortingOrder + 7, Vector3.zero);
            hpTrailRatio = 1f;
            UpdateHpBar();
        }

        private void UpdateHpBar()
        {
            if (hpFillSr == null) return;
            float ratio = maxHp > 0f ? Mathf.Clamp01(Hp / maxHp) : 0f;
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
            float target = maxHp > 0f ? Mathf.Clamp01(Hp / maxHp) : 0f;
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

        private void Start()
        {
            FindPlayer();
            SnapToGround();
            EnsureRegisteredInRoom();
            EnterState(State.Idle);
        }

        private void EnsureRegisteredInRoom()
        {
            var room = Room != null ? Room : FindAnyObjectByType<Room>();
            if (room != null && !room.Enemies.Contains(this))
                room.Enemies.Add(this);
            EnsureEnemyInitialized();
        }

        private void EnsureEnemyInitialized()
        {
            if (Status != null) return;
            var room = Room != null ? Room : FindAnyObjectByType<Room>();
            var def = new EnemyDef
            {
                id = "warlock",
                name = "워록",
                ai = "warlock",
                hp = maxHp,
                speed = moveSpeed,
                damage = ghostDamage,
                w = hitboxSize.x,
                h = hitboxSize.y,
                color = "#6E3AA8",
                gold = 0
            };
            Init(room, def, 1f, 1f, false);
            ContactDamage = 0f;
            goldValue = dropGold;
            expValue = dropExp;
        }

        private void FindPlayer()
        {
            if (player != null) return;
            if (GameRoot.I != null && GameRoot.I.PlayerGO != null)
            {
                player = GameRoot.I.PlayerGO.transform;
                playerAvatar = GameRoot.I.Avatar;
                playerCtrl = player.GetComponent<ProceduralCharacterController>();
                return;
            }
            var ctrl = FindFirstObjectByType<ProceduralCharacterController>();
            if (ctrl != null)
            {
                player = ctrl.transform;
                playerCtrl = ctrl;
                playerAvatar = ctrl.GetComponent<PlayerAvatar>();
            }
        }

        private void FixedUpdate()
        {
            if (dead) return;
            if (player == null) FindPlayer();

            float dt = Time.fixedDeltaTime;
            if (Status != null)
            {
                Status.Tick(dt);
                if (dead) return;
            }

            UpdateFlash(dt);
            UpdateHpBarTrail(dt);

            bool hidden = (Room != null && Room.IsPlayerHidden) || (GameRoot.I != null && GameRoot.I.Room != null && GameRoot.I.Room.IsPlayerHidden);
            if (hidden)
            {
                if (warlockState != State.Idle) EnterState(State.Idle);
                velocity.x = Mathf.MoveTowards(velocity.x, 0f, 20f * dt);
            }
            else if (Status == null || Status.stunT <= 0f)
            {
                UpdateStateMachine(dt);
            }

            MoveAndCollide(dt);
        }

        private void UpdateStateMachine(float dt)
        {
            float mul = Status != null ? Status.SpeedMul : 1f;
            stateTimer -= dt * mul;
            if (cooldownTimer > 0f) cooldownTimer -= dt * mul;

            switch (warlockState)
            {
                case State.Idle:
                    velocity.x = Mathf.MoveTowards(velocity.x, 0f, 20f * dt);
                    FacePlayer();
                    if (player != null)
                    {
                        float dx = Mathf.Abs(player.position.x - transform.position.x);
                        if (dx <= aggroRange)
                        {
                            if (dx < retreatRange && stateTimer <= 0f)
                            {
                                EnterState(State.Retreat);
                            }
                            else if (cooldownTimer <= 0f)
                            {
                                EnterState(State.CastReady);
                            }
                        }
                    }
                    break;

                case State.Retreat:
                    FacePlayer();
                    velocity.x = -facing * moveSpeed * mul;
                    if (stateTimer <= 0f || player == null || Mathf.Abs(player.position.x - transform.position.x) >= retreatRange + 2.2f)
                    {
                        velocity.x = 0f;
                        EnterState(cooldownTimer <= 0f ? State.CastReady : State.Idle);
                    }
                    break;

                case State.CastReady:
                    velocity.x = 0f;
                    FacePlayer();
                    if (stateTimer <= 0f)
                    {
                        SpawnGhostProjectile();
                        EnterState(State.CastFollow);
                    }
                    break;

                case State.CastFollow:
                    velocity.x = 0f;
                    if (stateTimer <= 0f)
                    {
                        cooldownTimer = castCooldown;
                        EnterState(State.Idle);
                    }
                    break;
            }
        }

        private void EnterState(State next)
        {
            warlockState = next;
            bool isCast = next == State.CastReady || next == State.CastFollow;
            if (sr != null)
                sr.sprite = isCast && castingSprite != null ? castingSprite : idleMoveSprite;

            if (visual != null)
            {
                // casting 스프라이트(62x110)가 idleandmove(64x98)보다 12px 높으므로 피벗 중앙 보정
                float yOff = isCast ? 0.06f * visualScale : 0f;
                visual.localPosition = new Vector3(0f, hitboxOffset.y + yOff, 0f);
            }

            switch (next)
            {
                case State.Idle: stateTimer = 0.2f; break;
                case State.Retreat: stateTimer = 0.65f; break;
                case State.CastReady:
                    stateTimer = castReadyTime;
                    Fx.Burst(transform.position + new Vector3(facing * 0.4f, 1.55f, 0f), new Color(0.65f, 0.35f, 0.95f), 6, 3f, 0.14f, 0.28f);
                    break;
                case State.CastFollow: stateTimer = castFollowTime; break;
            }
        }

        private void SpawnGhostProjectile()
        {
            Vector2 origin = (Vector2)transform.position + new Vector2(facing * 0.55f, 1.65f);
            Vector2 initialVel = new Vector2(facing * 2.4f, 5.0f);

            var go = new GameObject("WarlockGhost");
            go.transform.position = new Vector3(origin.x, origin.y, -0.05f);
            var proj = go.AddComponent<WarlockGhostProjectile>();
            float bodyScale = Mathf.Abs(transform.localScale.x) * visualScale;
            proj.Init(origin, initialVel, ghostDamage, bodyScale, ghostSprite, trailSprites, hitFxSprite, fleshSprites, groundLayer, sortingOrder, player, playerAvatar, playerCtrl, this);
            Sfx.Play("swing", 0.55f, 0.75f);
        }

        private void FacePlayer()
        {
            if (player == null) return;
            float dx = player.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.1f)
            {
                facing = dx >= 0f ? 1 : -1;
                if (sr != null) sr.flipX = facing > 0; // 원본 스프라이트가 왼쪽을 바라보는 기준
            }
        }

        private void MoveAndCollide(float dt)
        {
            if (!grounded) velocity.y -= gravity * dt;
            Vector2 pos = transform.position;

            float moveX = velocity.x * dt;
            if (Mathf.Abs(moveX) > 0.0001f)
            {
                float dirX = Mathf.Sign(moveX);
                Vector2 center = pos + hitboxOffset;
                var wallHit = Physics2D.BoxCast(center, new Vector2(hitboxSize.x * 0.9f, hitboxSize.y * 0.8f), 0f,
                    new Vector2(dirX, 0f), Mathf.Abs(moveX) + 0.05f, groundLayer);
                bool hasFloorAhead = !grounded || Physics2D.Raycast(
                    new Vector2(pos.x + dirX * (hitboxSize.x * 0.5f + 0.25f), pos.y + 0.5f),
                    Vector2.down, 1.1f, groundLayer).collider != null;

                if ((wallHit.collider != null && !wallHit.collider.isTrigger) || !hasFloorAhead) velocity.x = 0f;
                else pos.x += moveX;
            }

            float moveY = velocity.y * dt;
            Vector2 footOrigin = new Vector2(pos.x, pos.y + 0.2f);
            float castDist = Mathf.Max(0.25f, -moveY + 0.25f);
            if (velocity.y <= 0f)
            {
                var groundHit = Physics2D.BoxCast(footOrigin, new Vector2(hitboxSize.x * 0.7f, 0.1f), 0f,
                    Vector2.down, castDist, groundLayer);
                if (groundHit.collider != null && !groundHit.collider.isTrigger && groundHit.point.y <= pos.y + 0.25f)
                {
                    pos.y = groundHit.point.y;
                    velocity.y = 0f;
                    grounded = true;
                }
                else
                {
                    pos.y += moveY;
                    grounded = false;
                }
            }
            else
            {
                pos.y += moveY;
                grounded = false;
            }
            transform.position = pos;
        }

        private void SnapToGround()
        {
            var hit = Physics2D.Raycast((Vector2)transform.position + Vector2.up * 1.5f, Vector2.down, 12f, groundLayer);
            if (hit.collider != null && !hit.collider.isTrigger)
            {
                transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
                grounded = true;
            }
        }

        public bool IsHeadHit(Transform part, Vector2 worldPoint) => (part != null && part == headCol?.transform) || IsHeadShot(worldPoint);
        public void MarkNextHitCritical() => pendingNextHitCritical = true;

        private bool IsHeadShot(Vector2 worldPoint)
        {
            if (headCol != null)
            {
                Bounds b = headCol.bounds;
                b.Expand(0.1f);
                if (b.Contains(new Vector3(worldPoint.x, worldPoint.y, b.center.z))) return true;
            }
            Vector2 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x - headHitboxOffset.x * facing) <= headHitboxSize.x * 0.5f + 0.05f
                && Mathf.Abs(local.y - headHitboxOffset.y) <= headHitboxSize.y * 0.5f + 0.05f;
        }

        private void SpawnHitFx(Vector2 point)
        {
            if (hitFxSprite == null) return;
            var go = new GameObject("Warlock_HitFx");
            go.transform.position = new Vector3(point.x, point.y, -0.08f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-22f, 22f));
            var hsr = go.AddComponent<SpriteRenderer>();
            hsr.sprite = hitFxSprite;
            hsr.sortingOrder = sortingOrder + 12;
            float bodyScale = Mathf.Abs(transform.localScale.x) * visualScale;
            go.AddComponent<WarlockHitFx>().Init(hsr, bodyScale, 0.22f);
        }

        public override void TakeDamage(float amount, string label, Color color, bool isArrowHit)
        {
            if (dead || amount <= 0f) return;
            EnsureEnemyInitialized();

            bool crit = pendingNextHitCritical;
            pendingNextHitCritical = false;

            if (Status != null && Status.calamity > 0)
            {
                amount *= 2f;
                Status.calamity--;
                Fx.Text(Center + Vector3.up * 0.8f, "재앙!", new Color(0.7f, 0.3f, 1f), 20, 0.6f);
            }
            if (Status != null) amount *= Status.DamageTakenMul;

            Hp = Mathf.Max(0f, Hp - amount);
            UpdateHpBar();
            flashT = 0f;
            Vector2 fxPos = pendingHitPoint ?? (Vector2)(Center + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.2f, 0.2f), 0f));
            pendingHitPoint = null;
            SpawnHitFx(fxPos);

            int dmgInt = Mathf.Max(1, Mathf.RoundToInt(amount));
            DamagePopup.Spawn(transform.position + Vector3.up * 1.4f, dmgInt, crit, crit ? new Color(1f, 0.88f, 0.25f, 1f) : color);

            if (isArrowHit && Status != null && Status.resonanceDamage > 0f && Room != null)
            {
                float rd = Status.resonanceDamage;
                Status.resonanceDamage = 0f;
                Fx.Circle(Center, 2.6f, new Color(0.5f, 0.9f, 1f), 0.08f, 0.3f);
                Room.Explode(Center, 2.6f, rd, null, new Color(0.5f, 0.9f, 1f), "공명");
            }
            if (Status != null && Status.midasT > 0f) DropMidasGold(false);
            OnDamaged(amount);
            if (Hp <= 0f) Die();
        }

        public void TakeLaserHit(int damage, bool critical, Vector2 point, Vector2 dir)
        {
            if (dead) return;
            lastHitDirection = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
            if (IsHeadShot(point))
            {
                damage = Mathf.Max(1, Mathf.RoundToInt(damage * headCritMultiplier));
                critical = true;
            }
            Hp = Mathf.Max(0f, Hp - damage);
            UpdateHpBar();
            flashT = 0f;
            SpawnHitFx(point);
            DamagePopup.Spawn(point + new Vector2(Random.Range(-0.2f, 0.2f), 0.3f), damage, critical);
            Sfx.Play("hit", critical ? 0.95f : 0.7f, critical ? 0.8f : Random.Range(0.95f, 1.15f));
            if (Hp <= 0f) Die();
        }

        private void UpdateFlash(float dt)
        {
            if (sr == null) return;
            if (flashT >= 1f) { sr.color = Color.white; return; }
            flashT += dt;
            float t = flashT;
            for (int i = 0; i < Blink.Length; i++)
            {
                if (t < Blink[i]) { sr.color = (i % 2 == 0) ? new Color(1f, 0.35f, 0.55f, 1f) : Color.white; return; }
                t -= Blink[i];
            }
            flashT = 1f;
            sr.color = Color.white;
        }

        protected override void Die()
        {
            if (dead) return;
            SpawnFleshFragments();
            Fx.Burst(Center, new Color(0.62f, 0.32f, 0.95f), 16, 6f, 0.2f, 0.45f);
            base.Die();
        }

        private void SpawnFleshFragments()
        {
            if (fleshSprites == null || fleshSprites.Length == 0) return;
            Vector2 center = (Vector2)transform.position + hitboxOffset;
            for (int i = 0; i < fleshSprites.Length; i++)
            {
                var sp = fleshSprites[i];
                if (sp == null) continue;
                var go = new GameObject($"WarlockFlesh_{i}");
                go.transform.position = new Vector3(center.x + Random.Range(-0.35f, 0.35f), center.y + Random.Range(-0.2f, 0.4f), -0.02f);
                go.transform.localScale = new Vector3(visualScale, visualScale, 1f);
                var fsr = go.AddComponent<SpriteRenderer>();
                fsr.sprite = sp;
                fsr.sortingOrder = sortingOrder + 1;
                var frag = go.AddComponent<WarlockFleshFragment>();
                Vector2 vel = new Vector2(lastHitDirection.x * Random.Range(2f, 6f) + Random.Range(-3.5f, 3.5f), Random.Range(5f, 11f));
                frag.Init(fsr, vel, Random.Range(-380f, 380f), groundLayer);
            }
        }
    }

    /// <summary>워록이 발사하는 느린 유도 영혼(ghost) 투사체 (화살로 격추 가능)</summary>
    public class WarlockGhostProjectile : MonoBehaviour, ILaserTarget
    {
        private Vector2 velocity;
        private float speed = 5.6f;
        private float turnRateDeg = 115f;
        private float life = 4.8f;
        private float damage;
        private float trailTimer;
        private bool destroyed;
        private Sprite[] trailSprites;
        private Sprite hitFxSprite;
        private Sprite[] fleshSprites;
        private LayerMask groundLayer;
        private int sortingOrder = 10;
        private SpriteRenderer sr;
        private Transform player;
        private PlayerAvatar playerAvatar;
        private ProceduralCharacterController playerCtrl;
        private WarlockEnemy ownerWarlock;

        public void Init(Vector2 start, Vector2 initialVel, float dmg, float scale, Sprite ghostSp, Sprite[] trails,
            Sprite hitFxSp, Sprite[] fleshSps, LayerMask ground, int sortOrder,
            Transform pTf, PlayerAvatar pAv, ProceduralCharacterController pCtrl, WarlockEnemy owner = null)
        {
            velocity = initialVel;
            damage = dmg;
            trailSprites = trails;
            hitFxSprite = hitFxSp;
            fleshSprites = fleshSps;
            groundLayer = ground;
            sortingOrder = sortOrder;
            player = pTf;
            playerAvatar = pAv;
            playerCtrl = pCtrl;
            ownerWarlock = owner;

            gameObject.layer = Layers.Enemy;
            transform.position = new Vector3(start.x, start.y, -0.05f);
            transform.localScale = Vector3.one * scale;

            var col = gameObject.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.25f;

            sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = ghostSp != null ? ghostSp : Art.Circle;
            sr.sortingOrder = 22;
        }

        public bool TryGetReflectTarget(out Vector2 targetPos)
        {
            if (ownerWarlock != null && ownerWarlock.IsAlive)
            {
                targetPos = ownerWarlock.Center;
                return true;
            }
            var room = GameRoot.I != null ? GameRoot.I.Room : null;
            if (room != null)
            {
                float bestDist = float.MaxValue;
                WarlockEnemy best = null;
                foreach (var e in room.Enemies)
                {
                    if (e is WarlockEnemy w && w.IsAlive)
                    {
                        float d = Vector2.Distance(transform.position, w.Center);
                        if (d < bestDist) { bestDist = d; best = w; }
                    }
                }
                if (best != null) { targetPos = best.Center; return true; }
            }
            targetPos = default;
            return false;
        }

        public void TakeLaserHit(int dmg, bool critical, Vector2 point, Vector2 dir)
        {
            if (destroyed) return;
            destroyed = true;

            if (hitFxSprite != null)
            {
                var fxGo = new GameObject("WarlockGhost_HitFx");
                fxGo.transform.position = new Vector3(point.x, point.y, -0.08f);
                fxGo.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-22f, 22f));
                var hsr = fxGo.AddComponent<SpriteRenderer>();
                hsr.sprite = hitFxSprite;
                hsr.sortingOrder = sortingOrder + 12;
                fxGo.AddComponent<WarlockHitFx>().Init(hsr, transform.localScale.x, 0.22f);
            }

            if (fleshSprites != null && fleshSprites.Length > 0)
            {
                int startIdx = fleshSprites.Length >= 4 ? 3 : 0;
                for (int i = startIdx; i < fleshSprites.Length; i++)
                {
                    var sp = fleshSprites[i];
                    if (sp == null) continue;
                    var fGo = new GameObject($"GhostFlesh_{i}");
                    fGo.transform.position = new Vector3(transform.position.x + Random.Range(-0.15f, 0.15f), transform.position.y + Random.Range(-0.15f, 0.15f), -0.02f);
                    fGo.transform.localScale = transform.localScale;
                    var fsr = fGo.AddComponent<SpriteRenderer>();
                    fsr.sprite = sp;
                    fsr.sortingOrder = sortingOrder + 2;
                    Vector2 vel = dir.normalized * Random.Range(2f, 5f) + new Vector2(Random.Range(-3f, 3f), Random.Range(4f, 9f));
                    fGo.AddComponent<WarlockFleshFragment>().Init(fsr, vel, Random.Range(-380f, 380f), groundLayer);
                }
            }

            Fx.Burst(point, new Color(0.65f, 0.35f, 0.95f), 10, 5f, 0.14f, 0.3f);
            Sfx.Play("hit", 0.75f, Random.Range(0.95f, 1.15f));
            Destroy(gameObject);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0f)
            {
                Fx.Burst(transform.position, new Color(0.62f, 0.32f, 0.95f), 8, 4f, 0.12f, 0.28f);
                Destroy(gameObject);
                return;
            }

            if (player != null)
            {
                Vector2 target = (Vector2)player.position + Vector2.up * 0.7f;
                Vector2 want = (target - (Vector2)transform.position).normalized;
                float ang = Vector2.SignedAngle(velocity, want);
                float maxTurn = turnRateDeg * dt;
                Vector2 rotated = (Vector2)(Quaternion.Euler(0f, 0f, Mathf.Clamp(ang, -maxTurn, maxTurn)) * velocity);
                velocity = rotated.normalized * Mathf.MoveTowards(velocity.magnitude, speed, 4f * dt);
            }

            Vector2 pos = (Vector2)transform.position + velocity * dt;
            Vector2 dir = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : Vector2.right;
            transform.position = new Vector3(pos.x, pos.y, -0.05f);
            if (sr != null) sr.flipX = dir.x > 0f;

            trailTimer -= dt;
            if (trailTimer <= 0f)
            {
                trailTimer = 0.065f;
                SpawnTrailSmoke(pos, dir);
            }

            if (player != null)
            {
                Vector2 playerCenter = (Vector2)player.position + Vector2.up * 0.7f;
                if (Vector2.Distance(pos, playerCenter) <= 0.75f)
                {
                    if (playerCtrl != null && playerCtrl.IsDashInvulnerable)
                    {
                        DamagePopup.SpawnText(player.position + Vector3.up * 1.4f, "DODGE!", new Color(0.55f, 0.95f, 1f, 1f));
                        playerCtrl.TriggerDodgeFlameAura(2f);
                    }
                    else if (playerAvatar != null && playerAvatar.Hurt(damage, "워록"))
                    {
                        float kb = Mathf.Abs(dir.x) > 0.1f ? Mathf.Sign(dir.x) : 1f;
                        playerCtrl?.ApplyKnockback(new Vector2(kb * 10f, 7f), 0.2f);
                    }
                    Fx.Burst(pos, new Color(0.65f, 0.35f, 0.95f), 10, 5f, 0.14f, 0.3f);
                    Destroy(gameObject);
                    return;
                }
            }
        }

        private void SpawnTrailSmoke(Vector2 pos, Vector2 flightDir)
        {
            if (trailSprites == null || trailSprites.Length == 0) return;
            var sp = trailSprites[Random.Range(0, trailSprites.Length)];
            if (sp == null) return;
            var go = new GameObject("WarlockGhostTrail");
            go.transform.position = new Vector3(pos.x + Random.Range(-0.08f, 0.08f), pos.y + Random.Range(-0.08f, 0.08f), -0.04f);
            var tsr = go.AddComponent<SpriteRenderer>();
            tsr.sprite = sp;
            tsr.sortingOrder = 21;
            Vector2 drift = -flightDir * 0.45f + new Vector2(Random.Range(-0.25f, 0.25f), Random.Range(0.4f, 0.9f));
            go.AddComponent<WarlockTrailSmoke>().Init(tsr, drift, transform.localScale.x, 0.42f);
        }
    }

    public class WarlockTrailSmoke : MonoBehaviour
    {
        private SpriteRenderer sr;
        private Vector2 drift;
        private float startScale;
        private float duration;
        private float elapsed;

        public void Init(SpriteRenderer r, Vector2 vel, float scale, float life)
        {
            sr = r;
            drift = vel;
            startScale = scale;
            duration = life;
            transform.localScale = Vector3.one * startScale;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            transform.position += (Vector3)(drift * dt);
            transform.localScale = Vector3.one * Mathf.Lerp(startScale, startScale * 0.35f, t);
            if (sr != null) sr.color = new Color(1f, 1f, 1f, 0.85f * (1f - t));
            if (t >= 1f) Destroy(gameObject);
        }
    }

    public class WarlockHitFx : MonoBehaviour
    {
        private SpriteRenderer sr;
        private float baseScale;
        private float duration;
        private float elapsed;

        public void Init(SpriteRenderer r, float scale, float life)
        {
            sr = r;
            baseScale = scale;
            duration = life;
            transform.localScale = Vector3.one * (baseScale * 0.7f);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            transform.localScale = Vector3.one * Mathf.Lerp(baseScale * 0.7f, baseScale * 1.15f, t);
            if (sr != null) sr.color = new Color(1f, 1f, 1f, 1f - t);
            if (t >= 1f) Destroy(gameObject);
        }
    }

    public class WarlockFleshFragment : MonoBehaviour
    {
        private SpriteRenderer sr;
        private Vector2 velocity;
        private float spin;
        private LayerMask groundLayer;
        private bool settled;
        private float life = 4.5f;

        public void Init(SpriteRenderer r, Vector2 vel, float spinDeg, LayerMask ground)
        {
            sr = r;
            velocity = vel;
            spin = spinDeg;
            groundLayer = ground;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0f) { Destroy(gameObject); return; }
            if (life < 1.0f && sr != null) sr.color = new Color(1f, 1f, 1f, life);
            if (settled) return;

            velocity.y -= 30f * dt;
            Vector2 pos = transform.position;
            Vector2 next = pos + velocity * dt;
            if (velocity.y <= 0f)
            {
                var hit = Physics2D.Raycast(pos, Vector2.down, Mathf.Abs(velocity.y * dt) + 0.15f, groundLayer);
                if (hit.collider != null && !hit.collider.isTrigger)
                {
                    next.y = hit.point.y + 0.08f;
                    velocity = Vector2.zero;
                    settled = true;
                }
            }
            transform.position = new Vector3(next.x, next.y, transform.position.z);
            transform.Rotate(0f, 0f, spin * dt);
        }
    }
}
