using System.Collections;
using UnityEngine;

namespace Procedural2D
{
    /// <summary>
    /// 화살 피격 시 충격 방향 흔들림(Wobble)과 피격 플래시를 연출하는 적 더미 컴포넌트입니다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
    public class EnemyDummy : MonoBehaviour
    {
        private SpriteRenderer spriteRenderer;
        private Color defaultColor = Color.white;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Coroutine hitRoutine;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null) defaultColor = spriteRenderer.color;
            initialPosition = transform.position;
            initialRotation = transform.rotation;
        }

        public void TakeHit(Vector2 impactVelocity)
        {
            if (hitRoutine != null) StopCoroutine(hitRoutine);
            hitRoutine = StartCoroutine(HitReaction(impactVelocity));
        }

        private IEnumerator HitReaction(Vector2 impactVelocity)
        {
            // 피격 플래시
            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(1.8f, 1.4f, 0.9f, 1f);
            }

            // 충격 방향으로 기울어진 후 진동 감쇠
            float sign = impactVelocity.x >= 0 ? -1f : 1f;
            float maxAngle = 12f * sign;
            float elapsed = 0f;
            float duration = 0.5f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float decay = Mathf.Exp(-6f * t);
                float angle = Mathf.Sin(t * Mathf.PI * 7f) * maxAngle * decay;
                transform.rotation = initialRotation * Quaternion.Euler(0f, 0f, angle);

                if (spriteRenderer != null && t > 0.1f)
                {
                    spriteRenderer.color = Color.Lerp(spriteRenderer.color, defaultColor, Time.deltaTime * 15f);
                }

                yield return null;
            }

            transform.rotation = initialRotation;
            transform.position = initialPosition;
            if (spriteRenderer != null) spriteRenderer.color = defaultColor;
            hitRoutine = null;
        }
    }
}
