using Procedural2D;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 플레이어 파츠 시트(player_parts.png)의 스프라이트를 상태에 맞게 바꿔 끼운다.
    /// 위치·회전·흔들림은 기존 Procedural2D 리그가 그대로 담당하고, 여기서는 '어떤 그림을 보일지'만 고른다.
    /// - 하의: 대기 / 점프(상승·낙하 공통) / 2단 점프 회전 / 달리기 3프레임
    /// - 오른팔: 쏘는 중(시위 당김·발사 직후)에는 굽은 팔, 그 외에는 내린 팔
    /// - 머리: 조준 각도(아래·정면·위)에 따라 3종
    /// </summary>
    public class PlayerPartsAnimator : MonoBehaviour
    {
        [Header("하의")]
        public SpriteRenderer skirtRenderer;
        public Sprite skirtIdle, skirtJump, skirtSpin;
        public Sprite[] skirtRun = new Sprite[3];
        [Tooltip("기본 이동 속도로 달릴 때 초당 달리기 프레임 수")]
        public float runFps = 10f;
        [Tooltip("이 속도(유닛/초)보다 빠르면 달리는 그림을 쓴다")]
        public float runThreshold = 0.6f;

        [Header("오른팔")]
        public SpriteRenderer rightArmDraw;
        public SpriteRenderer rightArmRelaxed;
        [Tooltip("발사 후 굽은 팔을 유지하는 시간(초)")]
        public float drawHoldAfterShot = 0.45f;

        [Header("머리 (아래 / 정면 / 위)")]
        public SpriteRenderer headRenderer;
        public Sprite headDown, headLevel, headUp;
        [Tooltip("이 각도보다 위·아래를 조준하면 머리를 바꾼다")]
        public float headTiltAngle = 28f;

        private ProceduralCharacterController ctrl;
        private Procedural2DAim aim;
        private Rigidbody2D rb;
        private PlayerCombat combat;
        private float runPhase;
        private int lastShots = -1;
        private float drawTimer;

        private void Awake()
        {
            ctrl = GetComponent<ProceduralCharacterController>();
            aim = GetComponent<Procedural2DAim>();
            rb = GetComponent<Rigidbody2D>();
        }

        private void LateUpdate()
        {
            if (combat == null) combat = GetComponent<PlayerCombat>(); // 게임이 런타임에 붙인다
            UpdateSkirt();
            UpdateRightArm();
            UpdateHead();
        }

        private void UpdateSkirt()
        {
            if (skirtRenderer == null) return;
            bool grounded = ctrl == null || ctrl.IsGrounded;
            float vx = rb != null ? Mathf.Abs(rb.linearVelocity.x) : 0f;
            Sprite s;
            if (ctrl != null && ctrl.IsSpinning) s = skirtSpin;
            else if (!grounded) s = skirtJump;
            else if (vx > runThreshold && skirtRun != null && skirtRun.Length > 0)
            {
                float baseSpeed = ctrl != null ? Mathf.Max(0.1f, ctrl.moveSpeed) : 10f;
                runPhase += Time.deltaTime * runFps * Mathf.Clamp(vx / baseSpeed, 0.5f, 1.6f);
                s = skirtRun[(int)runPhase % skirtRun.Length];
            }
            else { s = skirtIdle; runPhase = 0f; }
            if (s != null) skirtRenderer.sprite = s;
        }

        private void UpdateRightArm()
        {
            if (rightArmDraw == null || rightArmRelaxed == null) return;
            bool drawing = false;
            if (combat != null && combat.Active)
            {
                // 라운드마다 통계가 새로 만들어지므로 늘었을 때만 발사로 본다
                if (lastShots >= 0 && combat.Stats.shots > lastShots) drawTimer = drawHoldAfterShot;
                lastShots = combat.Stats.shots;
                drawing = combat.CurrentInput.fireHeld;
            }
            if (drawTimer > 0f) { drawTimer -= Time.deltaTime; drawing = true; }
            rightArmDraw.enabled = drawing;
            rightArmRelaxed.enabled = !drawing;
        }

        private void UpdateHead()
        {
            if (headRenderer == null || aim == null) return;
            float a = aim.CurrentAimAngle;
            var s = a > headTiltAngle ? headUp : (a < -headTiltAngle ? headDown : headLevel);
            if (s != null) headRenderer.sprite = s;
        }
    }
}
