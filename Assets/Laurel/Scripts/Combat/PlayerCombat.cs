using System.Collections.Generic;
using System.Text;
using Procedural2D;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Laurel
{
    /// <summary>한 프레임의 전투 입력. 실제 장치 또는 자동 검증 드라이버가 채운다.</summary>
    public struct CombatInput
    {
        public Vector2 aimWorld;
        public bool fireDown, fireHeld, fireUp;
        public bool recallDown, recallHeld, recallUp;
        public bool discardDown;
    }

    public class CombatStats
    {
        public int shots, hitsLanded, phantomHits, recoveries, discards, consumed, thrustMisses;
    }

    /// <summary>
    /// 플레이어 전투. 발사 궁술 1종 + 회수 궁술 1종을 독립적으로 조합한다(5×6=30).
    /// 화살통으로 들어가는 길은 TryRecover→FlushArrivals 하나, 나가는 길은 ArrowQuiver.TakeNext 하나다.
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        public ArcheryTuning Tuning => DB.Balance.archery;
        public Room Room { get; private set; }
        public PlayerAvatar Avatar { get; private set; }
        public Procedural2DAim Aim { get; private set; }
        public RunState Run { get; private set; }
        public LaunchStyle LaunchType { get; private set; }
        public RetrievalStyle RetrievalType { get; private set; }
        public LaunchBehaviour Launcher { get; private set; }
        public RetrievalBehaviour Retriever { get; private set; }
        public ArrowQuiver Quiver { get; private set; }
        public readonly List<ArrowBody> Bodies = new List<ArrowBody>();
        public bool Active { get; private set; }
        /// <summary>이번 라운드(노드)의 전투 통계</summary>
        public CombatStats Stats { get; private set; } = new CombatStats();

        public int PhantomCount;
        public int PhantomsSpawnedTotal;
        public bool inputEnabled = true;
        public System.Func<CombatInput> InputOverride;
        public CombatInput CurrentInput { get; private set; }

        public readonly List<string> Violations = new List<string>();
        public readonly List<string> LogLines = new List<string>();
        public string LastBatchDescription { get; private set; } = "";
        public int TotalRecoveries { get; private set; }

        public float Attack => Avatar != null ? Avatar.Stats.attack : 1f;
        public float AttackSpeedMultiplier => Mathf.Max(0.25f, 1f + (Avatar != null ? Avatar.Stats.attackSpeedBonus : 0f) + (Launcher != null ? Launcher.AttackSpeedBonus : 0f));
        public bool DualShot => Run != null && Run.HasRelic("dual_shot");
        public Vector2 PlayerCenter => (Vector2)transform.position + Vector2.up * 0.1f;
        public Vector2 MuzzlePosition => Aim != null && Aim.muzzlePoint != null ? (Vector2)Aim.muzzlePoint.position : PlayerCenter + Vector2.up * 0.2f;

        private Collider2D[] playerColliders;
        private readonly List<ArrowBody> pendingArrivals = new List<ArrowBody>();
        private readonly HashSet<string> sourcesThisFrame = new HashSet<string>();
        private readonly HashSet<ArrowBody> consumeAfterHit = new HashSet<ArrowBody>();
        private readonly HashSet<ArrowBody> consumeOnFlightEnd = new HashSet<ArrowBody>();
        private float emptyClickCooldown, discardCooldown, pebbleTimer;
        private int pendingPierceBonus;
        private ArrowDef lastFiredForm;
        private Enemy focusEnemy;
        private int focusStreak;
        private int terrainMask = -1;

        private static readonly List<RaycastHit2D> castResults = new List<RaycastHit2D>(16);

        public void Bind(PlayerAvatar avatar)
        {
            Avatar = avatar;
            Aim = GetComponent<Procedural2DAim>();
            playerColliders = GetComponentsInChildren<Collider2D>(true);
        }

        // ───────────── 라운드(=노드) 시작·종료 ─────────────

        public void BeginRound(Room room, RunState run, LaunchStyle launch, RetrievalStyle retrieval)
        {
            if (Active) EndRound();
            Room = room;
            Run = run;
            LaunchType = launch;
            RetrievalType = retrieval;
            Violations.Clear();
            LogLines.Clear();
            Stats = new CombatStats();
            pendingArrivals.Clear();
            consumeAfterHit.Clear();
            consumeOnFlightEnd.Clear();
            pendingPierceBonus = 0;
            lastFiredForm = null;
            focusEnemy = null;
            focusStreak = 0;
            terrainMask = Layers.TerrainMask | Layers.EnemyMask;

            // 동시 회수 무작위 배치용 난수 (라운드마다 새로)
            Quiver = new ArrowQuiver(Random.Range(1, int.MaxValue));
            var parent = room.transform;
            // 저장된 화살통 순서대로 몸체를 만든다 (빠진 카드는 뒤에)
            var order = new List<ArrowCard>();
            foreach (var uid in run.quiverOrder) { var c = run.Card(uid); if (c != null && !order.Contains(c)) order.Add(c); }
            foreach (var c in run.deck) if (!order.Contains(c)) order.Add(c);
            foreach (var card in order)
            {
                card.ResetRound();
                var go = new GameObject();
                go.transform.SetParent(parent, false);
                var body = go.AddComponent<ArrowBody>();
                body.Setup(this, card);
                Bodies.Add(body);
                Quiver.InitialAdd(body);
            }
            Launcher = LaunchBehaviour.Create(launch, this);
            Retriever = RetrievalBehaviour.Create(retrieval, this);
            Active = true;
            Log($"라운드 시작: [{StyleNames.Launch(launch)}] + [{StyleNames.Retrieval(retrieval)}], 화살 {Bodies.Count}발");
        }

        /// <summary>
        /// 라운드 종료: 필드의 화살과 소멸한 화살을 모두 화살통으로 되돌린다(동시 회수=무작위 배치).
        /// 라운드 한정 효과(예리한·유리·분노·소멸)를 초기화하고 화살통 순서를 도전 상태에 기록한다.
        /// </summary>
        public void EndRound()
        {
            if (!Active) return;
            FlushArrivals();
            var rest = new List<ArrowBody>();
            foreach (var b in Bodies)
            {
                if (b.State == ArrowState.InQuiver) continue;
                if (b.State == ArrowState.Consumed) b.RestoreFromConsumed();
                else b.EnterQuiver();
                rest.Add(b);
            }
            // RestoreFromConsumed / EnterQuiver 로 InQuiver 가 된 몸체를 한 묶음으로 추가
            Quiver.AddRecoveredBatch(rest);
            Run.quiverOrder.Clear();
            foreach (var b in Quiver.Order) Run.quiverOrder.Add(b.Card.uid);
            foreach (var c in Run.deck) c.ResetRound();
            foreach (var b in Bodies) if (b != null) Destroy(b.gameObject);
            Bodies.Clear();
            Launcher = null;
            Retriever = null;
            Active = false;
            Room = null;
        }

        public void Log(string msg)
        {
            LogLines.Add($"[{Time.time:0.00}] {msg}");
            if (LogLines.Count > 40) LogLines.RemoveAt(0);
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
            if (!Active || Launcher == null) return;
            float dt = Time.deltaTime;
            var input = InputOverride != null ? InputOverride() : ReadDeviceInput();
            if (!inputEnabled) input = new CombatInput { aimWorld = input.aimWorld, fireUp = true, recallUp = true };
            CurrentInput = input;

            Launcher.Tick(input, dt);
            Retriever.Tick(input, dt);
            if (input.discardDown) Discard();
            TickPebbles(dt);

            Vector2 p = PlayerCenter;
            for (int i = 0; i < Bodies.Count; i++) Bodies[i].SimulateFrame(dt, p);
            FlushArrivals();
            CheckIntegrity();
            if (emptyClickCooldown > 0f) emptyClickCooldown -= dt;
            if (discardCooldown > 0f) discardCooldown -= dt;
        }

        private void FixedUpdate()
        {
            if (!Active) return;
            Physics2D.SyncTransforms();
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < Bodies.Count; i++) Bodies[i].SimulateFixed(dt);
        }

        private CombatInput ReadDeviceInput()
        {
            var input = new CombatInput();
            Camera cam = Camera.main;
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            var k = Keyboard.current;
            if (m != null && cam != null)
            {
                Vector2 sp = m.position.ReadValue();
                input.aimWorld = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -cam.transform.position.z));
                input.fireDown = m.leftButton.wasPressedThisFrame;
                input.fireHeld = m.leftButton.isPressed;
                input.fireUp = m.leftButton.wasReleasedThisFrame;
                input.discardDown = m.rightButton.wasPressedThisFrame;
            }
            if (k != null)
            {
                input.recallDown = k.rKey.wasPressedThisFrame;
                input.recallHeld = k.rKey.isPressed;
                input.recallUp = k.rKey.wasReleasedThisFrame;
                if (k.qKey.wasPressedThisFrame) input.discardDown = true;
            }
