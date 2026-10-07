using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Laurel.EditorTools
{
    /// <summary>
    /// 라우랄 빌드. 메뉴: Tools > Laurel > Build ...
    /// 명령줄(에디터를 닫은 상태):
    ///   Unity -batchmode -quit -projectPath &lt;프로젝트&gt; -executeMethod Laurel.EditorTools.LaurelBuilder.BuildMacFromCommandLine
    ///   Unity -batchmode -quit -projectPath &lt;프로젝트&gt; -buildTarget Win64 -executeMethod Laurel.EditorTools.LaurelBuilder.BuildWindowsFromCommandLine
    /// </summary>
    public static class LaurelBuilder
    {
        public const string Scene = "Assets/Laurel/Scenes/Laurel_Main.unity";
        public const string MacPath = "Builds/Laurel/macOS/Laurel.app";
        public const string WinDir = "Builds/Laurel/Windows";
        public const string WinExe = "Builds/Laurel/Windows/Laurel.exe";
        public const string WinZip = "Builds/Laurel/Laurel-Windows.zip";

        [MenuItem("Tools/Laurel/Build macOS")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, MacPath);

        [MenuItem("Tools/Laurel/Build Windows x64 + ZIP")]
        public static void BuildWindows()
        {
            if (Build(BuildTarget.StandaloneWindows64, WinExe)) Zip();
        }

        public static void BuildMacFromCommandLine() { if (!Build(BuildTarget.StandaloneOSX, MacPath)) EditorApplication.Exit(1); }
        public static void BuildWindowsFromCommandLine() { if (!Build(BuildTarget.StandaloneWindows64, WinExe)) EditorApplication.Exit(1); Zip(); }

        private static bool Build(BuildTarget target, string path)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
            {
                Debug.LogError($"[Laurel] {target} 빌드 모듈이 설치되어 있지 않습니다. Unity Hub > Installs > 6000.3.25f1 > Add modules 에서 설치하세요.");
                return false;
            }
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = path,
                target = target,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Laurel] {target} 빌드 결과: {report.summary.result}, 크기 {report.summary.totalSize / (1024 * 1024)}MB, 오류 {report.summary.totalErrors}, 경로 {path}");
            return report.summary.result == BuildResult.Succeeded;
        }

        private static void Zip()
        {
            string staging = "Builds/Laurel/_zip/Laurel-Windows";
            if (Directory.Exists("Builds/Laurel/_zip")) Directory.Delete("Builds/Laurel/_zip", true);
            CopyDir(WinDir, staging);
            File.WriteAllText(Path.Combine(staging, "README.txt"),
                "라우랄 (Laurel) — Windows x64\r\n\r\n압축을 푼 뒤 Laurel.exe 를 실행하세요.\r\n" +
                "조작: A/D 이동, Space 점프, Shift 대쉬(구매 후), 좌클릭 발사, R 회수 궁술, 우클릭/Q 버리기, Esc 일시정지\r\n" +
                "자동 검증: Laurel.exe -laurelSelfTest -laurelSelfTestOut report.txt\r\n", System.Text.Encoding.UTF8);
            if (File.Exists(WinZip)) File.Delete(WinZip);
            ZipFile.CreateFromDirectory("Builds/Laurel/_zip", WinZip);
            Directory.Delete("Builds/Laurel/_zip", true);
            Debug.Log("[Laurel] ZIP: " + WinZip);
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src))
            {
                if (d.EndsWith("_DoNotShip")) continue;
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
            }
        }
    }
}
