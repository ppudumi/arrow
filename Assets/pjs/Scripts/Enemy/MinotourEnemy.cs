using Laurel;
using Procedural2D;
using UnityEngine;

namespace pjs
{
    /// <summary>
    /// Minotour.png 13종 스프라이트를 사용하는 이동형 적/보스.
    /// - 대기(idle) → 거리/패턴에 따라 근접 휘두르기(attackReady→attack), 일반 점프 이동(readyandendofjump→whileJump),
    ///   대점프 내려찍기(readytoBigjump→whileBigjump→jumpSmash)를 수행한다.
    /// - ILaserTarget 구현으로 레이저 활 타격, 피격 깜빡임, 데미지 팝업, 처치 시 flesh* 5종 파츠 분리를 처리한다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class MinotourEnemy : Enemy, ILaserTarget
    {
        private enum State
        {
            Idle,
            DashApproach,
            AttackReady,
            Attack,
            JumpReady,
            WhileJump,
            JumpLand,
            BigJumpReady,
            WhileBigJump,
            JumpSmash
        }

        [Header("Sprites (Minotour.png)")]
        public Sprite idleSprite;
        public Sprite attackReadySprite;
        public Sprite attackSprite;
        public Sprite readyAndEndOfJumpSprite;
        public Sprite whileJumpSprite;
        public Sprite readyToBigJumpSprite;
        public Sprite whileBigJumpSprite;
        public Sprite jumpSmashSprite;

        [Header("Death Flesh Sprites")]
        public Sprite fleshHeadSprite;
        public Sprite flesh0Sprite;
        public Sprite flesh1Sprite;
        public Sprite flesh2Sprite;
        public Sprite flesh3Sprite;

        [Header("Stats & Hitbox")]
        public float maxHp = 200f;
        public Vector2 hitboxOffset = new Vector2(0f, 0.85f);
        public Vector2 hitboxSize = new Vector2(1.6f, 1.7f);
        public float visualScale = 2.0f;
        public int sortingOrder = 10;

        [Header("Head Critical Hitbox (머리 치명타)")]
        [Tooltip("머리 치명타 판정 위치 (몸통 콜라이더 바로 위)")]
        public Vector2 headHitboxOffset = new Vector2(0f, 2.05f);
        [Tooltip("머리 치명타 판정 크기")]
        public Vector2 headHitboxSize = new Vector2(1.1f, 0.7f);
        [Tooltip("머리 명중 시 데미지 배율")]
        public float headCritMultiplier = 3f;

        [Header("HP Bar")]
        public Vector2 hpBarOffset = new Vector2(0f, 2.65f);
        public Vector2 hpBarSize = new Vector2(2.2f, 0.22f);

        [Header("Movement & Physics")]
        public LayerMask groundLayer = 1 << 8; // Terrain
        public new float gravity = 40f;
        public float aggroRange = 30f;
        public float attackRange = 3.8f;
        public float idleDuration = 0.55f;

        [Header("Attack (Sweep & Dash)")]
        public float attackDamage = 15f;
        public float attackReadyTime = 0.36f;
        public float attackTime = 0.42f;
        public float attackLungeSpeed = 24f;
        public float shortAttackTime = 0.24f;
        public float shortAttackLungeSpeed = 11f;
        public Vector2 attackVisualOffset = new Vector2(-0.35f, 0f);

        [Header("Dash Approach & Elevation")]
        [Tooltip("이 거리보다 멀면 대시로 거리를 좁힌 뒤 대시 공격")]
        public float dashApproachRange = 9.5f;
        public float dashApproachSpeed = 16f;
        public float dashApproachTime = 0.36f;
        [Tooltip("플레이어와 이 높이 이상 단차가 나면 점프 공격")]
        public float heightDiffThreshold = 1.6f;

        [Header("Normal Jump")]
        public float jumpReadyTime = 0.14f;
        public float jumpLandTime = 0.14f;
        public float jumpSpeedY = 13f;
        public float jumpSpeedX = 9.5f;

        [Header("Big Jump Smash")]
        public float smashDamage = 25f;
        public float bigJumpReadyTime = 0.34f;
        public float bigJumpSpeedY = 19.5f;
        public float bigJumpSpeedX = 11.5f;
        public float jumpSmashTime = 0.36f;
        public Vector2 jumpSmashVisualOffset = new Vector2(-0.25f, 0f);

        private Transform visual;
        private SpriteRenderer sr;
        private SpriteRenderer hpFillSr;
        private BoxCollider2D col;
        private BoxCollider2D headCol;
        private Transform player;
        private PlayerAvatar playerAvatar;
        private ProceduralCharacterController playerCtrl;
        private TutorialBossMapTest arenaMap;

        private State minoState = State.Idle;
        private float stateTimer;
        private bool attackHitDealt;
        private float flashT = -1f;
        private float pendingAttackLungeSpeed;
        private float pendingAttackTime;
        private bool pendingNextHitCritical;
        private Vector2 lastHitDirection = Vector2.right;

        private static readonly float[] Blink = { 0.045f, 0.035f, 0.035f };

        private void Awake()
        {
            gameObject.layer = Layers.Enemy;

            col = GetComponent<BoxCollider2D>();
            col.isTrigger = true;
            hitboxOffset = col.offset;
            hitboxSize = col.size;
            Col = col;
            halfSize = hitboxSize * 0.5f;
            MaxHp = maxHp;
            Hp = maxHp;
            DisplayName = "미노타우로스";
            baseColor = Color.white;
            spawnPos = transform.position;

            var existingHead = transform.Find("HeadHitbox");
            if (existingHead != null && existingHead.TryGetComponent(out BoxCollider2D existingHeadCol))
            {
                headCol = existingHeadCol;
                headCol.gameObject.layer = Layers.Enemy;
                headCol.isTrigger = true;
                headHitboxOffset = headCol.offset;
                headHitboxSize = headCol.size;
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
            sr.sprite = idleSprite;

            BuildHpBar();
        }

        protected override void BuildVisual(Color c, bool elite)
        {
            // MinotourEnemy는 Awake()에서 자체 스프라이트/콜라이더를 구성하므로 Enemy 기본 사각형 비주얼을 생성하지 않는다.
        }

        private void BuildHpBar()
        {
            var oldBar = transform.Find("HpBar");
            if (oldBar != null) Destroy(oldBar.gameObject);

            var barRoot = new GameObject("HpBar").transform;
            barRoot.SetParent(transform, false);
            barRoot.localPosition = hpBarOffset;

            Art.Quad(barRoot, "Bg", Art.Square, new Color(0.08f, 0.08f, 0.08f, 0.85f),
                new Vector2(hpBarSize.x + 0.08f, hpBarSize.y + 0.08f), sortingOrder + 5, Vector3.zero);
            hpFillSr = Art.Quad(barRoot, "Fill", Art.Square, new Color(0.92f, 0.22f, 0.22f, 1f),
                hpBarSize, sortingOrder + 6, Vector3.zero);
            UpdateHpBar();
        }

        private void UpdateHpBar()
        {
            if (hpFillSr == null) return;
            float ratio = maxHp > 0f ? Mathf.Clamp01(Hp / maxHp) : 0f;
            float w = hpBarSize.x * ratio;
            hpFillSr.transform.localScale = new Vector3(w, hpBarSize.y, 1f);
            hpFillSr.transform.localPosition = new Vector3((w - hpBarSize.x) * 0.5f, 0f, 0f);
        }

        private void Start()
        {
            FindPlayer();
            arenaMap = FindFirstObjectByType<TutorialBossMapTest>();

            SnapToGround();
            EnsureRegisteredInRoom();
            EnterState(State.Idle);
        }

        private void EnsureEnemyInitialized()
        {
            if (Status != null) return;
            var room = Room != null ? Room : FindAnyObjectByType<Room>();
            var def = new EnemyDef
            {
                id = "minotour",
                name = "미노타우로스",
                ai = "minotour",
                hp = maxHp,
                speed = dashApproachSpeed,
                damage = attackDamage,
                w = hitboxSize.x,
                h = hitboxSize.y,
                color = "#B04030",
                gold = 0
            };
            Init(room, def, 1f, 1f, false);
            ContactDamage = 0f;
            goldValue = 0;
            halfSize = hitboxSize * 0.5f;
            baseColor = Color.white;
            Col = col;
            facing = 1f;
        }

        private void EnsureRegisteredInRoom()
        {
            EnsureEnemyInitialized();
            var room = Room != null ? Room : FindAnyObjectByType<Room>();
            if (room != null && !dead && !room.Enemies.Contains(this))
            {
                room.Enemies.Add(this);
            }
        }

        private void FindPlayer()
        {
            if (player != null) return;
            var ctrl = FindFirstObjectByType<ProceduralCharacterController>();
            if (ctrl != null)
            {
                player = ctrl.transform;
                playerCtrl = ctrl;
                playerAvatar = ctrl.GetComponent<PlayerAvatar>();
                return;
            }
            var avatar = FindFirstObjectByType<PlayerAvatar>();
            if (avatar != null)
            {
                player = avatar.transform;
                playerAvatar = avatar;
                playerCtrl = avatar.GetComponent<ProceduralCharacterController>();
            }
        }

        protected override void Update()
        {
            if (dead) return;
            EnsureRegisteredInRoom();
            if (player == null) FindPlayer();

            float dt = Time.deltaTime;
            if (Status != null)
            {
                Status.Tick(dt);
                if (dead) return;
            }
            UpdateFlash(dt);
            if (Status == null || Status.stunT <= 0f) UpdateStateMachine(dt);
            MoveAndCollide(dt);
        }

        private void UpdateStateMachine(float dt)
        {
            stateTimer -= dt;

            switch (minoState)
            {
                case State.Idle:
                    velocity.x = Mathf.MoveTowards(velocity.x, 0f, 30f * dt);
                    if (stateTimer <= 0f) ChooseNextAction();
                    break;

                case State.DashApproach:
                    velocity.x = facing * dashApproachSpeed;
                    if (stateTimer <= 0f || (player != null && Mathf.Abs(player.position.x - transform.position.x) <= attackRange + 1.5f))
                    {
                        FacePlayer();
                        pendingAttackLungeSpeed = attackLungeSpeed;
                        pendingAttackTime = attackTime;
                        EnterState(State.AttackReady);
                    }
                    break;

                case State.AttackReady:
                    velocity.x = 0f;
                    if (stateTimer <= 0f)
                    {
                        velocity.x = facing * (pendingAttackLungeSpeed > 0f ? pendingAttackLungeSpeed : attackLungeSpeed);
                        attackHitDealt = false;
                        EnterState(State.Attack);
                        Sfx.Play("hit", 0.55f, 0.75f);
                    }
                    break;

                case State.Attack:
                    velocity.x = Mathf.MoveTowards(velocity.x, 0f, 12f * dt);
                    if (!attackHitDealt) TryDamagePlayer(attackDamage, 2.8f, 2.0f, true);
                    if (stateTimer <= 0f) EnterState(State.Idle);
                    break;

                case State.JumpReady:
                    velocity.x = 0f;
                    if (stateTimer <= 0f)
                    {
                        FacePlayer();
                        velocity = new Vector2(facing * jumpSpeedX, jumpSpeedY);
                        grounded = false;
                        EnterState(State.WhileJump);
                    }
                    break;

                case State.WhileJump:
                    if (grounded && velocity.y <= 0f)
                    {
                        velocity.x = 0f;
                        EnterState(State.JumpLand);
                    }
                    break;

                case State.JumpLand:
                    velocity.x = 0f;
                    if (stateTimer <= 0f) EnterState(State.Idle);
                    break;

                case State.BigJumpReady:
                    velocity.x = 0f;
                    if (stateTimer <= 0f)
                    {
                        FacePlayer();
                        velocity = new Vector2(facing * bigJumpSpeedX, bigJumpSpeedY);
                        grounded = false;
                        EnterState(State.WhileBigJump);
                    }
                    break;

                case State.WhileBigJump:
                    if (grounded && velocity.y <= 0f)
                    {
                        velocity.x = 0f;
                        attackHitDealt = false;
                        EnterState(State.JumpSmash);
                        TryDamagePlayer(smashDamage, 3.6f, 2.5f, false);
                        Fx.Burst(transform.position, new Color(0.95f, 0.45f, 0.2f), 10, 6f, 0.16f, 0.35f);
                        Sfx.Play("hit", 0.85f, 0.65f);
                    }
                    break;

                case State.JumpSmash:
                    velocity.x = 0f;
                    if (!attackHitDealt) TryDamagePlayer(smashDamage, 3.6f, 2.5f, false);
                    if (stateTimer <= 0f) EnterState(State.Idle);
                    break;
            }
        }

        private void ChooseNextAction()
        {
            FacePlayer();
            if (player == null)
            {
                EnterState(State.Idle);
                return;
            }

            float dx = Mathf.Abs(player.position.x - transform.position.x);
            float dy = Mathf.Abs(player.position.y - transform.position.y);
            if (dx > aggroRange)
            {
                EnterState(State.Idle);
                return;
            }

            // 근거리: 짧은 거리 대시 공격
            if (dx <= attackRange)
            {
                pendingAttackLungeSpeed = shortAttackLungeSpeed;
                pendingAttackTime = shortAttackTime;
                EnterState(State.AttackReady);
                return;
            }

            // 멀리 + 단차가 있는 곳: 점프 공격
            if (dy >= heightDiffThreshold)
            {
                EnterState(State.BigJumpReady);
                return;
            }

            // 아주 멀리 (같은 높이): 대시 준비 후 곧바로 긴 거리 대시 공격
            if (dx > dashApproachRange)
            {
                pendingAttackLungeSpeed = attackLungeSpeed;
                pendingAttackTime = attackTime;
                EnterState(State.AttackReady);
                return;
            }

            // 기존 점프 공격 거리 (중거리, 같은 높이): 짧은 거리 대시 공격
            pendingAttackLungeSpeed = shortAttackLungeSpeed;
            pendingAttackTime = shortAttackTime;
            EnterState(State.AttackReady);
        }

        private void EnterState(State next)
        {
            minoState = next;
            Vector2 offset = Vector2.zero;

            switch (next)
            {
                case State.Idle:
                    stateTimer = idleDuration;
                    SetSprite(idleSprite);
                    break;
                case State.DashApproach:
                    stateTimer = dashApproachTime;
                    SetSprite(readyAndEndOfJumpSprite);
                    break;
                case State.AttackReady:
                    stateTimer = attackReadyTime;
                    SetSprite(attackReadySprite);
                    break;
                case State.Attack:
                    stateTimer = pendingAttackTime > 0f ? pendingAttackTime : attackTime;
                    SetSprite(attackSprite);
                    offset = attackVisualOffset;
                    break;
                case State.JumpReady:
                    stateTimer = jumpReadyTime;
                    SetSprite(readyAndEndOfJumpSprite);
                    break;
                case State.WhileJump:
                    SetSprite(whileJumpSprite);
                    break;
                case State.JumpLand:
                    stateTimer = jumpLandTime;
                    SetSprite(readyAndEndOfJumpSprite);
                    break;
                case State.BigJumpReady:
                    stateTimer = bigJumpReadyTime;
                    SetSprite(readyToBigJumpSprite);
                    break;
                case State.WhileBigJump:
                    SetSprite(whileBigJumpSprite);
                    break;
                case State.JumpSmash:
                    stateTimer = jumpSmashTime;
                    SetSprite(jumpSmashSprite);
                    offset = jumpSmashVisualOffset;
                    break;
            }

            visual.localPosition = new Vector3(offset.x * facing, offset.y, 0f);
        }

        private void SetSprite(Sprite s)
        {
            if (sr != null && s != null) sr.sprite = s;
        }

        private void FacePlayer()
        {
            if (player == null) return;
            float dx = player.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.1f) facing = dx >= 0f ? 1f : -1f;
            if (sr != null) sr.flipX = facing < 0f;
        }

        private void MoveAndCollide(float dt)
        {
            if (!grounded) velocity.y -= gravity * dt;

            Vector2 pos = transform.position;
            Vector2 half = hitboxSize * 0.5f;

            // Horizontal move + wall check
            float moveX = velocity.x * dt;
            if (Mathf.Abs(moveX) > 0.0001f)
            {
                Vector2 center = pos + hitboxOffset;
                float dist = Mathf.Abs(moveX) + 0.05f;
                var wallHit = Physics2D.BoxCast(center, new Vector2(hitboxSize.x * 0.9f, hitboxSize.y * 0.8f), 0f,
                    new Vector2(Mathf.Sign(moveX), 0f), dist, groundLayer);
                if (wallHit.collider != null && !wallHit.collider.isTrigger)
                {
                    velocity.x = 0f;
                }
                else
                {
                    pos.x += moveX;
                }
            }

            if (arenaMap != null)
            {
                float minX = arenaMap.arena.xMin + half.x + 0.2f;
                float maxX = arenaMap.arena.xMax - half.x - 0.2f;
                if (pos.x < minX) { pos.x = minX; velocity.x = 0f; }
                else if (pos.x > maxX) { pos.x = maxX; velocity.x = 0f; }
            }

            // Vertical move + ground check
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

        public bool IsHeadHit(Transform part, Vector2 worldPoint)
        {
            if (part != null && part == headCol?.transform) return true;
            return IsHeadShot(worldPoint);
        }

        public void MarkNextHitCritical() => pendingNextHitCritical = true;

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

            int dmgInt = Mathf.Max(1, Mathf.RoundToInt(amount));
            DamagePopup.Spawn(transform.position + Vector3.up * 1.4f + new Vector3(Random.Range(-0.2f, 0.2f), 0f, 0f), dmgInt, crit, crit ? new Color(1f, 0.88f, 0.25f, 1f) : color);

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

            bool isHead = IsHeadShot(point);
            if (isHead)
            {
                damage = Mathf.Max(1, Mathf.RoundToInt(damage * headCritMultiplier));
                critical = true;
            }

            Hp = Mathf.Max(0f, Hp - damage);
            UpdateHpBar();
            flashT = 0f;
            DamagePopup.Spawn(point + new Vector2(Random.Range(-0.2f, 0.2f), 0.3f), damage, critical);
            Fx.Burst(point, critical ? new Color(1f, 0.85f, 0.2f) : new Color(0.95f, 0.25f, 0.25f), critical ? 10 : 5, critical ? 6f : 4f, critical ? 0.14f : 0.1f, 0.28f);
            Sfx.Play("hit", critical ? 0.95f : 0.7f, critical ? 0.8f : Random.Range(0.95f, 1.15f));

            if (Hp <= 0f) Die();
        }

        private void TryDamagePlayer(float dmg, float reachX, float reachY, bool frontOnly)
        {
            if (player == null) return;
            if (playerAvatar == null) playerAvatar = player.GetComponent<PlayerAvatar>();
            if (playerCtrl == null) playerCtrl = player.GetComponent<ProceduralCharacterController>();
            if (playerAvatar == null) return;

            Vector2 diff = (Vector2)player.position - (Vector2)transform.position;
            bool inX = frontOnly
                ? (diff.x * facing >= -0.4f && diff.x * facing <= reachX)
                : Mathf.Abs(diff.x) <= reachX;
            if (inX && diff.y >= -0.5f && diff.y <= reachY)
            {
                if (playerCtrl != null && playerCtrl.IsDashing)
                {
                    attackHitDealt = true;
                    DamagePopup.SpawnText(player.position + Vector3.up * 1.4f, "DODGE!", new Color(0.55f, 0.95f, 1f, 1f));
                    return;
                }

                if (playerAvatar.Hurt(dmg, "미노타우로스"))
                {
                    attackHitDealt = true;
                    float kbDir = Mathf.Abs(diff.x) > 0.05f ? Mathf.Sign(diff.x) : facing;
                    if (playerCtrl != null)
                        playerCtrl.ApplyKnockback(new Vector2(kbDir * 16f, 11f), 0.28f);
                }
            }
        }

        private bool IsHeadShot(Vector2 worldPoint)
        {
            if (headCol != null)
            {
                Bounds b = headCol.bounds;
                b.Expand(0.1f);
                if (b.Contains(new Vector3(worldPoint.x, worldPoint.y, b.center.z))) return true;
            }
            Vector2 local = transform.InverseTransformPoint(worldPoint);
            float hx = headHitboxOffset.x * facing;
            float hy = headHitboxOffset.y;
            return Mathf.Abs(local.x - hx) <= headHitboxSize.x * 0.5f + 0.05f
                && Mathf.Abs(local.y - hy) <= headHitboxSize.y * 0.5f + 0.05f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.15f, 0.95f);
            Vector2 hOff = new Vector2(headHitboxOffset.x * facing, headHitboxOffset.y);
            Vector2 hSize = headHitboxSize;
            Vector3 center = (Vector2)transform.position + hOff;
            var existingHead = transform.Find("HeadHitbox");
            if (existingHead != null && existingHead.TryGetComponent(out BoxCollider2D hc))
            {
                center = existingHead.TransformPoint(hc.offset);
                hSize = Vector2.Scale(hc.size, existingHead.lossyScale);
            }
            Gizmos.DrawWireCube(center, hSize);
        }

