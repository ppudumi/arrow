using Procedural2D;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 기존 플레이어 프리팹(ProceduralCharacter)에 붙는 게임 연결부.
    /// 외형·이동·점프·대쉬는 기존 ProceduralCharacterController 를 그대로 쓰고,
    /// 여기서는 체력·피격·회복과 '구매 상태에 따른 유니크 스킬 활성화'만 담당한다.
    /// </summary>
    public class PlayerAvatar : MonoBehaviour
    {
        public PlayerStats Stats { get; private set; }
        public float Hp { get; private set; }
        public float MaxHp => Stats.maxHp;
        public bool Dead { get; private set; }
        public bool invulnerable;
        public System.Func<bool> TryRevive;
        public System.Action OnDeath;
        public System.Action<float> OnHurt;
        public ProceduralCharacterController Controller { get; private set; }
        public Procedural2DAim Aim { get; private set; }
        public Rigidbody2D Body { get; private set; }
        public float BaseMoveSpeed { get; private set; }
        public float DamageTakenTotal { get; private set; }

        private SpriteRenderer[] renderers;
        private Color[] baseColors;
        private float iframes, regenTimer;

        public void Setup()
        {
            Controller = GetComponent<ProceduralCharacterController>();
            Aim = GetComponent<Procedural2DAim>();
            Body = GetComponent<Rigidbody2D>();
            BaseMoveSpeed = Controller.moveSpeed;
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;
        }

        /// <summary>능력치 적용. 더블점프·대쉬·3단 점프는 구매(또는 튜토리얼) 상태에 따라 켜고 끈다.</summary>
        public void ApplyStats(PlayerStats s, bool refill)
        {
            float oldMax = Stats.maxHp;
            Stats = s;
            Controller.moveSpeed = BaseMoveSpeed * Mathf.Max(0.3f, s.moveMul);
            Controller.maxJumps = s.MaxJumps;
            Controller.dashUnlocked = s.dash;
            if (refill) Hp = s.maxHp;
            else if (oldMax > 0f && s.maxHp > oldMax) Hp += s.maxHp - oldMax;
            Hp = Mathf.Min(Hp, s.maxHp);
            Dead = false;
        }

        public void SetHp(float hp) { Hp = Mathf.Clamp(hp, 0f, MaxHp); }

        public void SetInputEnabled(bool on)
        {
            Controller.inputEnabled = on;
            if (Aim != null) Aim.inputEnabled = on;
            var pc = GetComponent<PlayerCombat>();
            if (pc != null) pc.inputEnabled = on;
        }

        public void Teleport(Vector2 pos)
        {
            transform.position = pos;
            Controller.ResetMotionState();
            if (Body != null) Body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            var ribbon = GetComponent<ProceduralWaistRibbon>();
            if (ribbon != null) ribbon.SnapToAnchor();
            var skirt = GetComponent<ProceduralSkirtPhysics>();
            if (skirt != null) skirt.ResetMotion();
        }

        /// <summary>피해를 받는다. 방어력만큼 줄이고(최소 1) 무적시간을 준다. 실제로 맞았으면 true.</summary>
        public bool Hurt(float dmg, string source)
        {
            if (Dead || invulnerable || iframes > 0f || !isActiveAndEnabled) return false;
            float final = Mathf.Max(1f, dmg - Stats.defense);
            Hp -= final;
            DamageTakenTotal += final;
            iframes = DB.Balance.player.iframes;
            Fx.Text(transform.position + Vector3.up * 1.4f, "-" + final.ToString("0"), new Color(1f, 0.35f, 0.35f), 30);
            Fx.Burst(transform.position, new Color(1f, 0.3f, 0.3f), 8, 5f, 0.15f, 0.35f);
            Sfx.Play("hurt", 0.8f);
            CameraRig.Shake(0.18f);
            if (Body != null) Body.linearVelocity = new Vector2(Body.linearVelocity.x, Mathf.Max(Body.linearVelocity.y, 6f));
            OnHurt?.Invoke(final);
            if (Hp <= 0f)
            {
                if (TryRevive != null && TryRevive())
                {
                    Hp = MaxHp * 0.5f;
                    iframes = 2f;
                    Fx.Text(transform.position + Vector3.up * 2f, "불사조의 깃 — 부활!", new Color(1f, 0.7f, 0.3f), 30, 1.5f);
                    Fx.Burst(transform.position, new Color(1f, 0.6f, 0.2f), 30, 8f, 0.25f, 0.8f);
                }
                else
                {
                    Hp = 0f;
                    Dead = true;
                    OnDeath?.Invoke();
                }
            }
            return true;
        }

        public void Heal(float amount, string label)
        {
            if (Dead || amount <= 0f) return;
            float before = Hp;
            Hp = Mathf.Min(MaxHp, Hp + amount);
            if (Hp > before) Fx.Text(transform.position + Vector3.up * 1.4f, "+" + (Hp - before).ToString("0"), new Color(0.4f, 1f, 0.5f), 24);
        }

        /// <summary>혈 화살처럼 스스로 체력을 쓴다 (이것으로는 죽지 않는다: 최소 1)</summary>
        public void SpendHp(float amount, string label)
        {
            if (Dead) return;
            Hp = Mathf.Max(1f, Hp - amount);
            Fx.Text(transform.position + Vector3.up * 1.4f, "-" + amount.ToString("0"), new Color(0.8f, 0.1f, 0.1f), 20);
        }

        public void ClearIframes() { iframes = 0f; }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (iframes > 0f)
            {
                iframes -= dt;
                bool vis = iframes <= 0f || Mathf.PingPong(Time.time * 18f, 1f) > 0.35f;
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].color = vis ? baseColors[i] : baseColors[i] * new Color(1f, 0.5f, 0.5f, 0.5f);
            }
            if (Stats.regenPer5s > 0f && !Dead)
            {
                regenTimer += dt;
                if (regenTimer >= 5f) { regenTimer = 0f; if (Hp < MaxHp) Heal(Stats.regenPer5s, "재생"); }
            }
            // 방 밖으로 떨어지는 사고 방지
            if (transform.position.y < -60f) Teleport(Vector2.zero);
        }
    }
}
