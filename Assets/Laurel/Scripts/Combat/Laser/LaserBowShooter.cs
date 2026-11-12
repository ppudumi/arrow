using Procedural2D;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Laurel
{
    /// <summary>레이저 사격으로 맞을 수 있는 대상 (허수아비 등)</summary>
    public interface ILaserTarget
    {
        /// <summary>맞았을 때. point = 맞은 지점, dir = 사격 방향</summary>
        void TakeLaserHit(int damage, bool critical, Vector2 point, Vector2 dir);
    }

    /// <summary>
    /// 데미지 난수: 기본값 × [최소, 최대] 배율, 일정 확률로 치명타(배율 추가).
    /// seed 를 0 이 아닌 값으로 두면 같은 순서로 나온다 (테스트용).
    /// </summary>
    [System.Serializable]
    public class DamageRoller
    {
        public int baseDamage = 22;
        [Tooltip("일반 피해 배율 범위 (기본값에 곱함)")]
        public Vector2 multiplierRange = new Vector2(0.35f, 1.3f);
        [Range(0f, 1f)] public float critChance = 0.12f;
        public float critMultiplier = 3f;
        [Tooltip("0 이면 매번 다른 난수, 0 이 아니면 고정 순서")]
        public int seed = 0;

        private System.Random rng;

        public int Roll(out bool critical)
        {
            if (rng == null) rng = seed != 0 ? new System.Random(seed) : new System.Random(System.Guid.NewGuid().GetHashCode());
            float m = Mathf.Lerp(multiplierRange.x, multiplierRange.y, (float)rng.NextDouble());
            critical = rng.NextDouble() < critChance;
            float dmg = baseDamage * m * (critical ? critMultiplier : 1f);
            return Mathf.Max(1, Mathf.RoundToInt(dmg));
        }
    }

    /// <summary>
    /// 레이저 활 (테스트용 새 사격 방식, 기존 PlayerCombat·ArrowBody 와 무관).
    /// - 마우스 왼쪽 버튼: 누르면 바로 한 발, 누르고 있으면 fireInterval 마다 연사
    /// - 화살이 날아가지 않는다: 활(Muzzle_Point)에서 조준 방향으로 즉시 판정(레이캐스트)하고 레이저 이펙트만 그린다
    /// - 맞은 대상(ILaserTarget)은 데미지·깜빡임·박힌 화살·데미지 숫자를 처리한다. 지형에 맞으면 불꽃만
    /// - 쏠 때 기존 조준 리그의 반동 연출을 쓰고, 시위 당긴 팔을 잠깐 보여 준다
    /// 튜토리얼 보스맵 플레이어 Variant 에만 붙어 있다.
    /// </summary>
    [DefaultExecutionOrder(50)] // 조준(Procedural2DAim)이 방향을 정하고, 파츠 애니메이터가 팔을 정한 뒤
    public class LaserBowShooter : MonoBehaviour
    {
        [Header("사격")]
        [Tooltip("누르고 있을 때 연사 간격 (초)")]
        public float fireInterval = 0.09f;
        public float range = 40f;
        [Tooltip("판정에서 뺄 레이어 (화면 가장자리 보이지 않는 벽 등)")]
        public LayerMask ignoreLayers = 1 << 2; // Ignore Raycast
        public DamageRoller damage = new DamageRoller();

        [Header("연출")]
        [Tooltip("쏜 뒤 시위 당긴 팔을 보여 주는 시간")]
        public float drawArmHold = 0.12f;

        /// <summary>테스트용: 값이 있으면 마우스 대신 이 값으로 발사 버튼 상태를 정한다</summary>
        public System.Func<bool> FireOverride;
        public int ShotCount { get; private set; }
        public int HitCount { get; private set; }
        public int LastDamage { get; private set; }
        public bool LastCritical { get; private set; }

        private Procedural2DAim aim;
        private ProceduralCharacterController ctrl;
        private PlayerPartsAnimator parts;
        private float nextFire;
        private float drawTimer;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];

        private void Awake()
        {
            aim = GetComponent<Procedural2DAim>();
            ctrl = GetComponent<ProceduralCharacterController>();
            parts = GetComponent<PlayerPartsAnimator>();
        }

        private void Update()
        {
            bool held = FireOverride != null ? FireOverride() : MouseHeld();
            if (ctrl != null && !ctrl.inputEnabled) held = false;
            if (held && Time.time >= nextFire)
            {
                nextFire = Time.time + fireInterval;
                Fire();
            }
            if (!held) nextFire = Mathf.Min(nextFire, Time.time); // 다시 누르면 바로 쏜다
        }

        private static bool MouseHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            return m != null && m.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        /// <summary>한 발 쏜다 (즉시 판정)</summary>
        public void Fire()
        {
            if (aim == null) return;
            Vector2 origin = aim.muzzlePoint != null ? (Vector2)aim.muzzlePoint.position : (Vector2)transform.position;
            Vector2 dir = aim.AimDirection.sqrMagnitude > 0.01f ? aim.AimDirection.normalized : Vector2.right;
            ShotCount++;

            var filter = new ContactFilter2D { useTriggers = true, useLayerMask = true, layerMask = ~ignoreLayers };
            int n = Physics2D.Raycast(origin, dir, filter, hits, range);
            Vector2 end = origin + dir * range;
            ILaserTarget target = null;
            bool hitSolid = false;
            // 가까운 순으로: 대상이면 맞히고, 대상 아닌 트리거는 지나가고, 지형이면 멈춘다
            System.Array.Sort(hits, 0, n, HitComparer.I);
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                var t = h.collider.GetComponentInParent<ILaserTarget>();
                if (t != null) { target = t; end = h.point; break; }
                if (h.collider.isTrigger) continue;
                hitSolid = true; end = h.point; break;
            }

            LaserFx.Shot(origin, end, dir);
            if (target != null)
            {
                int dmg = damage.Roll(out bool crit);
                LastDamage = dmg; LastCritical = crit; HitCount++;
                target.TakeLaserHit(dmg, crit, end, dir);
                LaserFx.Impact(end, dir, true);
            }
            else if (hitSolid) LaserFx.Impact(end, dir, false);

            // 소리: 기존 Sfx 의 사격음(짧고 높게), 지형에 맞으면 "wall". 대상 타격음은 대상이 낸다
            Sfx.Play("shoot", 0.35f, Random.Range(1.35f, 1.55f));
            if (target == null && hitSolid) Sfx.Play("wall", 0.4f);

            aim.PlayShotRecoil(dir);
            drawTimer = drawArmHold;
        }

        private void LateUpdate()
        {
            // 쏜 직후 시위 당긴 팔 (PlayerPartsAnimator 가 매 프레임 정하는 값을 이번 프레임만 덮어씀)
            if (drawTimer > 0f)
            {
                drawTimer -= Time.deltaTime;
                if (parts != null && parts.rightArmDraw != null && parts.rightArmRelaxed != null)
                {
                    parts.rightArmDraw.enabled = true;
                    parts.rightArmRelaxed.enabled = false;
                }
            }
        }

        private class HitComparer : System.Collections.Generic.IComparer<RaycastHit2D>
        {
            public static readonly HitComparer I = new HitComparer();
            public int Compare(RaycastHit2D a, RaycastHit2D b) => a.distance.CompareTo(b.distance);
        }
    }
}
