#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Procedural2D;
using System.IO;
using System.Collections.Generic;

namespace Procedural2D.Editor
{
    /// <summary>
    /// 새로운 128x128 스프라이트 시트 기반 2D 픽셀 캐릭터 자동 조립 에디터 툴:
    /// 1. 시위 당기는 팔 2조각 분리 (상완 Arm_Right_Upper, 전완 Arm_Right_Forearm)
    /// 2. 활과 활 잡은 손 분리 (Arm_Bow_Hand, Bow)
    /// 3. 치마 위치를 머리 바로 아래에서 허리(Waist) 쪽으로 조정
    /// 4. 4조각 다리 추가 (좌/우 허벅지 Leg_Upper_L/R, 좌/우 종아리 Leg_Lower_L/R)
    /// </summary>
    public static class CharacterAutoAssembler
    {
        [MenuItem("Tools/Procedural 2D/Re-import All Character Sprites", false, 0)]
        public static void ReimportSprites()
        {
            ConfigureSprites();
        }

        [MenuItem("Tools/Procedural 2D/Assemble Character in Scene", false, 1)]
        public static void AssembleCharacter()
        {
            // 1. 스프라이트 피벗 및 픽셀 설정 강제 적용
            ConfigureSprites();

            // 2. 화살 프리팹 생성/확인
            GameObject arrowPrefab = CreateArrowPrefab();

            // 3. 기존 캐릭터 제거 후 신규 구조로 완전 조립
            GameObject existingPlayer = GameObject.Find("Player_Character");
            Vector3 spawnPos = new Vector3(0f, -0.32f, 0f);
            if (existingPlayer != null)
            {
                spawnPos = existingPlayer.transform.position;
                Object.DestroyImmediate(existingPlayer);
            }

            // 4. 루트 게임오브젝트 생성
            GameObject player = new GameObject("Player_Character");
            player.tag = "Player";
            player.transform.position = spawnPos;

            Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.freezeRotation = true;
            rb.gravityScale = 3f;

            CapsuleCollider2D col = player.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(0.48f, 0.96f);
            col.offset = new Vector2(0f, 0.02f);

            ProceduralCharacterController charCtrl = player.AddComponent<ProceduralCharacterController>();
            Procedural2DAim aimCtrl = player.AddComponent<Procedural2DAim>();
            ProceduralBodyShift bodyShift = player.AddComponent<ProceduralBodyShift>();
            bodyShift.walkBobAmount = 0f;
            bodyShift.headBobAmount = 0f;
            bodyShift.walkTiltAmount = 0f;
            bodyShift.runHeadDipAmount = 0.12f;
            bodyShift.headDipSpeed = 14f;
            ProceduralSkirtPhysics skirtPhysics = player.AddComponent<ProceduralSkirtPhysics>();
            ProceduralWaistRibbon waistRibbon = player.AddComponent<ProceduralWaistRibbon>();
            waistRibbon.anchorLocalOffset = new Vector3(-0.04f, -0.13f, 0f);

            // 5. Visual Root (좌우 플립 및 덤블링 제어용)
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform);
            visual.transform.localPosition = Vector3.zero;
            aimCtrl.visualRoot = visual.transform;
            charCtrl.visualTransform = visual.transform;

            // 6. 각 부위 스프라이트 로드 (AssetDatabase를 통해 강제 로드)
            string spritePath = "Assets/Sprites/Character/";
            Sprite sHead = LoadSprite(spritePath + "Head.png");
            Sprite sBody = LoadSprite(spritePath + "Body.png");
            Sprite sBow = LoadSprite(spritePath + "Bow.png");
            Sprite sBowHand = LoadSprite(spritePath + "Arm_Bow_Hand.png");
            Sprite sArmRUpper = LoadSprite(spritePath + "Arm_Right_Upper.png");
            Sprite sArmRForearm = LoadSprite(spritePath + "Arm_Right_Forearm.png");
            Sprite sSkirt1 = LoadSprite(spritePath + "Skirt_1.png");
            Sprite sSkirt2 = LoadSprite(spritePath + "Skirt_2.png");
            Sprite sSkirt3 = LoadSprite(spritePath + "Skirt_3.png");
            Sprite sSkirt4 = LoadSprite(spritePath + "Skirt_4.png");
            Sprite sSkirt5 = LoadSprite(spritePath + "Skirt_5.png");
            Sprite sLegUpL = LoadSprite(spritePath + "Leg_Upper_L.png");
            Sprite sLegLowL = LoadSprite(spritePath + "Leg_Lower_L.png");
            Sprite sLegUpR = LoadSprite(spritePath + "Leg_Upper_R.png");
            Sprite sLegLowR = LoadSprite(spritePath + "Leg_Lower_R.png");
            Sprite sQuiver = LoadSprite(spritePath + "Quiver.png");

