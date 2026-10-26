using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using UnityEditor;

using Newtonsoft.Json.Linq;

namespace Game.Editor
{
    public static class MonitorBrowserEngineInstaller
    {
        private const string PACKAGE_NAME = "dev.voltstro.unitywebbrowser.engine.cef.win.x64";
        private const string PACKAGE_VERSION = "2.2.8";
        private const string MENU_PATH = "Tools/Monitor/Install Browser Engine";
        private static bool _installing;

        /// <summary>
        /// 현재 프로젝트에 고정 버전의 웹 엔진을 설치하고 완료 또는 실패를 표시한다.
        /// 설치 중에는 중복 실행과 어셈블리 재로드를 막으며 원본 패키지 설정은 유지한다.
        /// </summary>
        [MenuItem(MENU_PATH)]
        private static async void Install()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string engineRoot = Path.Combine(projectRoot, "Packages", PACKAGE_NAME, "Engine~");
            if (IsInstalled(engineRoot))
            {
                EditorUtility.DisplayDialog("Monitor Browser", "웹 엔진이 이미 설치되어 있습니다.", "확인");
                return;
            }

            _installing = true;
            EditorApplication.LockReloadAssemblies();
            EditorUtility.DisplayProgressBar("Monitor Browser", "웹 엔진 다운로드 및 설치 중입니다. 잠시 기다려 주세요.", 0.5f);
            try
            {
                await Task.Run(() => DownloadAndInstall(projectRoot, engineRoot));
                Debug.Log("[Monitor Browser] 웹 엔진 설치 완료: " + engineRoot);
                EditorUtility.DisplayDialog("Monitor Browser", "웹 엔진 설치가 완료되었습니다.\n모니터의 웹 앱을 실행할 수 있습니다.", "확인");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Monitor Browser", "설치에 실패했습니다. 인터넷 연결과 Console 오류를 확인하세요.\n" + exception.Message, "확인");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorApplication.UnlockReloadAssemblies();
                _installing = false;
            }
        }