        private void UpdateFlash(float dt)
        {
            if (flashT < 0f || sr == null) return;
            flashT += dt;
            float t = flashT;
            bool white = t < Blink[0] || (t >= Blink[0] + Blink[1] && t < Blink[0] + Blink[1] + Blink[2]);
            sr.color = white ? new Color(1f, 0.35f, 0.35f, 1f) : Color.white;
            if (t >= Blink[0] + Blink[1] + Blink[2])
            {
                sr.color = Color.white;
                flashT = -1f;
            }
        }

        protected override void OnDeathVisual()
        {
            Vector2 hitDir = lastHitDirection;
            Fx.Burst(transform.position + Vector3.up * 1f, new Color(0.85f, 0.15f, 0.15f), 18, 7f, 0.18f, 0.5f);
            Sfx.Play("hit", 1f, 0.55f);

            SpawnFlesh(fleshHeadSprite, new Vector2(0f, 1.4f), hitDir);
            SpawnFlesh(flesh0Sprite, new Vector2(0.3f * facing, 0.9f), hitDir);
            SpawnFlesh(flesh1Sprite, new Vector2(-0.3f, 0.8f), hitDir);
            SpawnFlesh(flesh2Sprite, new Vector2(0.2f, 1.1f), hitDir);
            SpawnFlesh(flesh3Sprite, new Vector2(0f, 0.7f), hitDir);

            Destroy(gameObject);
        }

        private void SpawnFlesh(Sprite sprite, Vector2 localOffset, Vector2 hitDir)
        {
            if (sprite == null) return;
            var go = new GameObject($"Minotour_{sprite.name}");
            go.transform.position = transform.position + (Vector3)localOffset;
            go.transform.localScale = new Vector3(visualScale, visualScale, 1f);

            var fsr = go.AddComponent<SpriteRenderer>();
            fsr.sprite = sprite;
            fsr.sortingOrder = sortingOrder;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 2.5f;
            rb.linearVelocity = new Vector2(
                Random.Range(-4f, 4f) + hitDir.x * 2f,
                Random.Range(5f, 10f));
            rb.angularVelocity = Random.Range(-360f, 360f);

            var box = go.AddComponent<BoxCollider2D>();
            box.size = sprite.bounds.size * 0.7f;

            Destroy(go, 4f);
        }
    }
}
