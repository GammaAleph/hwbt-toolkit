// Integration of the "Beat That! Revival" community patches V1 and V2 by Kyubikkumasuta
// (https://hot-wheels-beat-that-revival.com/). The patches are NOT part of this program:
// they are downloaded from the author's official links and installed with the author's own scripts.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;

namespace Hwbt
{
    public class PatchInfo
    {
        public string Name;          // "Patch V1"
        public string Folder;        // folder under community\
        public string PageUrl;       // official download page
        public string DownloadUrl;   // official file link
        public string ZipSha256;     // verified version of the ZIP
        public string InstallBat, RestoreBat;
        public bool AlwaysElevate;   // V1 helpers request administrator rights themselves
    }

    public static class CommunityPatch
    {
        public const string Author = "Kyubikkumasuta";
        public const string SiteUrl = "https://hot-wheels-beat-that-revival.com/";

        public static readonly PatchInfo V1 = new PatchInfo
        {
            Name = "Patch V1",
            Folder = "HWBT_Patch_V1",
            PageUrl = "https://hot-wheels-beat-that-revival.com/patch-download.html",
            DownloadUrl = "https://drive.usercontent.google.com/download?id=1Y4Eg9fypgLyKmenW_C7Mg5pytvOqCLFl&export=download&confirm=t",
            ZipSha256 = "18713508c9735f98b55259feb1a3e9618744671448e34dcfb42e183d91be1051",
            InstallBat = "Install_HWBT_Patch_V1.bat",
            RestoreBat = "Restore_HWBT_Patch_V1.bat",
            AlwaysElevate = true,
        };

        public static readonly PatchInfo V2 = new PatchInfo
        {
            Name = "Patch V2",
            Folder = "HWBT_Patch_V2",
            PageUrl = "https://hot-wheels-beat-that-revival.com/Patch-V2.html",
            DownloadUrl = "https://drive.usercontent.google.com/download?id=1AcTt5ISmcmSljr6XYETuHkyYBvEbqzbc&export=download&confirm=t",
            ZipSha256 = "4ec1f0f1c9903878e7cf803286beb9016ce2673615dcdb00a7b8c2603043edbe",
            InstallBat = "Install_HWBT_Patch_V2.bat",
            RestoreBat = "Restore_HWBT_Patch_V2.bat",
            AlwaysElevate = false,
        };

        const string V2ControllerDllSha256 = "60f5b2f83658d4b8b517f7789c144246caa69d998f160a320d05bf395037c294";
        // HOT.exe hashes produced by Patch V1 (English, English+widescreen, German, German+widescreen).
        static readonly string[] V1ExeSha256 =
        {
            "51bd2cc9bf9695c38c90bed5f8c0118a32d8b185854ba5917dc8f61309d80af7",
            "2f178f70d9b83219aba5d4281e2a0854851d87d64e65e672f203832213636fd5",
            "698a648ccb3846ed9d85f1f74c13e99e2ff7f1ae4a486bac860f60fdc30d021a",
            "5cb7993d76434bf3428e5505567c5d514d9edced31b23222cc3f265eb0181230",
        };

        public static string CommunityDir(string toolDir) { return Path.Combine(toolDir, "community"); }

        // Folder that contains the patch's install script, or null.
        public static string FindPatchDir(string toolDir, PatchInfo p)
        {
            string dir = CommunityDir(toolDir);
            if (!Directory.Exists(dir)) return null;
            var bat = Directory.GetFiles(dir, p.InstallBat, SearchOption.AllDirectories).FirstOrDefault();
            return bat == null ? null : Path.GetDirectoryName(bat);
        }

        public enum State { NotInstalled, V1, V2 }

        public static State GetState(string binDir)
        {
            string dll = Path.Combine(binDir, "dinput8.dll");
            if (File.Exists(Path.Combine(binDir, "HOT.exe.before_patch_V2")) && File.Exists(dll) && Sha256(dll) == V2ControllerDllSha256)
                return State.V2;
            string exe = Path.Combine(binDir, "HOT.exe");
            if (File.Exists(exe) && V1ExeSha256.Contains(Sha256(exe))) return State.V1;
            return State.NotInstalled;
        }

        // Downloads the official ZIP into community\. confirmMismatch(actualHash) decides whether to keep
        // a file whose hash differs from the verified one (e.g. the author published an update).
        public static string Download(string toolDir, PatchInfo p, Func<string, bool> confirmMismatch)
        {
            string dir = CommunityDir(toolDir);
            Directory.CreateDirectory(dir);
            string zip = Path.Combine(dir, p.Folder + ".zip");
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "HWBT-Toolkit/" + Program.Version;
                wc.DownloadFile(p.DownloadUrl, zip);
            }
            string hash = Sha256(zip);
            if (hash != p.ZipSha256 && !confirmMismatch(hash))
            {
                File.Delete(zip);
                throw new InvalidDataException("Download cancelled: SHA-256 mismatch (" + hash + ")");
            }
            string target = Path.Combine(dir, p.Folder);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            ZipFile.ExtractToDirectory(zip, target);
            string patchDir = FindPatchDir(toolDir, p);
            if (patchDir == null) throw new InvalidDataException("The ZIP does not contain " + p.InstallBat);
            return patchDir;
        }

        // Copies the patch (without its source\ folder) next to HOT.exe, as the author's README requires.
        public static void CopyToBin(string patchDir, string binDir)
        {
            foreach (var f in Directory.GetFiles(patchDir, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(patchDir.TrimEnd('\\').Length + 1);
                if (rel.StartsWith("source\\", StringComparison.OrdinalIgnoreCase)) continue;
                string dst = Path.Combine(binDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true);
            }
        }

        // Runs the author's installer / restore script in a visible console and waits for it.
        public static int RunScript(string binDir, string batName, bool elevate)
        {
            string bat = Path.Combine(binDir, batName);
            if (!File.Exists(bat)) throw new FileNotFoundException(batName + " not found in " + binDir);
            // Full path: an elevated cmd.exe ignores WorkingDirectory. The scripts cd to their own folder.
            var psi = new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { WorkingDirectory = binDir, UseShellExecute = true };
            if (elevate) psi.Verb = "runas";
            using (var proc = Process.Start(psi))
            {
                proc.WaitForExit();
                return proc.ExitCode;
            }
        }

        public static string Sha256(string file)
        {
            using (var s = File.OpenRead(file))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }
    }
}
