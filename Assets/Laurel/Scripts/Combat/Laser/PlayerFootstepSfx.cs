using Procedural2D;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 걷는 소리 (사각사각): 땅 위에서 실제로 움직이는 동안 일정 간격으로 Sfx 의 "step"/"step2" 를 번갈아 낸다.
    /// 벽에 막혀 제자리이거나, 공중·벽타기 중에는 내지 않는다. 튜토리얼 보스맵 플레이어 Variant 에만 붙어 있다.
    /// </summary>
    public class PlayerFootstepSfx : MonoBehaviour
    {
        [Tooltip("발소리 간격 (초)")]
        public float interval = 0.14f;
        [Tooltip("이 속도(유닛/초)보다 빨리 실제로 움직일 때만")]
        public float minSpeed = 1f;
        [Range(0f, 1f)] public float volume = 0.45f;

        public int StepCount { get; private set; }

        private ProceduralCharacterController ctrl;
        private Rigidbody2D rb;
        private float lastX;
        private float timer;
        private bool alt;
        private float movingFor;

        private void Awake()
        {
            ctrl = GetComponent<ProceduralCharacterController>();
            rb = GetComponent<Rigidbody2D>();
            lastX = transform.position.x;
        }

        private void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float x = transform.position.x;
            float speed = Mathf.Abs(x - lastX) / dt;
            lastX = x;
            bool grounded = ctrl != null && ctrl.IsGrounded && !ctrl.IsClimbing && (rb == null || Mathf.Abs(rb.linearVelocity.y) < 0.1f);
            // 물리(50Hz)와 화면 프레임이 달라 이동이 0 인 프레임이 끼므로 잠깐 멈춘 것은 이동으로 본다
            if (grounded && speed > minSpeed) movingFor = 0.06f;
            else movingFor -= dt;
            if (grounded && movingFor > 0f)
            {
                timer += dt;
                if (timer >= interval)
                {
                    timer = 0f;
                    alt = !alt;
                    StepCount++;
                    Sfx.Play(alt ? "step" : "step2", volume, Random.Range(0.92f, 1.1f));
                }
            }
            else timer = interval * 0.7f; // 다시 걷기 시작하면 금방 첫 발소리
        }
    }
}
