using System.Collections.Generic;
using System.Text;
using Procedural2D;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Archery
{
    /// <summary>한 프레임의 궁술 입력. 실제 장치 또는 자동 검증 드라이버가 채운다.</summary>
    public struct ArcheryInput
    {
        public Vector2 aimWorld;
        public bool fireDown, fireHeld, fireUp;
        public bool recallDown, recallHeld, recallUp;
    }

    /// <summary>
    /// 플레이어에 붙는 궁술 컨트롤러. 선택한 발사 궁술 1종과 회수 궁술 1종을 조합해 실행한다.
    /// 기존 Procedural2DAim 은 조준·자세 연출만 맡고, 사격과 회수는 이 컴포넌트가 맡는다.
    /// </summary>
    public class ArcheryCombat : MonoBehaviour
    {
        public ArcheryCombatContext Ctx { get; private set; }
        public ArcheryConfig Config => Ctx.Config;
        public LaunchStyle LaunchType { get; private set; }
        public RetrievalStyle RetrievalType { get; private set; }
        public LaunchBehaviour Launcher { get; private set; }
        public RetrievalBehaviour Retriever { get; private set; }
        public ArrowQuiver Quiver { get; private set; }
        public readonly List<ArcheryArrow> Arrows = new List<ArcheryArrow>();

        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public float Attack { get; private set; }

        /// <summary>공격속도 배율 (1 + 증가 합). 발사 간격 = 기본 간격 × 활시위 배율 ÷ 이 값</summary>
        public float AttackSpeedMultiplier => 1f + Launcher.AttackSpeedBonus;

        public ArcheryInput CurrentInput { get; private set; }
        /// <summary>자동 검증 드라이버가 입력을 대신 넣을 때 사용</summary>
        public System.Func<ArcheryInput> InputOverride;
        /// <summary>UI 위에 마우스가 있을 때 사격 입력 차단</summary>
        public System.Func<bool> PointerOverUi;

        public readonly List<string> Violations = new List<string>();
        public int TotalRecoveries { get; private set; }
        public string LastBatchDescription { get; private set; } = "";

        public Procedural2DAim Aim { get; private set; }
        public Vector2 PlayerCenter => (Vector2)transform.position + Vector2.up * 0.1f;
        public Vector2 MuzzlePosition => Aim != null && Aim.muzzlePoint != null ? (Vector2)Aim.muzzlePoint.position : PlayerCenter;

        private Collider2D[] playerColliders;
        private readonly List<ArcheryArrow> pendingArrivals = new List<ArcheryArrow>();
        private float emptyClickCooldown;

        public void Init(ArcheryCombatContext ctx, LaunchStyle launch, RetrievalStyle retrieval, List<ArrowDefinition> loadout,
                         float maxHp, Sprite arrowSprite, Transform arrowParent, int seed)
        {
            Ctx = ctx;
            LaunchType = launch;
            RetrievalType = retrieval;
            MaxHp = maxHp;
            Hp = maxHp;
            Attack = ctx.Config.playerAttack;
            Aim = GetComponent<Procedural2DAim>();
            playerColliders = GetComponentsInChildren<Collider2D>(true);

            Quiver = new ArrowQuiver(seed);
            for (int i = 0; i < loadout.Count; i++)
            {
                var go = new GameObject();
                go.transform.SetParent(arrowParent, false);
                var a = go.AddComponent<ArcheryArrow>();
                a.Setup(this, i + 1, loadout[i], arrowSprite);
                Arrows.Add(a);
                Quiver.InitialAdd(a);
            }

            Launcher = LaunchBehaviour.Create(launch, this);
            Retriever = RetrievalBehaviour.Create(retrieval, this);
            ctx.Log($"전투 시작: 발사 [{ArcheryConfig.LaunchName(launch)}] / 회수 [{ArcheryConfig.RetrievalName(retrieval)}], 화살 {loadout.Count}발");
        }

        public bool IsPlayerCollider(Collider2D c)
        {
            if (playerColliders == null) return false;
            for (int i = 0; i < playerColliders.Length; i++) if (playerColliders[i] == c) return true;
            return false;
        }

        // ───────────── 프레임 루프 ─────────────

        private void Update()
        {
            if (Ctx == null) return;
            float dt = Time.deltaTime;

            var input = InputOverride != null ? InputOverride() : ReadDeviceInput();
            CurrentInput = input;

            Launcher.Tick(input, dt);
            Retriever.Tick(input, dt);

            Vector2 p = PlayerCenter;
            for (int i = 0; i < Arrows.Count; i++) Arrows[i].SimulateFrame(dt, p);

            FlushArrivals();
            CheckIntegrity();
            if (emptyClickCooldown > 0f) emptyClickCooldown -= dt;

            Launcher.DrawGizmos(Ctx.Draw);
            Retriever.DrawGizmos(Ctx.Draw);
        }

        private void FixedUpdate()
        {
            if (Ctx == null) return;
            Physics2D.SyncTransforms();
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < Arrows.Count; i++) Arrows[i].SimulateFixed(dt);
        }

        private ArcheryInput ReadDeviceInput()
        {
            var input = new ArcheryInput();
            Camera cam = Camera.main;
            bool overUi = PointerOverUi != null && PointerOverUi();
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            var k = Keyboard.current;
            if (m != null && cam != null)
            {
                Vector2 sp = m.position.ReadValue();
                input.aimWorld = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -cam.transform.position.z));
                input.fireDown = m.leftButton.wasPressedThisFrame && !overUi;
                input.fireHeld = m.leftButton.isPressed && !overUi;
                input.fireUp = m.leftButton.wasReleasedThisFrame;
            }
            if (k != null)
            {
                input.recallDown = k.rKey.wasPressedThisFrame;
                input.recallHeld = k.rKey.isPressed;
                input.recallUp = k.rKey.wasReleasedThisFrame;
            }