#endif
            return input;
        }

        // ───────────── 발사 ─────────────

        public float DrawMultiplier(ArrowDef def) => Mathf.Max(0f, def.draw) / Mathf.Max(0.01f, Tuning.referenceDrawSeconds);

        /// <summary>발사 간격 계산에 쓰는 '다음 발사의 활시위 배율'. 듀얼샷이면 두 화살의 활시위를 더한다.</summary>
        public float NextDrawMultiplier()
        {
            var a = Quiver.Peek();
            if (a == null) return 1f;
            float m = DrawMultiplier(a.Card.Def);
            if (DualShot)
            {
                var b = Quiver.Peek(1);
                if (b != null) m += DrawMultiplier(b.Card.Def);
            }
            return m;
        }

        public Vector2 AimDirectionFrom(Vector2 origin, Vector2 aimWorld)
        {
            Vector2 d = aimWorld - origin;
            if (d.sqrMagnitude < 0.0001f) d = Aim != null && !Aim.IsFacingRight ? Vector2.left : Vector2.right;
            return d.normalized;
        }

        public void PlayFireFeedback(Vector2 direction)
        {
            if (Aim != null) Aim.PlayShotRecoil(direction);
            Sfx.Play("shoot", 0.6f);
        }

        public void EmptyQuiverFeedback()
        {
            if (emptyClickCooldown > 0f) return;
            emptyClickCooldown = 0.35f;
            Log("화살통이 비어 있음");
            Sfx.Play("empty");
            Fx.Text(PlayerCenter + Vector2.up * 1.2f, "화살 없음", new Color(1f, 0.6f, 0.4f), 22, 0.6f);
        }

        /// <summary>
        /// 화살통 맨 앞 화살을 쏜다 (듀얼샷이면 두 발). 바로 뒤가 에코 화살이면 같이 따라 나간다.
        /// cardDamageToFinal: 카드 피해 → 최종 피해 (기본: +공격력, 오디세우스: ×배율 + 공격력)
        /// </summary>
        public List<ArrowBody> FireFront(Vector2 dir, float gravity, System.Func<float, float> cardDamageToFinal = null)
        {
            var fired = new List<ArrowBody>();
            int count = DualShot ? 2 : 1;
            for (int k = 0; k < count; k++)
            {
                var body = Quiver.TakeNext();
                if (body == null) break;
                Vector2 d = k == 0 ? dir : (Vector2)(Quaternion.Euler(0, 0, 4f) * dir);
                FireBody(body, d, gravity, cardDamageToFinal, null);
                fired.Add(body);
                FollowWithEcho(body, d, gravity, cardDamageToFinal);
            }
            if (fired.Count > 0) PlayFireFeedback(dir);
            return fired;
        }

        private void FollowWithEcho(ArrowBody leader, Vector2 dir, float gravity, System.Func<float, float> dmgFn)
        {
            var next = Quiver.Peek();
            if (next == null || next.Card.Def.effect != "echo" || leader.Card.Def.effect == "echo") return;
            var echo = Quiver.TakeNext();
            FireBody(echo, Quaternion.Euler(0, 0, -3f) * dir, gravity, dmgFn, leader.Shot != null ? leader.Shot.form : leader.Card.Def);
            Log($"에코: {echo.Label} 가 {leader.Card.Def.name} 을(를) 따라 발사");
        }

        public ShotInfo MakeShot(ArrowBody body, ArrowDef copyForm, System.Func<float, float> dmgFn)
        {
            var card = body.Card;
            var form = card.Def;
            bool isEcho = form.effect == "echo";
            if (isEcho)
            {
                form = copyForm ?? lastFiredForm ?? DB.Arrow(0);
                if (form.effect == "echo") form = DB.Arrow(0);
            }
            float cardDamage = isEcho ? form.damage : card.Damage;
            var shot = new ShotInfo { form = form, isEcho = isEcho };
            shot.damage = dmgFn != null ? dmgFn(cardDamage) : HitResolver.NormalDamage(cardDamage, Attack);
            switch (form.effect)
            {
                case "fast": shot.speedMul = form.p1 > 0 ? form.p1 : 1.8f; break;
                case "icarus": shot.speedMul = form.p1 > 0 ? form.p1 : 1.7f; shot.pierce = Mathf.RoundToInt(form.p2 > 0 ? form.p2 : 3); break;
                case "pierce":
                case "ares": shot.pierce = Mathf.RoundToInt(form.p1 > 0 ? form.p1 : 3); break;
                case "bowling": shot.bounces = Mathf.RoundToInt(form.p1 > 0 ? form.p1 : 3); break;
                case "wraith": shot.redirects = Mathf.RoundToInt(form.p1 > 0 ? form.p1 : 3); break;
            }
            if (pendingPierceBonus > 0)
            {
                shot.pierce += pendingPierceBonus;
                Log($"강화: {body.Label} 관통 +{pendingPierceBonus}");
                pendingPierceBonus = 0;
            }
            return shot;
        }

        /// <summary>화살 한 장을 실제로 발사한다 (발사 시 효과 포함)</summary>
        public void FireBody(ArrowBody body, Vector2 dir, float gravity, System.Func<float, float> dmgFn, ArrowDef copyForm)
        {
            var card = body.Card;
            var shot = MakeShot(body, copyForm, dmgFn);
            var form = shot.form;
            Stats.shots++;

            // 사용 시 비용·소모
            if (card.Def.effect == "blood") Avatar?.SpendHp(card.Def.p1 > 0 ? card.Def.p1 : 2f, "혈 화살");
            if (card.Def.effect == "bomb" || card.Def.effect == "sacrifice" || card.baseCode == 41) consumeOnFlightEnd.Add(body);

            if (form.effect == "hitscan")
            {
                DoHitscan(body, shot, dir);
            }
            else
            {
                float range = gravity > 0f ? float.MaxValue : Tuning.straightMaxRange;
                float delay = form.effect == "trigger" ? (form.p1 > 0 ? form.p1 : 1.5f) : 0f;
                body.Launch(MuzzlePosition, dir, Tuning.arrowSpeed * shot.speedMul, gravity, range, shot, delay);
            }
            SpawnOnFire(body, shot, dir, gravity);
            lastFiredForm = form;
            Log($"발사: {body.Label}{(shot.isEcho ? $" (에코: {form.name})" : "")} 피해 {shot.damage:0.#}");
        }

        /// <summary>발사 순간 추가로 나가는 임시 투사체 (카드가 아니므로 화살통에 들어오지 않는다)</summary>
        private void SpawnOnFire(ArrowBody body, ShotInfo shot, Vector2 dir, float gravity)
        {
            var form = shot.form;
            Vector2 o = MuzzlePosition;
            float speed = Tuning.arrowSpeed * shot.speedMul;
            switch (form.effect)
            {
                case "triple":
                    {
                        float a = form.p1 > 0 ? form.p1 : 12f;
                        Phantom.Spawn(this, form, o, Quaternion.Euler(0, 0, a) * dir, speed, shot.damage, Tuning.straightMaxRange, "삼살")?.SetGravity(gravity);
                        Phantom.Spawn(this, form, o, Quaternion.Euler(0, 0, -a) * dir, speed, shot.damage, Tuning.straightMaxRange, "삼살")?.SetGravity(gravity);
                        break;
                    }
                case "mirror":
                    {
                        Vector2 md = new Vector2(-dir.x, dir.y);
                        Vector2 mo = new Vector2(2f * PlayerCenter.x - o.x, o.y);
                        Phantom.Spawn(this, form, mo, md, speed, shot.damage, Tuning.straightMaxRange, "거울 화살")?.SetGravity(gravity);
                        break;
                    }
                case "splitter":
                    Phantom.Spawn(this, form, o, Quaternion.Euler(0, 0, Random.Range(-8f, 8f)) * dir, speed * 0.9f, (form.p1 > 0 ? form.p1 : 7f) + Attack, Tuning.straightMaxRange, "분열체")?.SetGravity(gravity);
                    break;
                case "hades":
                    {
                        var consumed = new List<ArrowBody>();
                        foreach (var b in Bodies) if (b.State == ArrowState.Consumed) consumed.Add(b);
                        float spread = form.p1 > 0 ? form.p1 : 30f;
                        for (int i = 0; i < consumed.Count; i++)
                        {
                            float a = consumed.Count == 1 ? 0f : Mathf.Lerp(-spread * 0.5f, spread * 0.5f, i / (consumed.Count - 1f));
                            var def = consumed[i].Card.Def;
                            Phantom.Spawn(this, def, o, Quaternion.Euler(0, 0, a) * dir, Tuning.arrowSpeed, HitResolver.NormalDamage(consumed[i].Card.Damage, Attack), Tuning.straightMaxRange, "하데스: " + def.name);
                        }
                        Log($"하데스 화살: 이번 라운드에 소멸한 화살 {consumed.Count}발을 재발사 (추가 소멸 없음)");
                        break;
                    }
            }
        }

        private void DoHitscan(ArrowBody body, ShotInfo shot, Vector2 dir)
        {
            float range = shot.form.p1 > 0 ? shot.form.p1 : 30f;
            Vector2 from = MuzzlePosition;
            Vector2 to = from + dir * range;
            if (CastSegment(from, to, true, null, out RaycastHit2D hit, out Enemy enemy))
            {
                Fx.Polyline(new List<Vector3> { from, hit.point }, shot.form.tint, 0.08f, 0.2f);
                body.PlaceInstant(hit.point, dir, shot);
                if (enemy != null) OnArrowHitEnemy(body, enemy, hit.collider.transform, hit.point, HitSource.Hitscan);
                else { body.Drop(Vector2.zero); OnFlightEnded(body, hit.point); }
            }
            else
            {
                Fx.Polyline(new List<Vector3> { from, to }, shot.form.tint, 0.08f, 0.2f);
                body.PlaceInstant(to, dir, shot);
                body.Drop(Vector2.zero);
                OnFlightEnded(body, to);
            }
        }

        /// <summary>찌르기 적중 (아레스 발사 궁술)</summary>
        public void ThrustHit(Enemy enemy, Transform part, Vector2 point, Vector2 dir)
        {
            int count = DualShot ? 2 : 1;
            for (int k = 0; k < count; k++)
            {
                if (!enemy.IsAlive) break;
                var body = Quiver.TakeNext();
                if (body == null) break;
                ThrustOne(body, enemy, part, point, dir, null);
                var next = Quiver.Peek();
                if (next != null && next.Card.Def.effect == "echo" && body.Card.Def.effect != "echo" && enemy.IsAlive)
                {
                    var echo = Quiver.TakeNext();
                    ThrustOne(echo, enemy, part, point, dir, body.Shot != null ? body.Shot.form : body.Card.Def);
                }
            }
            PlayFireFeedback(dir);
        }

        private void ThrustOne(ArrowBody body, Enemy enemy, Transform part, Vector2 point, Vector2 dir, ArrowDef copy)
        {
            var shot = MakeShot(body, copy, null);
            Stats.shots++;
            if (body.Card.Def.effect == "blood") Avatar?.SpendHp(body.Card.Def.p1 > 0 ? body.Card.Def.p1 : 2f, "혈 화살");
            if (body.Card.Def.effect == "bomb" || body.Card.Def.effect == "sacrifice" || body.Card.baseCode == 41) consumeOnFlightEnd.Add(body);
            body.PlaceInstant(point, dir, shot);
            OnArrowHitEnemy(body, enemy, part, point, HitSource.Thrust);
            SpawnOnFire(body, shot, dir, 0f);
            lastFiredForm = shot.form;
        }

        private void TickPebbles(float dt)
        {
            if (pebbleTimer > 0f) pebbleTimer -= dt;
            var front = Quiver.Peek();
            if (front == null || front.Card.baseCode != 41 || pebbleTimer > 0f || !inputEnabled) return;
            Vector2 dir = AimDirectionFrom(MuzzlePosition, CurrentInput.aimWorld);
            var body = Quiver.TakeNext();
            FireBody(body, dir, 0f, null, null);
            pebbleTimer = Mathf.Max(Tuning.minInterval, Tuning.apolloBaseInterval * DrawMultiplier(body.Card.Def) / AttackSpeedMultiplier);
        }

        // ───────────── 버리기 ─────────────

        public bool Discard()
        {
            if (discardCooldown > 0f) return false;
            discardCooldown = 0.2f;
            var body = Quiver.TakeNext();
            if (body == null) { EmptyQuiverFeedback(); return false; }
            var card = body.Card;
            Stats.discards++;
            float facing = Aim != null && !Aim.IsFacingRight ? -1f : 1f;
            if (card.Def.effect == "splitter")
            {
                body.Consume();
                Stats.consumed++;
                Log($"버리기: {body.Label} — 분열체는 버리면 이번 라운드 동안 소멸");
            }
            else
            {
                body.Toss(PlayerCenter + new Vector2(facing * 0.4f, 0.3f), new Vector2(facing * Tuning.discardToss, 4f));
                Log($"버리기: {body.Label}");
            }
            if (card.Def.effect == "empower")
            {
                pendingPierceBonus += Mathf.RoundToInt(card.Def.p1 > 0 ? card.Def.p1 : 1);
                Log($"강화 화살: 다음 화살 관통 +{pendingPierceBonus}");
                Fx.Text(PlayerCenter + Vector2.up, "다음 화살 관통+", new Color(1f, 0.7f, 0.5f), 20);
            }
            var poker = Run?.RelicWithEffect("discardHit");
            if (poker != null && Room != null)
            {
                var targets = new List<Enemy>();
                foreach (var e in Room.Enemies) if (e != null && e.IsAlive && e.DistanceFrom(PlayerCenter) <= poker.p1) targets.Add(e);
                if (targets.Count > 0)
                {
                    var t = targets[Random.Range(0, targets.Count)];
                    float dmg = HitResolver.NormalDamage(card.Damage, Attack);
                    Fx.Lightning(PlayerCenter, t.Center, card.Def.tint, 0.07f, 0.25f);
                    t.lastArrowForm = card.Def;
                    t.TakeDamage(dmg, "화살 찌르게", card.Def.tint, true);
                    HitResolver.ApplyOnHit(this, card.Def, t, t.Center, (t.Center - (Vector3)PlayerCenter).normalized, card, dmg, false, null);
                    Log($"화살 찌르게: 버린 {card.Def.name} 이(가) {t.DisplayName}에게 적중");
                }
            }
            Sfx.Play("swing", 0.4f);
            return true;
        }

        // ───────────── 적중·비행 종료 ─────────────

        public HitOutcome OnArrowHitEnemy(ArrowBody body, Enemy enemy, Transform part, Vector2 point, HitSource src)
        {
            var outcome = HitResolver.Resolve(this, body, enemy, part, point, src);
            if (outcome.continueFlight) return outcome;

            if (consumeAfterHit.Remove(body) | (consumeOnFlightEnd.Remove(body)))
            {
                body.Consume();
                Stats.consumed++;
                Log($"{body.Label}: 이번 라운드 동안 소멸");
                return outcome;
            }
            if (src == HitSource.Pull) return outcome;
            if (Retriever.ArrowsStickToEnemies && enemy.IsAlive)
            {
                body.StickTo(enemy, part, point);
            }
            else
            {
                Vector2 bounce = new Vector2(-body.LastDirection.x * 2.5f, 5f);
                body.Drop(bounce);
            }
            return outcome;
        }

        public void MarkConsumeAfterHit(ArrowBody body, string reason)
        {
            if (consumeAfterHit.Add(body)) Log($"{body.Label}: {reason} → 소멸 예정");
        }

        public bool ShouldConsumeNow(ArrowBody body) => consumeAfterHit.Contains(body);

        public void ConsumeNow(ArrowBody body)
        {
            consumeAfterHit.Remove(body);
            consumeOnFlightEnd.Remove(body);
            body.Consume();
            Stats.consumed++;
        }

        /// <summary>적을 맞추지 못하고 비행이 끝났을 때 (지형에 꽂힘·사거리 초과)</summary>
        public void OnFlightEnded(ArrowBody body, Vector2 point)
        {
            var form = body.Shot != null ? body.Shot.form : body.Card.Def;
            if (!body.AnyEnemyHitThisFlight && body.Card.Def.effect == "keen")
            {
                body.Card.roundBonus += body.Card.Def.p1 > 0 ? body.Card.Def.p1 : 6f;
                Log($"{body.Label}: 빗나감 — 이번 라운드 피해 +{body.Card.Def.p1:0}");
            }
            if (form.effect == "bomb" || form.effect == "explode")
            {
                float r = form.p1 > 0 ? form.p1 : 1.6f;
                float dmg = body.Shot != null ? body.Shot.damage : HitResolver.NormalDamage(body.Card.Damage, Attack);
                Room?.Explode(point, r, dmg, null, form.tint, form.name);
            }
            if (consumeOnFlightEnd.Remove(body) || consumeAfterHit.Remove(body))
            {
                body.Consume();
                Stats.consumed++;
                Log($"{body.Label}: 이번 라운드 동안 소멸");
            }
        }

        public void OnFlightEndedOnTerrain(ArrowBody body, Vector2 point)
        {
            Sfx.Play("wall", 0.4f);
            OnFlightEnded(body, point);
        }

        // ───────────── 일점타격 ─────────────

        public float FocusBonus(Enemy e)
        {
            var r = Run?.RelicWithEffect("focus");
            if (r == null || e == null || e != focusEnemy) return 0f;
            return Mathf.Min(focusStreak * r.p1, r.p2 > 0 ? r.p2 : 6f);
        }

        public void RegisterFocusHit(Enemy e)
        {
            if (e == focusEnemy) focusStreak++;
            else { focusEnemy = e; focusStreak = 1; }
        }

        // ───────────── 회수 (화살통으로 들어가는 유일한 경로) ─────────────

        public bool TryRecover(ArrowBody body, string source)
        {
            if (body == null || !Active) return false;
            if (pendingArrivals.Contains(body)) return false;
            switch (body.State)
            {
                case ArrowState.Grounded:
                case ArrowState.Stuck:
                case ArrowState.Falling:
                case ArrowState.Returning:
                    break;
                default:
                    return false; // InQuiver(중복), Flying/Delayed(비행 중), Consumed(소멸) 거부
            }
            if (Quiver.Contains(body)) { ReportViolation($"{body.Label}: 화살통에 있는데 필드 상태({body.State})"); return false; }
            body.EnterQuiver();
            pendingArrivals.Add(body);
            sourcesThisFrame.Add(source);
            TotalRecoveries++;
            Stats.recoveries++;
            Sfx.Play("pick", 0.5f);
            return true;
        }

        public void NotifyReturnArrived(ArrowBody body) => TryRecover(body, body.ReturnSource ?? "귀환");

        private void FlushArrivals()
        {
            if (pendingArrivals.Count == 0) return;
            var batch = new List<ArrowBody>(pendingArrivals);
            pendingArrivals.Clear();
            var added = Quiver.AddRecoveredBatch(batch);
            var sb = new StringBuilder();
            for (int i = 0; i < added.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(added[i].Label); }
            string src = string.Join("/", sourcesThisFrame);
            sourcesThisFrame.Clear();
            LastBatchDescription = added.Count > 1 ? $"동시 회수 {added.Count}발({src}) → 무작위 배치: {sb}" : $"순차 회수({src}) → 맨 뒤: {sb}";
            Log(LastBatchDescription);
        }

        // ───────────── 충돌 판정 ─────────────

        /// <summary>선분과 겹치는 첫 지형/적. 플레이어·이미 맞춘 적·죽은 적·지형 아닌 트리거는 무시.</summary>
        public bool CastSegment(Vector2 from, Vector2 to, bool includeEnemies, HashSet<Enemy> exclude, out RaycastHit2D best, out Enemy bestEnemy)
        {
            best = default;
            bestEnemy = null;
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(includeEnemies ? (Layers.TerrainMask | Layers.EnemyMask) : Layers.TerrainMask);
            castResults.Clear();
            int n = Physics2D.Linecast(from, to, filter, castResults);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = castResults[i];
                if (h.collider == null || IsPlayerCollider(h.collider)) continue;
                Enemy e = h.collider.GetComponentInParent<Enemy>();
                if (e != null)
                {
                    if (!includeEnemies || !e.IsAlive || (exclude != null && exclude.Contains(e))) continue;
                }
                else if (h.collider.isTrigger) continue;
                else if (h.collider.usedByEffector && to.y > from.y + 0.0001f) continue; // 한쪽 통과 발판: 아래→위는 통과
                if (h.distance < bestDist) { bestDist = h.distance; best = h; bestEnemy = e; found = true; }
            }
            return found;
        }

        public Vector2 SafeGroundPoint() => Room != null ? Room.PlayerSpawn : (Vector2)transform.position;

        // ───────────── 무결성 ─────────────

        public int CountState(ArrowState s)
        {
            int n = 0;
            for (int i = 0; i < Bodies.Count; i++) if (Bodies[i].State == s) n++;
            return n;
        }

        public void CheckIntegrity()
        {
            if (Quiver.HasDuplicates()) ReportViolation("화살통에 같은 화살이 중복으로 들어 있음");
            int inQ = CountState(ArrowState.InQuiver);
            int pending = pendingArrivals.Count;
            if (inQ - pending != Quiver.Count) ReportViolation($"화살통 수({Quiver.Count})와 InQuiver 상태 수({inQ - pending}) 불일치");
            if (Run != null && Bodies.Count != Run.deck.Count) ReportViolation($"화살 몸체 수({Bodies.Count})와 덱 수({Run.deck.Count}) 불일치");
        }

        public void ReportViolation(string msg)
        {
            if (Violations.Count < 100) Violations.Add(msg);
            Log("<무결성 위반> " + msg);
            Debug.LogWarning("[Laurel] 무결성 위반: " + msg);
        }

        public string QuiverSummary(int max = 12)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Quiver.Count && i < max; i++) { if (i > 0) sb.Append(" › "); sb.Append(Quiver.Order[i].Card.Def.name); }
            if (Quiver.Count > max) sb.Append($" … (+{Quiver.Count - max})");
            return sb.ToString();
        }
    }

    public static class PhantomExt
    {
        public static void SetGravity(this Phantom p, float g) { if (p != null) p.gravity = g; }
    }

    /// <summary>레이어 이름 → 마스크 (프로젝트 설정의 Terrain/Enemy 레이어)</summary>
    public static class Layers
    {
        private static int terrain = -2, enemy = -2;
        public static int Terrain { get { if (terrain == -2) terrain = LayerMask.NameToLayer("Terrain"); return terrain < 0 ? 0 : terrain; } }
        public static int Enemy { get { if (enemy == -2) enemy = LayerMask.NameToLayer("Enemy"); return enemy < 0 ? 0 : enemy; } }
        public static int TerrainMask => 1 << Terrain;
        public static int EnemyMask => 1 << Enemy;
    }
}