            Material defaultMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/Materials/Sprite-Lit-Default.mat");

            // --- A. 상체/몸통 (Sorting 1) ---
            GameObject body = new GameObject("Body");
            body.transform.SetParent(visual.transform, false);
            body.transform.localPosition = new Vector3(0f, 0f, 0f);
            SpriteRenderer srBody = body.AddComponent<SpriteRenderer>();
            srBody.sprite = sBody;
            srBody.sortingOrder = 1;
            if (defaultMat != null) srBody.sharedMaterial = defaultMat;
            charCtrl.bodyTransform = body.transform;
            bodyShift.bodyTransform = body.transform;

            // --- C-2. 허리춤 화살통 (Quiver at Waist) ---
            GameObject quiver = new GameObject("Quiver");
            quiver.transform.SetParent(body.transform, false);
            quiver.transform.localPosition = new Vector3(-0.08f, -0.10f, 0f);
            quiver.transform.localRotation = Quaternion.Euler(0f, 0f, -15f);
            SpriteRenderer srQuiver = quiver.AddComponent<SpriteRenderer>();
            srQuiver.sprite = sQuiver;
            srQuiver.sortingOrder = 2;
            if (defaultMat != null) srQuiver.sharedMaterial = defaultMat;


            // --- B. 머리 (몸통 목 바로 위에 완벽 결합, Sorting 4) ---
            GameObject head = new GameObject("Head");
            head.transform.SetParent(body.transform, false);
            head.transform.localPosition = new Vector3(0.015f, 0.14f, 0f);
            SpriteRenderer srHead = head.AddComponent<SpriteRenderer>();
            srHead.sprite = sHead;
            srHead.sortingOrder = 4;
            if (defaultMat != null) srHead.sharedMaterial = defaultMat;
            charCtrl.headTransform = head.transform;
            bodyShift.headTransform = head.transform;

            // --- C. 구조 변경 1: 시위 당기는 팔 2조각 분리 (상완 + 전완) ---
            GameObject armRPivot = new GameObject("Arm_Right_Pivot");
            armRPivot.transform.SetParent(body.transform, false);
            armRPivot.transform.localPosition = new Vector3(-0.12f, 0.08f, 0f);
            aimCtrl.rightArmPivot = armRPivot.transform;

            GameObject armRUpper = new GameObject("Arm_Right_Upper");
            armRUpper.transform.SetParent(armRPivot.transform, false);
            armRUpper.transform.localPosition = Vector3.zero;
            SpriteRenderer srUpper = armRUpper.AddComponent<SpriteRenderer>();
            srUpper.sprite = sArmRUpper;
            srUpper.sortingOrder = -1;
            if (defaultMat != null) srUpper.sharedMaterial = defaultMat;
            aimCtrl.rightUpperArm = armRUpper.transform;

            GameObject armRForearm = new GameObject("Arm_Right_Forearm");
            armRForearm.transform.SetParent(armRUpper.transform, false);
            armRForearm.transform.localPosition = new Vector3(-0.16f, 0f, 0f);
            SpriteRenderer srForearm = armRForearm.AddComponent<SpriteRenderer>();
            srForearm.sprite = sArmRForearm;
            srForearm.sortingOrder = -1;
            if (defaultMat != null) srForearm.sharedMaterial = defaultMat;
            aimCtrl.rightForearm = armRForearm.transform;

            // --- D. 치마 제거 (사용자 요청: 다리 가림 방지 및 시인성 극대화) ---
            skirtPhysics.joint1 = null;
            skirtPhysics.joint2 = null;
            skirtPhysics.joint3 = null;
            skirtPhysics.joint4 = null;
            skirtPhysics.joint5 = null;
            skirtPhysics.enabled = false;

