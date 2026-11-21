using System.Collections.Generic;
using Laurel;
using UnityEngine;

namespace pjs
{
    /// <summary>
    /// 화살(또는 레이저)로 피격하면 지정된 스폰 위치에 MinotourEnemy를 생성하는 표적 오브젝트.
    /// </summary>
    public class MinotourSpawnerTarget : MonoBehaviour, ILaserTarget
    {
        [Header("Spawn Settings")]
        [Tooltip("복제 원본으로 사용할 씬/프리팹 미노타우르")]
        public MinotourEnemy sourceMinotour;
        [Tooltip("sourceMinotour가 없을 때 스포너 위치 기준 오프셋")]
        public Vector2 fallbackSpawnOffset = new Vector2(-4f, 0f);
        [Tooltip("중복 피격 방지 쿨다운(초)")]
        public float spawnCooldown = 0.25f;

        [Header("Hitbox & Ground")]
        public Vector2 hitboxOffset = new Vector2(0f, 0.9f);
        public Vector2 hitboxSize = new Vector2(1.1f, 1.8f);
        public LayerMask groundLayer = 1 << 8; // Terrain

        private GameObject templateGo;
        private Vector3 spawnPosition;
        private Transform visual;
        private Vector3 visualBaseLocalPos;
        private SpriteRenderer[] renderers;
        private Color[] baseColors;
        private float nextSpawnTime;
        private float flashT = -1f;
        private float shakeT = -1f;
        private Vector2 shakeDir;

        private static readonly float[] Blink = { 0.045f, 0.035f, 0.035f };

        private void Awake()
        {
            gameObject.layer = Layers.Enemy;

            var box = GetComponent<BoxCollider2D>();
            if (box == null) box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.offset = hitboxOffset;
            box.size = hitboxSize;

            if (sourceMinotour != null)
            {
                spawnPosition = sourceMinotour.transform.position;
                templateGo = Instantiate(sourceMinotour.gameObject, transform);
                templateGo.name = "MinotourTemplate";
                templateGo.SetActive(false);
            }
            else
            {
                spawnPosition = transform.position + (Vector3)fallbackSpawnOffset;
            }

            renderers = GetComponentsInChildren<SpriteRenderer>(false);
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;

            var existingVisual = transform.Find("Visual");
            visual = existingVisual != null ? existingVisual : (renderers.Length > 0 ? renderers[0].transform : transform);
            visualBaseLocalPos = visual.localPosition;
        }

        private void Start()
        {
            SnapToGround();
        }

        private void SnapToGround()
        {
            var hit = Physics2D.Raycast((Vector2)transform.position + Vector2.up * 1.5f, Vector2.down, 12f, groundLayer);
            if (hit.collider != null && !hit.collider.isTrigger)
            {
                transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
            }
        }

        public void TakeLaserHit(int damage, bool critical, Vector2 point, Vector2 dir)
        {
            flashT = 0f;
            shakeT = 0f;
            shakeDir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.right;

            Fx.Burst(point, new Color(1f, 0.42f, 0.25f), 10, 5.5f, 0.14f, 0.35f);
            Sfx.Play("hit", 0.75f, 0.9f);

            if (Time.time < nextSpawnTime) return;
            nextSpawnTime = Time.time + spawnCooldown;

            if (templateGo != null)
            {
                var spawned = Instantiate(templateGo, spawnPosition, Quaternion.identity);
                spawned.name = "MinotourEnemy";
                spawned.SetActive(true);
            }

            Fx.WaveRing(spawnPosition + Vector3.up * 0.9f, 0.65f, 0.35f, new Color(1f, 0.45f, 0.3f, 1f));
            DamagePopup.SpawnText(transform.position + Vector3.up * 2.15f, "SPAWN!", new Color(1f, 0.55f, 0.3f, 1f));
        }

        private void Update()
        {
            if (flashT >= 0f && renderers != null)
            {
                flashT += Time.deltaTime;
                bool white = flashT < Blink[0] || (flashT >= Blink[0] + Blink[1] && flashT < Blink[0] + Blink[1] + Blink[2]);
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].color = white ? new Color(1f, 0.35f, 0.35f, 1f) : baseColors[i];

                if (flashT >= Blink[0] + Blink[1] + Blink[2])
                {
                    for (int i = 0; i < renderers.Length; i++)
                        if (renderers[i] != null) renderers[i].color = baseColors[i];
                    flashT = -1f;
                }
            }

            if (shakeT >= 0f && visual != null && visual != transform)
            {
                shakeT += Time.deltaTime;
                const float dur = 0.14f;
                if (shakeT >= dur)
                {
                    visual.localPosition = visualBaseLocalPos;
                    shakeT = -1f;
                }
                else
                {
                    float k = 1f - shakeT / dur;
                    visual.localPosition = visualBaseLocalPos + (Vector3)(shakeDir * (Mathf.Sin(shakeT * 55f) * 0.08f * k));
                }
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.25f, 0.9f);
            Vector3 center = transform.position + new Vector3(hitboxOffset.x, hitboxOffset.y, 0f);
            Gizmos.DrawWireCube(center, new Vector3(hitboxSize.x, hitboxSize.y, 0.1f));
            Gizmos.DrawSphere(transform.position + Vector3.up * 1.48f, 0.28f);
        }
    }
}
