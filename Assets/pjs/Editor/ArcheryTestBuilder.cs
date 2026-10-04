#if UNITY_EDITOR
using System;
using System.IO;
using Archery;
using Procedural2D;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Procedural2D.Editor
{
    /// <summary>
    /// 궁술 테스트 빌드 도구.
    /// - Tools/Archery/Create Test Scene : 설정 에셋 + 테스트 씬(Assets/pjs/Scenes/ArcheryTest.unity) 생성
    /// - Tools/Archery/Build macOS Test Build : 기존 ArcheryTest 씬으로 Builds/ArcheryTest/macOS/ArcheryTest.app 빌드 (씬이 없을 때만 생성)
    /// - 명령줄: -executeMethod Procedural2D.Editor.ArcheryTestBuilder.BuildMacFromCommandLine
    /// 프로젝트의 Build Settings 씬 목록은 건드리지 않고, 빌드 시 테스트 씬만 지정한다.
    /// </summary>
    public static class ArcheryTestBuilder
    {
        public const string ScenePath = "Assets/pjs/Scenes/ArcheryTest.unity";
        public const string ConfigPath = "Assets/pjs/Resources/ArcheryConfig.asset";
        public const string LineMaterialPath = "Assets/pjs/Archery/ArcheryLine.mat";
        public const string PlayerPrefabPath = "Assets/pjs/Prefabs/ProceduralCharacter.prefab";
        public const string DefaultBuildPath = "Builds/ArcheryTest/macOS/ArcheryTest.app";

        [MenuItem("Tools/Archery/Create Test Scene", false, 10)]
        public static void CreateTestSceneMenu()
        {
            CreateOrUpdateAssets();
            CreateTestScene();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Archery/Build macOS Test Build", false, 11)]
        public static void BuildMacMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var report = BuildMac(DefaultBuildPath);
            EditorUtility.DisplayDialog("Archery Test Build", $"{report.summary.result}\n{Path.GetFullPath(DefaultBuildPath)}", "OK");
        }

        public static void BuildMacFromCommandLine()
        {
            string output = DefaultBuildPath;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-archeryBuildPath");
            if (i >= 0 && i + 1 < args.Length) output = args[i + 1];

            BuildReport report = BuildMac(output);
            Debug.Log($"[ArcheryTestBuilder] result={report.summary.result} errors={report.summary.totalErrors} size={report.summary.totalSize} path={output}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static BuildReport BuildMac(string outputPath)
        {
            // 프로젝트의 ArcheryTest 씬을 그대로 사용 (없을 때만 생성) — 씬을 직접 수정해도 빌드가 덮어쓰지 않는다
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateOrUpdateAssets();
                CreateTestScene();
            }
            string full = Path.GetFullPath(outputPath);
            if (Directory.Exists(full)) Directory.Delete(full, true); // 이전 앱 파일이 섞이지 않게

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            return BuildPipeline.BuildPlayer(options);
        }

        // ───────────── Windows 64비트 배포 빌드 ─────────────

        public const string WindowsBuildDir = "Builds/ArcheryTest/Windows";
        public const string WindowsExeName = "ArcheryTest.exe";
        public const string WindowsZipPath = "Builds/ArcheryTest/ArcheryTest-Windows.zip";

        /// <summary>현재 에디터에 Windows Build Support 모듈이 설치되어 있는지</summary>
        public static bool IsWindowsBuildSupported() =>
            BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

        [MenuItem("Tools/Archery/Build Windows x64 Test Build + ZIP", false, 12)]
        public static void BuildWindowsMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string msg = BuildWindowsAndZip(out bool ok);
            EditorUtility.DisplayDialog("Archery Windows Build", msg, "OK");
        }

        public static void BuildWindowsFromCommandLine()
        {
            string msg = BuildWindowsAndZip(out bool ok);
            Debug.Log("[ArcheryTestBuilder] " + msg);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// ArcheryTest 씬 하나만 시작 씬으로 넣어 Windows x64 빌드를 만들고, 배포용 폴더 전체를 ZIP으로 묶는다.
        /// 프로젝트의 Build Settings 씬 목록과 macOS 빌드는 건드리지 않는다.
        /// </summary>
        public static string BuildWindowsAndZip(out bool ok)
        {
            ok = false;
            if (!IsWindowsBuildSupported())
            {
                return "Windows Build Support (Mono) 모듈이 이 Unity 에디터에 설치되어 있지 않습니다. " +
                       "Unity Hub > Installs > 6000.3.25f1 > Add modules > 'Windows Build Support (Mono)'를 설치한 뒤 다시 실행하세요.";
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateOrUpdateAssets();
                CreateTestScene();
            }

            string dir = Path.GetFullPath(WindowsBuildDir);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(WindowsBuildDir, WindowsExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                return $"Windows 빌드 실패: {report.summary.result}, 오류 {report.summary.totalErrors}건 (Console/로그 확인)";
            }

            string zip = PackageWindowsZip(dir);
            ok = true;
            return $"Windows 빌드 성공: {Path.Combine(dir, WindowsExeName)}\nZIP: {zip}";
        }

        /// <summary>
        /// 배포 폴더(ArcheryTest-Windows/)를 만들어 ZIP으로 묶는다.
        /// 디버그 심볼 폴더(*_BurstDebugInformation_DoNotShip, *_BackUpThisFolder_ButDontShipItWithYourGame)는 제외한다.
        /// </summary>
        private static string PackageWindowsZip(string buildDir)
        {
            string staging = Path.GetFullPath("Temp/ArcheryWindowsZip/ArcheryTest-Windows");
            if (Directory.Exists(Path.GetDirectoryName(staging))) Directory.Delete(Path.GetDirectoryName(staging), true);
            CopyDirectory(buildDir, staging);
            File.WriteAllText(Path.Combine(staging, "README.txt"),
                "궁술 테스트 빌드 (Windows 64비트)\r\n\r\n" +
                "1. ZIP 압축을 전부 풉니다. (ZIP 안에서 바로 실행하지 마세요)\r\n" +
                "2. 압축을 푼 ArcheryTest-Windows 폴더 안의 ArcheryTest.exe 를 실행합니다.\r\n" +
                "3. ArcheryTest.exe 와 함께 들어 있는 파일과 폴더(ArcheryTest_Data, UnityPlayer.dll 등)를 이동하거나 삭제하지 마세요.\r\n" +
                "   exe만 따로 옮기면 실행되지 않습니다. 폴더째로 옮기세요.\r\n" +
                "4. Windows SmartScreen 경고가 뜨면 '추가 정보' > '실행'을 누르세요. (서명되지 않은 테스트 빌드)\r\n\r\n" +
                "조작: A/D 이동 · Space 점프(2단) · Shift 대시 · 마우스 왼쪽 클릭 발사/충전/찌르기 · R 회수\r\n" +
                "      F5 전투 초기화 · Esc 로비 · F1 범위 표시 · F2 번개 시험 배치 · F3 적 체력 초기화\r\n" +
                "로비에서 발사 궁술 1종과 회수 궁술 1종을 고르고 '테스트 시작'을 누르세요.\r\n",
                new System.Text.UTF8Encoding(true));

            string zip = Path.GetFullPath(WindowsZipPath);
            if (File.Exists(zip)) File.Delete(zip);
            System.IO.Compression.ZipFile.CreateFromDirectory(staging, zip, System.IO.Compression.CompressionLevel.Optimal, true);
            Directory.Delete(Path.GetDirectoryName(staging), true);
            return zip;
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src)) File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            foreach (var sub in Directory.GetDirectories(src))
            {
                string name = Path.GetFileName(sub);
                if (name.EndsWith("_DoNotShip") || name.EndsWith("_ButDontShipItWithYourGame")) continue;
                CopyDirectory(sub, Path.Combine(dst, name));
            }
        }

        /// <summary>설정 에셋과 선 머티리얼이 없으면 만든다 (있으면 수치를 덮어쓰지 않음).</summary>
        public static void CreateOrUpdateAssets()
        {
            EnsureFolder("Assets/pjs/Resources");
            EnsureFolder("Assets/pjs/Archery");

            var cfg = AssetDatabase.LoadAssetAtPath<ArcheryConfig>(ConfigPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<ArcheryConfig>();
                cfg.EnsureDefaults();
                AssetDatabase.CreateAsset(cfg, ConfigPath);
            }
            else
            {
                cfg.EnsureDefaults();
                EditorUtility.SetDirty(cfg);
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(LineMaterialPath);
            if (mat == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                mat = new Material(sh) { name = "ArcheryLine" };
                AssetDatabase.CreateAsset(mat, LineMaterialPath);
            }
            AssetDatabase.SaveAssets();
        }

        public static void CreateTestScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 카메라 (기존 CameraFollow2D 사용)
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 7f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 1f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;
            camGo.transform.position = new Vector3(-16f, 3f, -10f);
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            var follow = camGo.AddComponent<CameraFollow2D>();

            // 2D 전역 조명 (URP 2D 렌더러)
            var lightGo = new GameObject("Global Light 2D");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;

            // 기존 이펙트 매니저
            new GameObject("Impact_Particle_Manager").AddComponent<ImpactParticleManager>();

            // 테스트 흐름
            var flowGo = new GameObject("ArcheryTestFlow");
            var flow = flowGo.AddComponent<ArcheryTestFlow>();
            flowGo.AddComponent<ArcheryTestUI>();
            flow.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            flow.arrowSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/pjs/Sprites/Character/Arrow.png");
            flow.dummySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/pjs/Sprites/Environment/Enemy_Dummy.png");
            flow.targetSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/pjs/Sprites/Environment/Target_Board.png");
            flow.eyeSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/pjs/Sprites/Environment/DemonEye_Body.png");
            flow.lineMaterial = AssetDatabase.LoadAssetAtPath<Material>(LineMaterialPath);
            flow.cameraFollow = follow;

            if (flow.playerPrefab == null) Debug.LogWarning("[ArcheryTestBuilder] 플레이어 프리팹을 찾지 못했습니다: " + PlayerPrefabPath);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[ArcheryTestBuilder] 테스트 씬 생성: " + ScenePath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