#else
            if (cam != null)
            {
                Vector3 sp = Input.mousePosition;
                input.aimWorld = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -cam.transform.position.z));
            }
            input.fireDown = Input.GetMouseButtonDown(0) && !overUi;
            input.fireHeld = Input.GetMouseButton(0) && !overUi;
            input.fireUp = Input.GetMouseButtonUp(0);
            input.recallDown = Input.GetKeyDown(KeyCode.R);
            input.recallHeld = Input.GetKey(KeyCode.R);
            input.recallUp = Input.GetKeyUp(KeyCode.R);
#endif
            return input;
        }

        // ───────────── 발사 공통 ─────────────

        /// <summary>발사 간격 = 기본 간격 × 활시위 배율 (공격속도는 진행 속도로 반영)</summary>
        public float BaseIntervalFor(float baseInterval, ArrowDefinition def) => baseInterval * Config.DrawMultiplier(def);

        public ArcheryArrow FireNext(Vector2 direction, float gravityAccel, float damage)
        {
            var fired = Quiver.TakeNext();
            if (fired == null) return null;
            float range = gravityAccel > 0f ? float.MaxValue : Config.straightMaxRange;
            fired.Launch(MuzzlePosition, direction, Config.arrowSpeed, gravityAccel, range, damage);
            PlayFireFeedback(direction);
            return fired;
        }

        public void PlayFireFeedback(Vector2 direction)
        {
            if (Aim != null) Aim.PlayShotRecoil(direction);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayArrowShoot(MuzzlePosition);
        }

        public void EmptyQuiverFeedback()
        {
            if (emptyClickCooldown > 0f) return;
            emptyClickCooldown = 0.35f;
            Ctx.Log("화살통이 비어 있음");
            if (AudioManager.Instance != null) AudioManager.Instance.PlayEmptyClick(PlayerCenter);
        }

        public Vector2 AimDirectionFrom(Vector2 origin, Vector2 aimWorld)
        {
            Vector2 d = aimWorld - origin;
            if (d.sqrMagnitude < 0.0001f) d = Aim != null && !Aim.IsFacingRight ? Vector2.left : Vector2.right;
            return d.normalized;
        }

        /// <summary>
        /// 화살(발사/찌르기)이 적에게 닿았을 때. 한 비행당 한 번만 판정하고,
        /// 회수 궁술에 따라 적에게 박히거나(기본 줍기 제외) 바닥으로 떨어진다.
        /// </summary>
        public void OnArrowStruckEnemy(ArcheryArrow arrow, ArcheryEnemy enemy, Vector2 point, HitSource source)
        {
            if (!arrow.MarkHitResolved())
            {
                ReportViolation($"{arrow.Label}: 같은 비행에서 중복 적중 시도");
                return;
            }

            ArrowHitResolver.Resolve(this, arrow, enemy, source, arrow.ShotDamage, arrow.LastDirection);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayArrowEnemyHit(point);
            if (ImpactParticleManager.Instance != null) ImpactParticleManager.Instance.PlayImpact(point, -arrow.LastDirection, true);

            if (Retriever.ArrowsStickToEnemies && enemy.IsAlive)
            {
                arrow.StickTo(enemy, point);
            }
            else
            {
                // 기본(줍기): 박히지 않고 적 앞에 떨어진다 → 바닥에서 주울 수 있음
                Vector2 bounce = new Vector2(-arrow.LastDirection.x * 2.5f, 5f);
                arrow.Drop(bounce);
            }
        }

        // ───────────── 회수 공통 (화살통으로 들어가는 유일한 경로) ─────────────

        /// <summary>
        /// 화살을 화살통으로 회수한다. 같은 프레임에 회수된 화살은 하나의 '동시 회수' 묶음이 된다.
        /// 이미 화살통에 있거나 비행 중이거나 이번 프레임에 이미 회수된 화살은 거부한다.
        /// </summary>
        public bool TryRecover(ArcheryArrow arrow, string source)
        {
            if (arrow == null) return false;
            if (pendingArrivals.Contains(arrow)) return false;
            switch (arrow.State)
            {
                case ArrowState.Grounded:
                case ArrowState.Stuck:
                case ArrowState.Falling:
                case ArrowState.Returning:
                    break;
                default:
                    return false; // InQuiver(중복 회수), Flying(발사 도중) 거부
            }
            if (Quiver.Contains(arrow))
            {
                ReportViolation($"{arrow.Label}: 화살통에 있는데 필드 상태({arrow.State})");
                return false;
            }
            arrow.EnterQuiver();
            pendingArrivals.Add(arrow);
            sourcesThisFrame.Add(source);
            TotalRecoveries++;
            if (AudioManager.Instance != null) AudioManager.Instance.PlayArrowCatch(PlayerCenter);
            return true;
        }

        private readonly HashSet<string> sourcesThisFrame = new HashSet<string>();

        public void NotifyReturnArrived(ArcheryArrow arrow)
        {
            TryRecover(arrow, arrow.ReturnSource ?? "귀환");
        }

        private void FlushArrivals()
        {
            if (pendingArrivals.Count == 0) return;
            var batch = new List<ArcheryArrow>(pendingArrivals);
            pendingArrivals.Clear();
            var added = Quiver.AddRecoveredBatch(batch);
            var sb = new StringBuilder();
            for (int i = 0; i < added.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(added[i].Label);
            }
            string src = string.Join("/", sourcesThisFrame);
            sourcesThisFrame.Clear();
            LastBatchDescription = added.Count > 1
                ? $"동시 회수 {added.Count}발({src}) → 무작위 배치: {sb}"
                : $"순차 회수({src}) → 맨 뒤: {sb}";
            Ctx.Log(LastBatchDescription);
        }

        /// <summary>테스트 보조: 화살 두 발을 지정한 두 지점 바닥에 내려놓는다 (번개 사이 테스트용)</summary>
        public int DebugPlaceArrows(params Vector2[] points)
        {
            int placed = 0;
            for (int i = 0; i < points.Length; i++)
            {
                var a = Quiver.TakeNext();
                if (a == null) break;
                a.DebugPlaceOnGround(points[i]);
                placed++;
            }
            if (placed > 0) Ctx.Log($"[테스트 보조] 화살 {placed}발을 바닥에 배치");
            return placed;
        }

        // ───────────── 무결성 검사 ─────────────

        public int CountState(ArrowState s)
        {
            int n = 0;
            for (int i = 0; i < Arrows.Count; i++) if (Arrows[i].State == s) n++;
            return n;
        }

        private void CheckIntegrity()
        {
            if (Quiver.HasDuplicates()) ReportViolation("화살통에 같은 화살이 중복으로 들어 있음");
            int inQuiverState = CountState(ArrowState.InQuiver);
            if (inQuiverState != Quiver.Count) ReportViolation($"화살통 수({Quiver.Count})와 InQuiver 상태 수({inQuiverState}) 불일치");
            for (int i = 0; i < Quiver.Count; i++)
            {
                if (Quiver.Order[i].State != ArrowState.InQuiver) ReportViolation($"{Quiver.Order[i].Label}: 화살통에 있으나 상태가 {Quiver.Order[i].State}");
            }
        }

        public void ReportViolation(string msg)
        {
            if (Violations.Count < 100) Violations.Add(msg);
            Ctx?.Log("<무결성 위반> " + msg);
            Debug.LogWarning("[Archery] 무결성 위반: " + msg);
        }

        public Vector2 SafeGroundPoint() => new Vector2(transform.position.x, 0.1f);
    }
}