        /// <summary>
        /// 현재 에디터 상태를 검사하여 설치 메뉴의 사용 가능 여부를 반환한다.
        /// Windows 편집 모드에서만 허용하며 설치와 컴파일 중에는 실행을 차단한다.
        /// </summary>
        [MenuItem(MENU_PATH, true)]
        private static bool CanInstall()
        {
            return Application.platform == RuntimePlatform.WindowsEditor && !_installing
                && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>
        /// engineRoot의 실행 파일과 필수 리소스를 검사하여 설치 여부를 반환한다.
        /// 일부 파일만 남은 설치는 완료로 취급하지 않으며 디스크를 변경하지 않는다.
        /// </summary>
        private static bool IsInstalled(string engineRoot)
        {
            return File.Exists(Path.Combine(engineRoot, "UnityWebBrowser.Engine.Cef.exe"))
                && File.Exists(Path.Combine(engineRoot, "libcef.dll"))
                && File.Exists(Path.Combine(engineRoot, "icudtl.dat"))
                && Directory.Exists(Path.Combine(engineRoot, "locales"));
        }

        /// <summary>
        /// projectRoot의 임시 경로에 패키지를 다운로드하고 검증한 엔진을 engineRoot로 복원한다.
        /// 고정 버전의 체크섬을 사용하며 실패해도 검증 전 파일을 설치 위치에 복사하지 않는다.
        /// </summary>
        private static void DownloadAndInstall(string projectRoot, string engineRoot)
        {
            string staging = Path.Combine(projectRoot, "Temp", "MonitorBrowserInstall-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            string archive = Path.Combine(staging, "engine.tgz");
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
            {
                string metadata = client.GetStringAsync("https://upr.voltstro.dev/" + PACKAGE_NAME).GetAwaiter().GetResult();
                JToken distribution = JObject.Parse(metadata)["versions"]?[PACKAGE_VERSION]?["dist"];
                string expectedHash = (string)distribution?["shasum"];
                string tarball = (string)distribution?["tarball"];
                if (string.IsNullOrWhiteSpace(expectedHash) || !Uri.TryCreate(tarball, UriKind.Absolute, out Uri uri)
                    || uri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException("웹 엔진 패키지의 다운로드 정보를 확인할 수 없습니다.");
                using (Stream source = client.GetStreamAsync(uri).GetAwaiter().GetResult())
                using (FileStream target = File.Create(archive)) source.CopyTo(target);
                using (SHA1 sha1 = SHA1.Create())
                using (FileStream source = File.OpenRead(archive))
                {
                    string actualHash = BitConverter.ToString(sha1.ComputeHash(source)).Replace("-", "");
                    if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("웹 엔진 압축 파일의 체크섬이 일치하지 않습니다.");
                }
            }
            string extracted = Path.Combine(staging, "Engine~");
            ExtractEngine(archive, extracted);
            if (!IsInstalled(extracted)) throw new InvalidDataException("필수 웹 엔진 파일이 누락되었습니다.");
            foreach (string source in Directory.GetFiles(extracted, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(engineRoot, source.Substring(extracted.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target, true);
            }
            if (!IsInstalled(engineRoot)) throw new IOException("웹 엔진 설치 결과를 확인할 수 없습니다.");
        }

        /// <summary>
        /// 검증된 archive의 일반 TAR 파일에서 엔진과 라이선스 리소스만 destination에 추출한다.
        /// 링크와 경로 이탈 항목은 허용하지 않으며 압축 해제에 외부 셸을 사용하지 않는다.
        /// </summary>
        private static void ExtractEngine(string archive, string destination)
        {
            const string PREFIX = "package/Engine~/";
            string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            using (FileStream source = File.OpenRead(archive))
            using (var gzip = new GZipStream(source, CompressionMode.Decompress))
            {
                byte[] header = new byte[512];
                byte[] buffer = new byte[81920];
                while (true)
                {
                    ReadExactly(gzip, header, header.Length);
                    if (header[0] == 0) break;
                    string name = Encoding.UTF8.GetString(header, 0, 100).TrimEnd('\0');
                    string prefix = Encoding.UTF8.GetString(header, 345, 155).TrimEnd('\0');
                    if (prefix.Length > 0) name = prefix + "/" + name;
                    long size = Convert.ToInt64(Encoding.ASCII.GetString(header, 124, 12).Trim('\0', ' '), 8);
                    FileStream output = null;
                    try
                    {
                        if (name.StartsWith(PREFIX, StringComparison.Ordinal) && name.Length > PREFIX.Length)
                        {
                            string target = Path.GetFullPath(Path.Combine(destination, name.Substring(PREFIX.Length)));
                            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("압축 파일에 유효하지 않은 경로가 있습니다.");
                            if (header[156] == 0 || header[156] == (byte)'0')
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(target));
                                output = File.Create(target);
                            }
                            else if (header[156] != (byte)'5')
                                throw new InvalidDataException("지원하지 않는 엔진 압축 항목입니다.");
                        }
                        long remaining = size;
                        while (remaining > 0)
                        {
                            int count = (int)Math.Min(buffer.Length, remaining);
                            ReadExactly(gzip, buffer, count);
                            output?.Write(buffer, 0, count);
                            remaining -= count;
                        }
                    }
                    finally { output?.Dispose(); }
                    int padding = (int)((512 - size % 512) % 512);
                    ReadExactly(gzip, buffer, padding);
                }
            }
        }

        /// <summary>
        /// source에서 count 바이트를 buffer에 채우며 반환값 없이 스트림 위치를 이동한다.
        /// 요청한 길이 전에 끝나면 잘린 압축 파일 오류를 발생시킨다.
        /// </summary>
        private static void ReadExactly(Stream source, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = source.Read(buffer, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("웹 엔진 압축 파일이 완전하지 않습니다.");
                offset += read;
            }
        }
    }
}
