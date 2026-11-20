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
    public class MinotourEnemy : MonoBehaviour, ILaserTarget
    {
        private enum State
        {
            Idle,
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
        public float maxHp = 400f;
        public Vector2 hitboxOffset = new Vector2(0f, 1.1f);
        public Vector2 hitboxSize = new Vector2(1.8f, 2.2f);
        public float visualScale = 2.0f;
        public int sortingOrder = 10;

        [Header("HP Bar")]
        public Vector2 hpBarOffset = new Vector2(0f, 2.65f);
        public Vector2 hpBarSize = new Vector2(2.2f, 0.22f);

        [Header("Movement & Physics")]
        public LayerMask groundLayer = 1 << 8; // Terrain
        public float gravity = 32f;
        public float aggroRange = 30f;
        public float attackRange = 3.6f;
        public float idleDuration = 0.75f;

        [Header("Attack (Sweep)")]
        public float attackReadyTime = 0.42f;
        public float attackTime = 0.34f;
        public float attackLungeSpeed = 7.5f;
        public Vector2 attackVisualOffset = new Vector2(-0.35f, 0f);

        [Header("Normal Jump")]
        public float jumpReadyTime = 0.16f;
        public float jumpLandTime = 0.16f;
        public float jumpSpeedY = 11.5f;
        public float jumpSpeedX = 7.5f;

        [Header("Big Jump Smash")]
        public float bigJumpReadyTime = 0.38f;
        public float bigJumpSpeedY = 17.5f;
        public float bigJumpSpeedX = 9.5f;
        public float jumpSmashTime = 0.42f;
        public Vector2 jumpSmashVisualOffset = new Vector2(-0.25f, 0f);

        public float Hp { get; private set; }
        public bool IsAlive => !dead && Hp > 0f;

        private Transform visual;
        private SpriteRenderer sr;
        private SpriteRenderer hpFillSr;
        private BoxCollider2D col;
        private Transform player;
        private TutorialBossMapTest arenaMap;

        private State state = State.Idle;
        private float stateTimer;
        private float facing = 1f; // +1 = Right, -1 = Left
        private Vector2 velocity;
        private bool grounded;
        private bool dead;
        private float flashT = -1f;
        private int bigJumpCounter;

        private static readonly float[] Blink = { 0.045f, 0.035f, 0.035f };

        private void Awake()
        {
            Hp = maxHp;
            gameObject.layer = Layers.Enemy;

            col = GetComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.offset = hitboxOffset;
            col.size = hitboxSize;

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
            EnterState(State.Idle);
        }

        private void FindPlayer()
        {
            if (player != null) return;
            var ctrl = FindFirstObjectByType<ProceduralCharacterController>();
            if (ctrl != null) { player = ctrl.transform; return; }
            var avatar = FindFirstObjectByType<PlayerAvatar>();
            if (avatar != null) player = avatar.transform;
        }

        private void Update()
        {
            if (dead) return;
            if (player == null) FindPlayer();

            float dt = Time.deltaTime;
            UpdateFlash(dt);
            UpdateStateMachine(dt);
            MoveAndCollide(dt);
        }

        private void UpdateStateMachine(float dt)
        {
            stateTimer -= dt;

            switch (state)
            {
                case State.Idle:
                    velocity.x = Mathf.MoveTowards(velocity.x, 0f, 30f * dt);
                    if (stateTimer <= 0f) ChooseNextAction();
                    break;

                case State.AttackReady:
                    velocity.x = 0f;
                    if (stateTimer <= 0f)
                    {
                        velocity.x = facing * attackLungeSpeed;
                        EnterState(State.Attack);
                        Sfx.Play("hit", 0.55f, 0.75f);
                    }
                    break;

                case State.Attack:
                    velocity.x = Mathf.MoveTowards(velocity.x, 0f, 18f * dt);
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
                        EnterState(State.JumpSmash);
                        Fx.Burst(transform.position, new Color(0.95f, 0.45f, 0.2f), 10, 6f, 0.16f, 0.35f);
                        Sfx.Play("hit", 0.85f, 0.65f);
                    }
                    break;

                case State.JumpSmash:
                    velocity.x = 0f;
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
            if (dx > aggroRange)
            {
                EnterState(State.Idle);
                return;
            }

            if (dx <= attackRange)
            {
                EnterState(State.AttackReady);
                return;
            }

            bigJumpCounter++;
            if (bigJumpCounter >= 3)
            {
                bigJumpCounter = 0;
                EnterState(State.BigJumpReady);
            }
            else
            {
                EnterState(State.JumpReady);
            }
        }

        private void EnterState(State next)
        {
            state = next;
            Vector2 offset = Vector2.zero;

            switch (next)
            {
                case State.Idle:
                    stateTimer = idleDuration;
                    SetSprite(idleSprite);
                    break;
                case State.AttackReady:
                    stateTimer = attackReadyTime;
                    SetSprite(attackReadySprite);
                    break;
                case State.Attack:
                    stateTimer = attackTime;
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

        public void TakeLaserHit(int damage, bool critical, Vector2 point, Vector2 dir)
        {
            if (dead) return;

            Hp = Mathf.Max(0f, Hp - damage);
            UpdateHpBar();
            flashT = 0f;
            DamagePopup.Spawn(point + new Vector2(Random.Range(-0.2f, 0.2f), 0.3f), damage, critical);
            Fx.Burst(point, critical ? new Color(1f, 0.85f, 0.3f) : new Color(0.95f, 0.25f, 0.25f), 5, 4f, 0.1f, 0.25f);
            Sfx.Play("hit", critical ? 0.9f : 0.7f, critical ? 0.85f : Random.Range(0.95f, 1.15f));

            if (Hp <= 0f) Die(dir);
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

        private void Die(Vector2 hitDir)
        {
            dead = true;
            col.enabled = false;
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