            // --- E. 구조 변경 2: 활과 활을 잡은 손 분리 (Arm_Bow_Hand + Bow) ---
            GameObject armLPivot = new GameObject("Arm_Left_Pivot");
            armLPivot.transform.SetParent(visual.transform, false);
            armLPivot.transform.localPosition = new Vector3(0.12f, 0.08f, 0f);
            aimCtrl.leftArmPivot = armLPivot.transform;

            GameObject bowHand = new GameObject("Arm_Bow_Hand");
            bowHand.transform.SetParent(armLPivot.transform, false);
            bowHand.transform.localPosition = new Vector3(0.04f, 0f, 0f);
            bowHand.transform.localRotation = Quaternion.Euler(0f, 0f, 38f); // 팔을 수평 방향으로 뻗음
            SpriteRenderer srBowHand = bowHand.AddComponent<SpriteRenderer>();
            srBowHand.sprite = sBowHand;
            srBowHand.sortingOrder = 5;
            if (defaultMat != null) srBowHand.sharedMaterial = defaultMat;
            aimCtrl.bowHandTransform = bowHand.transform;
            aimCtrl.bowHandRenderer = srBowHand;

            GameObject bow = new GameObject("Bow");
            bow.transform.SetParent(armLPivot.transform, false);
            bow.transform.localPosition = new Vector3(0.22f, 0f, 0f);
            bow.transform.localRotation = Quaternion.Euler(0f, 0f, -82f); // 활을 팔과 거의 평행(수평 크로스보우 그립)하게 눕힘
            SpriteRenderer srBow = bow.AddComponent<SpriteRenderer>();
            srBow.sprite = sBow;
            srBow.sortingOrder = 6;
            if (defaultMat != null) srBow.sharedMaterial = defaultMat;
            aimCtrl.bowTransform = bow.transform;
            aimCtrl.bowRenderer = srBow;
            aimCtrl.leftArmBowRenderer = srBow; // 호환용

            // 화살 발사점 (수평 활의 전방 발사선)
            GameObject muzzle = new GameObject("Muzzle_Point");
            muzzle.transform.SetParent(armLPivot.transform, false);
            muzzle.transform.localPosition = new Vector3(0.36f, 0f, 0f);
            aimCtrl.muzzlePoint = muzzle.transform;
            aimCtrl.arrowPrefab = arrowPrefab;
            aimCtrl.maxAmmo = 12;
            aimCtrl.currentAmmo = 12;

            // --- F. 구조 변경 4: 4조각 다리 추가 (허벅지 + 종아리 각 2조각) ---
            GameObject legsRoot = new GameObject("Legs_Root");
            legsRoot.transform.SetParent(visual.transform, false);
            legsRoot.transform.localPosition = new Vector3(0f, -0.14f, 0f);
            charCtrl.legsTransform = legsRoot.transform;

            // 앞쪽 다리 (Left)
            GameObject legUpL = new GameObject("Leg_Upper_L");
            legUpL.transform.SetParent(legsRoot.transform, false);
            legUpL.transform.localPosition = new Vector3(-0.06f, 0f, 0f);
            SpriteRenderer srLegUpL = legUpL.AddComponent<SpriteRenderer>();
            srLegUpL.sprite = sLegUpL;
            srLegUpL.sortingOrder = 0;
            if (defaultMat != null) srLegUpL.sharedMaterial = defaultMat;
            charCtrl.legUpperL = legUpL.transform;

            GameObject legLowL = new GameObject("Leg_Lower_L");
            legLowL.transform.SetParent(legUpL.transform, false);
            legLowL.transform.localPosition = new Vector3(0f, -0.16f, 0f);
            SpriteRenderer srLegLowL = legLowL.AddComponent<SpriteRenderer>();
            srLegLowL.sprite = sLegLowL;
            srLegLowL.sortingOrder = 0;
            if (defaultMat != null) srLegLowL.sharedMaterial = defaultMat;
            charCtrl.legLowerL = legLowL.transform;

            // 뒤쪽 다리 (Right)
            GameObject legUpR = new GameObject("Leg_Upper_R");
            legUpR.transform.SetParent(legsRoot.transform, false);
            legUpR.transform.localPosition = new Vector3(0.06f, 0f, 0f);
            SpriteRenderer srLegUpR = legUpR.AddComponent<SpriteRenderer>();
            srLegUpR.sprite = sLegUpR;
            srLegUpR.sortingOrder = -2;
            if (defaultMat != null) srLegUpR.sharedMaterial = defaultMat;
            charCtrl.legUpperR = legUpR.transform;

            GameObject legLowR = new GameObject("Leg_Lower_R");
            legLowR.transform.SetParent(legUpR.transform, false);
            legLowR.transform.localPosition = new Vector3(0f, -0.16f, 0f);
            SpriteRenderer srLegLowR = legLowR.AddComponent<SpriteRenderer>();
            srLegLowR.sprite = sLegLowR;
            srLegLowR.sortingOrder = -2;
            if (defaultMat != null) srLegLowR.sharedMaterial = defaultMat;
            charCtrl.legLowerR = legLowR.transform;

            // 7. 대형 맵 환경 생성
            EnsureEnvironment();

            // 8. 오디오 매니저 설정
            EnsureAudioManager();

            // 8.5 고성능 충돌 파티클 풀 매니저 설정
            EnsureImpactParticleManager();

            // 8.6 테라리아 눈깔 괴물(Demon Eye) 생성
            EnsureDemonEye(player);

            // 9. 픽셀 퍼펙트 카메라 및 플레이어 추적 설정
            SetupPixelPerfectRendering(player);

            // 10. 프리팹으로 저장 및 씬 저장
            EnsurePrefab(player);

            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

            Selection.activeGameObject = player;
            Undo.RegisterCreatedObjectUndo(player, "Assemble Procedural Character");

            Debug.Log("<color=#00FF88><b>[Procedural2D]</b> 4대 구조 변경 (2단 시위팔, 활/손 분리, 허리 치마, 4단 다리) 반영 캐릭터 조립 완료!</color>");
        }

        public static Sprite LoadSprite(string path)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s != null) return s;

            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (allAssets != null)
            {
                foreach (Object obj in allAssets)
                {
                    if (obj is Sprite spr)
                    {
                        return spr;
                    }
                }
            }

            Debug.LogError($"<color=red><b>[Procedural2D]</b> 스프라이트를 찾을 수 없습니다: {path}</color>");
            return null;
        }

        [MenuItem("Tools/Procedural 2D/Setup Pixel Perfect Rendering", false, 3)]
        public static void SetupPixelPerfectMenu()
        {
            GameObject player = GameObject.Find("Player_Character");
            SetupPixelPerfectRendering(player);
        }

        public static void SetupPixelPerfectRendering(GameObject player)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            cam.orthographic = true;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.15f);

            PixelPerfectCamera ppc = cam.GetComponent<PixelPerfectCamera>();
            if (ppc == null)
            {
                ppc = cam.gameObject.AddComponent<PixelPerfectCamera>();
            }

            ppc.assetsPPU = 32;
            ppc.refResolutionX = 480;
            ppc.refResolutionY = 270;
            ppc.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            ppc.cropFrame = PixelPerfectCamera.CropFrame.None;

            CameraFollow2D follow = cam.GetComponent<CameraFollow2D>();
            if (follow == null)
            {
                follow = cam.gameObject.AddComponent<CameraFollow2D>();
            }
            if (player != null)
            {
                follow.target = player.transform;
            }

            Debug.Log("<color=#00D8FF><b>[Procedural2D]</b> URP Pixel Perfect Camera & 추적 컨트롤러 설정 완료</color>");
        }

        [MenuItem("Tools/Procedural 2D/Rebuild Environment Only", false, 2)]
        public static void RebuildEnvironmentOnly()
        {
            ConfigureSprites();
            EnsureEnvironment();
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);
            bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[Scene Save] SaveOpenScenes={saved}, ActiveScene={activeScene.name}, Path={activeScene.path}");
            Debug.Log("<color=#00FF88><b>[Procedural2D]</b> 새로운 고해상도 그라운드 타일 및 플랫폼 블록 리빌드 및 씬 저장 완료!</color>");
        }

        [MenuItem("Tools/Procedural 2D/Configure Sprite Settings", false, 3)]
        public static void ConfigureSprites()
        {
            bool anyChanged = false;
            string spritePath = "Assets/Sprites/Character/";
            string[] sprites = new string[]
            {
                "Head.png", "Body.png",
                "Bow.png", "Arm_Bow_Hand.png",
                "Arm_Right_Upper.png", "Arm_Right_Forearm.png",
                "Skirt_1.png", "Skirt_2.png", "Skirt_3.png", "Skirt_4.png", "Skirt_5.png",
                "Leg_Upper_L.png", "Leg_Upper_R.png", "Leg_Lower_L.png", "Leg_Lower_R.png",
                "Quiver.png",
                "Arrow.png"
            };

            string envPath = "Assets/Sprites/Environment/";
            string[] envSprites = new string[] { "Ground_Tile.png", "Platform_Block.png" };
            foreach (string s in envSprites)
            {
                string path = envPath + s;
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null)
                {
                    bool changed = false;
                    if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
                    if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
                    if (ti.spritePixelsPerUnit != 16) { ti.spritePixelsPerUnit = 16; changed = true; }
                    if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
                    if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; changed = true; }
                    if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }

                    if (changed)
                    {
                        ti.SaveAndReimport();
                        anyChanged = true;
                    }
                }
            }

            foreach (string s in sprites)
            {
                string path = spritePath + s;
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null)
                {
                    bool changed = false;
                    if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
                    if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
                    if (ti.spritePixelsPerUnit != 32) { ti.spritePixelsPerUnit = 32; changed = true; }
                    if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
                    if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }

                    TextureImporterSettings settings = new TextureImporterSettings();
                    ti.ReadTextureSettings(settings);

                    Vector2 targetPivot = new Vector2(0.5f, 0.5f);
                    if (s == "Head.png") targetPivot = new Vector2(0.5f, 0.05f);
                    else if (s == "Body.png") targetPivot = new Vector2(0.5f, 0.5f);
                    else if (s == "Bow.png") targetPivot = new Vector2(0.5f, 0.5f);
                    else if (s == "Arm_Bow_Hand.png") targetPivot = new Vector2(0.2f, 0.5f);
                    else if (s == "Arm_Right_Upper.png") targetPivot = new Vector2(0.85f, 0.5f);
                    else if (s == "Arm_Right_Forearm.png") targetPivot = new Vector2(0.5f, 0.9f);
                    else if (s.StartsWith("Skirt")) targetPivot = new Vector2(0.5f, 1.0f);
                    else if (s.StartsWith("Leg_Upper")) targetPivot = new Vector2(0.5f, 0.9f);
                    else if (s.StartsWith("Leg_Lower")) targetPivot = new Vector2(0.5f, 0.9f);
                    else if (s == "Arrow.png") targetPivot = new Vector2(0.1f, 0.5f);
                    else if (s == "Quiver.png") targetPivot = new Vector2(0.5f, 0.75f);

                    if (settings.spriteAlignment != (int)SpriteAlignment.Custom || settings.spritePivot != targetPivot)
                    {
                        settings.spriteAlignment = (int)SpriteAlignment.Custom;
                        settings.spritePivot = targetPivot;
                        ti.SetTextureSettings(settings);
                        changed = true;
                    }

                    if (changed)
                    {
                        ti.SaveAndReimport();
                        anyChanged = true;
                    }
                }
            }
            if (anyChanged)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
        }

        private static GameObject CreateArrowPrefab()
        {
            string prefabPath = "Assets/Prefabs/ArrowProjectile.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            Sprite sArrow = LoadSprite("Assets/Sprites/Character/Arrow.png");
            if (sArrow == null)
            {
                sArrow = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            }

            GameObject arrowGo = new GameObject("ArrowProjectile");
            SpriteRenderer sr = arrowGo.AddComponent<SpriteRenderer>();
            sr.sprite = sArrow;
            sr.sortingOrder = 7;

            Material litMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/Materials/Sprite-Lit-Default.mat");
            if (litMat != null) sr.sharedMaterial = litMat;

            Rigidbody2D rb = arrowGo.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            BoxCollider2D col = arrowGo.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.65f, 0.18f);
            col.isTrigger = false;

            arrowGo.AddComponent<ArrowProjectile>();

            string dir = "Assets/Prefabs";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(arrowGo, prefabPath);
            Object.DestroyImmediate(arrowGo);
            return prefab;
        }

        private static void EnsureDemonEye(GameObject player)
        {
            GameObject eyeGo = GameObject.Find("Demon_Eye");
            if (eyeGo == null) eyeGo = new GameObject("Demon_Eye");

            // 기존 촉수 자식 오브젝트 정리
            for (int i = eyeGo.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(eyeGo.transform.GetChild(i).gameObject);
            }

            eyeGo.transform.position = player.transform.position + new Vector3(6.5f, 4.2f, 0f);
            eyeGo.transform.localScale = Vector3.one;

            SpriteRenderer sr = eyeGo.GetComponent<SpriteRenderer>();
            if (sr == null) sr = eyeGo.AddComponent<SpriteRenderer>();
            Sprite eyeSprite = LoadSprite("Assets/Sprites/Environment/DemonEye_Body.png");
            if (eyeSprite != null) sr.sprite = eyeSprite;
            sr.color = Color.white;
            sr.sortingOrder = 5;

            CircleCollider2D col = eyeGo.GetComponent<CircleCollider2D>();
            if (col == null) col = eyeGo.AddComponent<CircleCollider2D>();
            col.radius = 0.95f;
            col.isTrigger = false;

            DemonEyeAI ai = eyeGo.GetComponent<DemonEyeAI>();
            if (ai == null) ai = eyeGo.AddComponent<DemonEyeAI>();
            ai.targetPlayer = player.transform;
            ai.baseSpeed = 4.5f;
            ai.dashSpeed = 15f;
            ai.orbitRadius = 6.5f;
            ai.maxHp = 6;
            ai.currentHp = 6;

            ProceduralTentaclePhysics tentacles = eyeGo.GetComponent<ProceduralTentaclePhysics>();
            if (tentacles == null) tentacles = eyeGo.AddComponent<ProceduralTentaclePhysics>();

            try
            {
                eyeGo.tag = "Enemy";
            }
            catch {}
        }

        private static void EnsureImpactParticleManager()
        {
            GameObject pmGo = GameObject.Find("Impact_Particle_Manager");
            if (pmGo == null) pmGo = new GameObject("Impact_Particle_Manager");
            if (pmGo.GetComponent<ImpactParticleManager>() == null) pmGo.AddComponent<ImpactParticleManager>();
        }

        private static void EnsureAudioManager()
        {
            GameObject audioMgrGo = GameObject.Find("Audio_Manager");
            if (audioMgrGo == null) audioMgrGo = new GameObject("Audio_Manager");

            AudioManager audioMgr = audioMgrGo.GetComponent<AudioManager>();
            if (audioMgr == null) audioMgr = audioMgrGo.AddComponent<AudioManager>();

            AudioClip bgm = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/BGM/Nujabes_Aruarian_Dance.mp3");
            AudioClip shoot = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Arrow_Shoot.wav");
            AudioClip wall = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Arrow_Wall_Hit.wav");
            AudioClip enemy = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Arrow_Enemy_Hit.wav");

            if (bgm != null) audioMgr.bgmClip = bgm;
            if (shoot != null) audioMgr.arrowShootClip = shoot;
            if (wall != null) audioMgr.arrowWallHitClip = wall;
            if (enemy != null) audioMgr.arrowEnemyHitClip = enemy;

            audioMgr.bgmVolume = 0.42f;
            audioMgr.shootVolume = 0.75f;
            audioMgr.hitWallVolume = 0.85f;
            audioMgr.hitEnemyVolume = 0.95f;
        }

        private static void EnsureEnvironment()
        {
            Sprite groundSprite = LoadSprite("Assets/Sprites/Environment/Ground_Tile.png");
            Sprite blockSprite = LoadSprite("Assets/Sprites/Environment/Platform_Block.png");
            Sprite dummySprite = LoadSprite("Assets/Sprites/Environment/Enemy_Dummy.png");
            Sprite squareFallback = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");

            Sprite gSprite = groundSprite != null ? groundSprite : squareFallback;
            Sprite bSprite = blockSprite != null ? blockSprite : squareFallback;

            GameObject ground = GameObject.Find("Ground");
            if (ground == null) ground = new GameObject("Ground");
            ground.transform.position = new Vector3(0f, -3.05f, 0f);
            ground.transform.localScale = Vector3.one;

            BoxCollider2D gCol = ground.GetComponent<BoxCollider2D>();
            if (gCol == null) gCol = ground.AddComponent<BoxCollider2D>();
            gCol.size = new Vector2(50f, 4.5f);
            gCol.offset = Vector2.zero;

            SpriteRenderer gSr = ground.GetComponent<SpriteRenderer>();
            if (gSr == null) gSr = ground.AddComponent<SpriteRenderer>();
            gSr.sprite = gSprite;
            gSr.color = Color.white;
            gSr.drawMode = SpriteDrawMode.Tiled;
            gSr.tileMode = SpriteTileMode.Continuous;
            gSr.size = new Vector2(50f, 4.5f);
            gSr.sortingOrder = -5;

            CreateOrUpdateTiledBlock("Wall_Left", new Vector3(-25f, 5f, 0f), new Vector2(1.5f, 16f), new Color(0.7f, 0.75f, 0.82f, 1f), -4, bSprite);
            CreateOrUpdateTiledBlock("Wall_Right", new Vector3(25f, 5f, 0f), new Vector2(1.5f, 16f), new Color(0.7f, 0.75f, 0.82f, 1f), -4, bSprite);

            CreateOrUpdateTiledBlock("Block_Square_1", new Vector3(-6.5f, 0.6f, 0f), new Vector2(3.2f, 3.2f), Color.white, -2, bSprite);
            CreateOrUpdateTiledBlock("Block_Square_2", new Vector3(-13.5f, 2.2f, 0f), new Vector2(3.6f, 3.6f), Color.white, -2, bSprite);
            CreateOrUpdateTiledBlock("Block_Square_3", new Vector3(0f, 2.8f, 0f), new Vector2(3.0f, 3.0f), Color.white, -2, bSprite);
            CreateOrUpdateTiledBlock("Block_Square_4", new Vector3(7.0f, 0.8f, 0f), new Vector2(3.4f, 3.4f), Color.white, -2, bSprite);
            CreateOrUpdateTiledBlock("Block_Square_5", new Vector3(15.0f, 2.6f, 0f), new Vector2(4.0f, 4.0f), Color.white, -2, bSprite);
            CreateOrUpdateTiledBlock("Block_Square_6", new Vector3(-19.5f, 4.5f, 0f), new Vector2(3.2f, 3.2f), Color.white, -2, bSprite);

            Sprite dSprite = dummySprite != null ? dummySprite : squareFallback;

            GameObject oldDummy = GameObject.Find("Target_Dummy");
            if (oldDummy != null) Object.DestroyImmediate(oldDummy);

            CreateOrUpdateDummy("Target_Dummy_Ground", new Vector3(-6.0f, 1.75f, 0f), dSprite);
            CreateOrUpdateDummy("Target_Dummy_1", new Vector3(7.0f, 4.25f, 0f), dSprite);
            CreateOrUpdateDummy("Target_Dummy_2", new Vector3(15.0f, 6.35f, 0f), dSprite);
            CreateOrUpdateDummy("Target_Dummy_3", new Vector3(-13.5f, 5.75f, 0f), dSprite);
        }

        private static GameObject CreateOrUpdateTiledBlock(string name, Vector3 pos, Vector2 size, Color col, int sortOrder, Sprite sprite)
        {
            GameObject go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);

            go.transform.position = pos;
            go.transform.localScale = Vector3.one;

            BoxCollider2D bc = go.GetComponent<BoxCollider2D>();
            if (bc == null) bc = go.AddComponent<BoxCollider2D>();
            bc.size = size;
            bc.offset = Vector2.zero;

            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            if (sprite != null)
            {
                sr.sprite = sprite;
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.tileMode = SpriteTileMode.Continuous;
                sr.size = size;
            }
            sr.color = col;
            sr.sortingOrder = sortOrder;

            return go;
        }

        private static GameObject CreateOrUpdateDummy(string name, Vector3 pos, Sprite sprite)
        {
            GameObject go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);

            go.transform.position = pos;
            go.transform.localScale = Vector3.one;

            BoxCollider2D bc = go.GetComponent<BoxCollider2D>(); if (bc == null) bc = go.AddComponent<BoxCollider2D>();
            bc.size = new Vector2(1.8f, 3.4f);
            bc.offset = Vector2.zero;

            SpriteRenderer sr = go.GetComponent<SpriteRenderer>(); if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            if (sprite != null)
            {
                sr.sprite = sprite;
                sr.drawMode = SpriteDrawMode.Simple;
            }
            sr.color = Color.white;
            sr.sortingOrder = 0;

            EnemyDummy dummy = go.GetComponent<EnemyDummy>();
            if (dummy == null) dummy = go.AddComponent<EnemyDummy>();

            return go;
        }

        private static void EnsurePrefab(GameObject player)
        {
            string dir = "Assets/Prefabs";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(player, "Assets/Prefabs/ProceduralCharacter.prefab", InteractionMode.AutomatedAction);
        }
    }
}
#endif


