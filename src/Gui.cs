using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Hwbt
{
    public class Gui : Form
    {
        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")] static extern uint GetConsoleProcessList(uint[] list, uint count);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const string TechnyxUrl = "https://github.com/PredatorCZ/Technyx";
        static readonly string ToolDir = Path.GetDirectoryName(Application.ExecutablePath);
        static readonly Color CreditGray = Color.FromArgb(85, 85, 85);

        TextBox gameBox, modsBox, logBox;
        CheckedListBox modList;
        ComboBox langBox;
        CheckBox allPngBox, saveLogsBox;
        StringBuilder logCapture; // output of the running install / restore job when "save log" is checked
        Label langLabel, gameLabel, modsLabel, cpStatus;
        GroupBox modGroup, cpGroup;
        LinkLabel credits;
        readonly Dictionary<Control, string> texts = new Dictionary<Control, string>(); // control -> Lang key
        readonly List<Button> jobButtons = new List<Button>();

        public static void Run()
        {
            // Hide the console window if it was created just for us (double-click start).
            var procs = new uint[4];
            if (GetConsoleProcessList(procs, 4) == 1) ShowWindow(GetConsoleWindow(), 0);
            Lang.Load();
            Application.EnableVisualStyles();
            Application.Run(new Gui());
        }

        Gui()
        {
            Size = new Size(900, 700);
            MinimumSize = new Size(720, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // paths + language
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));    // mods
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // buttons
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // community patch
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));    // log
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // credits
            Controls.Add(root);

            // paths + language
            var paths = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            langLabel = MakeLabel("language");
            langBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            langBox.Items.AddRange(Lang.Names);
            langBox.SelectedIndex = Lang.Current;
            langBox.SelectedIndexChanged += delegate { Lang.Current = langBox.SelectedIndex; Lang.Save(); ApplyLanguage(); };
            paths.Controls.Add(langLabel, 0, 0);
            paths.Controls.Add(langBox, 1, 0);
            gameBox = new TextBox { Dock = DockStyle.Fill, Text = DetectGameDir() };
            modsBox = new TextBox { Dock = DockStyle.Fill, Text = Path.Combine(ToolDir, "mods") };
            gameLabel = AddPathRow(paths, 1, "gameDir", gameBox);
            modsLabel = AddPathRow(paths, 2, "modsDir", modsBox);
            modsBox.TextChanged += delegate { RefreshMods(); };
            gameBox.TextChanged += delegate { RefreshPatchStatus(); };
            root.Controls.Add(paths);

            // mod list
            modGroup = new GroupBox { Dock = DockStyle.Fill };
            texts[modGroup] = "modsGroup";
            modList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            modGroup.Controls.Add(modList);
            root.Controls.Add(modGroup);

            // archive / mod buttons
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            AddButton(bar, "btnInstall", InstallMods, true, "install-mods");
            AddButton(bar, "btnRestore", Uninstall, true, "restore-original");
            AddButton(bar, "btnRefresh", RefreshMods, false);
            AddButton(bar, "btnOpenMods", delegate { Directory.CreateDirectory(modsBox.Text); Process.Start("explorer.exe", "\"" + modsBox.Text + "\""); }, false);
            AddButton(bar, "btnUnpack", UnpackArchive, true);
            AddButton(bar, "btnDecompile", UnpackAndDecompile, true);
            AddButton(bar, "btnTex", ExportTextures, true);
            AddButton(bar, "btnFonts", ExportFontsGui, true);
            AddButton(bar, "btnSounds", ExportSounds, true);
            AddButton(bar, "btnLda", ExportTexts, true);
            allPngBox = new CheckBox { AutoSize = true, Checked = Lang.AllPng, Margin = new Padding(6, 7, 3, 3) };
            texts[allPngBox] = "chkAllPng";
            allPngBox.CheckedChanged += delegate { Lang.AllPng = allPngBox.Checked; Lang.Save(); };
            bar.Controls.Add(allPngBox);
            saveLogsBox = new CheckBox { AutoSize = true, Checked = Lang.SaveLogs, Margin = new Padding(6, 7, 3, 3) };
            texts[saveLogsBox] = "chkSaveLogs";
            saveLogsBox.CheckedChanged += delegate { Lang.SaveLogs = saveLogsBox.Checked; Lang.Save(); };
            bar.Controls.Add(saveLogsBox);
            root.Controls.Add(bar);

            // community patch
            cpGroup = new GroupBox { Dock = DockStyle.Fill, AutoSize = true };
            texts[cpGroup] = "communityGroup";
            var cpBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            AddButton(cpBar, "btnCpInstallV1", delegate { InstallCommunityPatch(CommunityPatch.V1); }, true);
            AddButton(cpBar, "btnCpInstallV2", delegate { InstallCommunityPatch(CommunityPatch.V2); }, true);
            AddButton(cpBar, "btnCpRestore", RemoveCommunityPatch, true);
            AddButton(cpBar, "btnCpSite", delegate { Process.Start(CommunityPatch.SiteUrl); }, false);
            cpStatus = new Label { AutoSize = true, Margin = new Padding(10, 8, 3, 3) };
            cpBar.Controls.Add(cpStatus);
            cpGroup.Controls.Add(cpBar);
            root.Controls.Add(cpGroup);

            logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window };
            root.Controls.Add(logBox);

            // credits (dark grey)
            credits = new LinkLabel
            {
                Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(3, 6, 3, 0),
                ForeColor = CreditGray, LinkColor = CreditGray, ActiveLinkColor = Color.Black, VisitedLinkColor = CreditGray,
                LinkBehavior = LinkBehavior.HoverUnderline, Font = new Font("Segoe UI", 8.25f),
            };
            credits.LinkClicked += (s, e) => { try { Process.Start((string)e.Link.LinkData); } catch { } };
            root.Controls.Add(credits);

            Archive.Log = Log;
            ApplyLanguage();
            RefreshMods();
            Log(Lang.T(File.Exists(Path.Combine(gameBox.Text, "Bin", "HOT.exe")) ? "gameFound" : "gameNotFound"));
        }

        void ApplyLanguage()
        {
            Text = "HWBT Toolkit v" + Program.Version + " - Hot Wheels: Beat That!" + (IsAdmin() ? Lang.T("admin") : "");
            foreach (var kv in texts) kv.Key.Text = Lang.T(kv.Value);

            string c = Lang.T("credits");
            credits.Text = c;
            credits.Links.Clear();
            // Every name is a link. Patch author links go to the official patch website.
            AddLink(c, "Technyx", TechnyxUrl);
            AddLink(c, "Lukas Cone (PredatorCZ)", "https://github.com/PredatorCZ");
            AddLink(c, "Kyubikkumasuta", CommunityPatch.SiteUrl);
            AddLink(c, "Beat That! Revival", CommunityPatch.SiteUrl);
            AddLink(c, Lang.T("supportLink"), CommunityPatch.SiteUrl);
            RefreshPatchStatus();
        }

        void AddLink(string text, string part, string url)
        {
            int i = text.IndexOf(part, StringComparison.Ordinal);
            if (i >= 0) credits.Links.Add(i, part.Length, url);
        }

        Label MakeLabel(string key)
        {
            var l = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
            texts[l] = key;
            return l;
        }

        Label AddPathRow(TableLayoutPanel p, int row, string key, TextBox box)
        {
            var l = MakeLabel(key);
            p.Controls.Add(l, 0, row);
            p.Controls.Add(box, 1, row);
            var b = new Button { Text = "…", Width = 32 };
            b.Click += delegate
            {
                string picked = FolderPicker.Show(this, Lang.T(key), box.Text);
                if (picked != null) box.Text = picked;
            };
            p.Controls.Add(b, 2, row);
            return l;
        }

        // logName: the job's console output is saved to logs\<logName>_<time>.log when "save log" is checked
        void AddButton(FlowLayoutPanel bar, string key, Action action, bool isJob, string logName = null)
        {
            var b = new Button { AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
            texts[b] = key;
            b.Click += delegate { if (isJob) RunJob(b.Text, action, logName); else action(); };
            bar.Controls.Add(b);
            if (isJob) jobButtons.Add(b);
        }

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            logBox.AppendText(s + Environment.NewLine);
            if (logCapture != null) logCapture.AppendLine(s);
        }

        void RunJob(string title, Action action, string logName = null)
        {
            foreach (var b in jobButtons) b.Enabled = false;
            if (logName != null && Lang.SaveLogs)
            {
                logCapture = new StringBuilder();
                logCapture.AppendLine("HWBT Toolkit v" + Program.Version + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                logCapture.AppendLine("Game folder: " + gameBox.Text);
                logCapture.AppendLine("Mods folder: " + modsBox.Text);
                logCapture.AppendLine("Checked mods (lower ones win): " +
                    (modList.CheckedItems.Count > 0 ? string.Join(", ", modList.CheckedItems.Cast<string>()) : "(none)"));
                logCapture.AppendLine();
            }
            Log("---- " + title);
            var t = new Thread(() =>
            {
                try { action(); Log(Lang.T("done")); }
                catch (UnauthorizedAccessException ex) { Log(Lang.T("noAccess") + "(" + ex.Message + ")"); BeginInvoke(new Action(OfferElevation)); }
                catch (Exception ex) { Log(Lang.T("error") + ex.Message); }
                BeginInvoke(new Action(() =>
                {
                    foreach (var b in jobButtons) b.Enabled = true;
                    if (logCapture != null) SaveCapturedLog(logName);
                    RefreshPatchStatus();
                }));
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        // Writes the captured job output to <tool>\logs\ (or %APPDATA%\HWBT-Toolkit\logs\ when that is not writable).
        void SaveCapturedLog(string logName)
        {
            string text = logCapture.ToString();
            logCapture = null;
            string file = logName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".log";
            string[] dirs =
            {
                Path.Combine(ToolDir, "logs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HWBT-Toolkit", "logs"),
            };
            string error = null;
            foreach (var dir in dirs)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, file);
                    File.WriteAllText(path, text, new UTF8Encoding(true));
                    Log(Lang.T("logSaved") + path);
                    return;
                }
                catch (Exception ex) { error = ex.Message; }
            }
            Log(Lang.T("logSaveFail") + error);
        }

        // ---------- mods ----------
        void InstallMods()
        {
            var selected = (List<string>)Invoke(new Func<List<string>>(() =>
                modList.CheckedItems.Cast<string>().Select(n => Path.Combine(modsBox.Text, n)).ToList()));
            string game = GameDir();
            if (selected.Count == 0) { Log(Lang.T("noModsChecked")); Archive.UninstallGame(game); return; }
            foreach (var d in Archive.GameDataDirs)
            {
                string dir = Path.Combine(game, d);
                if (!File.Exists(Archive.DatIn(dir))) continue;
                Log("== " + d);
                Archive.Patch(dir, selected);
            }
        }

        void Uninstall() { Archive.UninstallGame(GameDir()); }

        void UnpackArchive()
        {
            string game = GameDir();
            string outDir = (string)Invoke(new Func<string>(() => AskFolder(Lang.T("askUnpackDir"))));
            if (outDir == null) return;
            foreach (var d in Archive.GameDataDirs)
            {
                string dir = Path.Combine(game, d);
                if (File.Exists(Archive.DatIn(dir))) Archive.Unpack(dir, Path.Combine(outDir, d), null);
            }
        }

        void UnpackAndDecompile()
        {
            string game = GameDir();
            string outDir = (string)Invoke(new Func<string>(() => AskFolder(Lang.T("askDecompileDir"))));
            if (outDir == null) return;
            Log(Lang.T("decompileInfo"));
            foreach (var d in Archive.GameDataDirs)
            {
                string dir = Path.Combine(game, d);
                if (!File.Exists(Archive.DatIn(dir))) continue;
                Log("== " + d);
                Decompiler.UnpackAll(dir, Path.Combine(outDir, d), false, Lang.AllPng);
            }
            Process.Start("explorer.exe", "\"" + outDir + "\"");
        }

        void ExportTextures()
        {
            string[] files = null; string outDir = null;
            Invoke(new Action(() =>
            {
                using (var d = new OpenFileDialog { Filter = "Arcbank (*.ARC)|*.ARC", Multiselect = true, Title = Lang.T("pickArc") })
                    if (d.ShowDialog(this) == DialogResult.OK) files = d.FileNames;
                if (files != null) outDir = AskFolder(Lang.T("askTexDir"));
            }));
            if (files == null || outDir == null) return;
            foreach (var f in files)
            {
                // <out>\<BANK.ARC>\*.png|tga - the same layout a mod uses, so edited files can be copied straight over
                int n = ArcTools.ExportTextures(File.ReadAllBytes(f), Path.Combine(outDir, Path.GetFileName(f)), "image", Lang.AllPng);
                Log(Lang.F("texCount", Path.GetFileName(f), n));
            }
            Log(Lang.T("texHint1"));
            Log(Lang.T("texHint2"));
        }

        void ExportFontsGui()
        {
            string[] files = null; string outDir = null;
            Invoke(new Action(() =>
            {
                using (var d = new OpenFileDialog { Filter = "Arcbank (*.ARC)|*.ARC", Multiselect = true, Title = Lang.T("pickArcFonts") })
                    if (d.ShowDialog(this) == DialogResult.OK) files = d.FileNames;
                if (files != null) outDir = AskFolder(Lang.T("askFontDir"));
            }));
            if (files == null || outDir == null) return;
            foreach (var f in files)
            {
                // <out>\<BANK.ARC>\<name>.font\ (font.txt + atlas.png) - same layout as in a mod
                int n = ArcTools.ExportFonts(File.ReadAllBytes(f), Path.Combine(outDir, Path.GetFileName(f)), false);
                Log(n > 0 ? Lang.F("fontsExported", Path.GetFileName(f), n) : Lang.F("fontNone", Path.GetFileName(f)));
            }
            Log(Lang.T("fontHint"));
        }

        void ExportSounds()
        {
            string[] files = null; string outDir = null;
            Invoke(new Action(() =>
            {
                using (var d = new OpenFileDialog { Filter = "Sound banks (*.HDR)|*.HDR", Multiselect = true, Title = Lang.T("pickHdr") })
                    if (d.ShowDialog(this) == DialogResult.OK) files = d.FileNames;
                if (files != null) outDir = AskFolder(Lang.T("askSoundDir"));
            }));
            if (files == null || outDir == null) return;
            foreach (var f in files)
            {
                string bank = Path.GetFileNameWithoutExtension(f);
                byte[] hdr = File.ReadAllBytes(f);
                // <out>\<BANK>.HDR\*.wav|ogg - same layout as in a mod
                string dst = Path.Combine(outDir, bank + ".HDR");
                if (Sound.IsSpeechBank(hdr))
                {
                    string spd = Path.ChangeExtension(f, ".SPD");
                    if (!File.Exists(spd)) { Log(Lang.F("soundNoData", Path.GetFileName(f), ".SPD")); continue; }
                    Log(Lang.F("soundCount", Path.GetFileName(f), Sound.SpdToOgg(hdr, spd, dst)));
                }
                else if (Sound.IsSoundBank(hdr))
                {
                    string raw = Path.ChangeExtension(f, ".RAW");
                    if (!File.Exists(raw)) { Log(Lang.F("soundMusicIndex", Path.GetFileName(f))); continue; }
                    Log(Lang.F("soundCount", Path.GetFileName(f), Sound.HdrToWav(hdr, raw, dst)));
                }
                else Log(Lang.T("error") + Path.GetFileName(f) + ": not a sound bank");
            }
            Log(Lang.T("soundHint"));
        }

        void ExportTexts()
        {
            string[] files = null;
            Invoke(new Action(() =>
            {
                using (var d = new OpenFileDialog { Filter = Lang.T("ldaFilter"), Multiselect = true })
                    if (d.ShowDialog(this) == DialogResult.OK) files = d.FileNames;
            }));
            if (files == null) return;
            foreach (var f in files)
            {
                string o = Path.ChangeExtension(f, ".txt");
                File.WriteAllText(o, Lda.Parse(File.ReadAllBytes(f)).ToText(), new System.Text.UTF8Encoding(true));
                Log("-> " + o);
            }
            Log(Lang.T("ldaHint"));
        }

        // ---------- community patch ----------
        void InstallCommunityPatch(PatchInfo p)
        {
            string bin = Path.Combine(GameDir(), "Bin");
            string name = Lang.T(p.Name);
            if (p == CommunityPatch.V1) Log(Lang.T("cpV2HasV1"));
            string patchDir = CommunityPatch.FindPatchDir(ToolDir, p);
            if (patchDir == null)
            {
                if (Ask(Lang.F("cpAskDownload", name, p.PageUrl), MessageBoxIcon.Question) != DialogResult.Yes) return;
                Log(Lang.F("cpDownloading", name));
                patchDir = CommunityPatch.Download(ToolDir, p,
                    hash => Ask(Lang.F("cpHashMismatch", hash), MessageBoxIcon.Warning) == DialogResult.Yes);
                Log(Lang.T("cpDownloaded") + patchDir);
            }
            if (Ask(Lang.F("cpAskInstall", name, bin), MessageBoxIcon.Question) != DialogResult.Yes) return;
            bool canWrite = IsAdmin() || CanWrite(bin);
            if (canWrite) CommunityPatch.CopyToBin(patchDir, bin);
            else
            {
                // Copy via an elevated robocopy, then run the installer elevated.
                RunElevated("robocopy.exe", "\"" + patchDir + "\" \"" + bin + "\" /E /XD source /NJH /NJS /NFL /NDL");
            }
            Log(Lang.T("cpRunning"));
            bool elevate = !IsAdmin() && (!canWrite || p.AlwaysElevate);
            Log(Lang.F("cpExit", CommunityPatch.RunScript(bin, p.InstallBat, elevate)));
        }

        // Removes the most recently installed patch with the author's restore script
        // (V2 restore returns HOT.exe to its pre-V2 state, which may be V1).
        void RemoveCommunityPatch()
        {
            string bin = Path.Combine(GameDir(), "Bin");
            var state = CommunityPatch.GetState(bin);
            if (state == CommunityPatch.State.NotInstalled) { Log(Lang.T("cpNothing")); return; }
            var p = state == CommunityPatch.State.V2 ? CommunityPatch.V2 : CommunityPatch.V1;
            if (!File.Exists(Path.Combine(bin, p.RestoreBat))) { Log(Lang.F("cpNoRestore", p.RestoreBat)); return; }
            if (Ask(Lang.F("cpAskRestore", Lang.T(p.Name)), MessageBoxIcon.Question) != DialogResult.Yes) return;
            Log(Lang.T("cpRunning"));
            bool elevate = !IsAdmin() && (p.AlwaysElevate || !CanWrite(bin));
            Log(Lang.F("cpExit", CommunityPatch.RunScript(bin, p.RestoreBat, elevate)));
        }

        void RefreshPatchStatus()
        {
            if (InvokeRequired) { BeginInvoke(new Action(RefreshPatchStatus)); return; }
            string bin = Path.Combine(gameBox.Text, "Bin");
            if (!File.Exists(Path.Combine(bin, "HOT.exe"))) { cpStatus.Text = Lang.T("cpUnknown"); cpStatus.ForeColor = SystemColors.GrayText; return; }
            var state = CommunityPatch.GetState(bin);
            cpStatus.Text = Lang.T(state == CommunityPatch.State.V2 ? "cpInstalledV2" : state == CommunityPatch.State.V1 ? "cpInstalledV1" : "cpNotInstalled");
            cpStatus.ForeColor = state != CommunityPatch.State.NotInstalled ? Color.DarkGreen : SystemColors.ControlText;
        }

        static void RunElevated(string exe, string args)
        {
            using (var p = Process.Start(new ProcessStartInfo(exe, args) { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }))
                p.WaitForExit();
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".hwbt_write_test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // ---------- helpers ----------
        DialogResult Ask(string text, MessageBoxIcon icon)
        {
            return (DialogResult)Invoke(new Func<DialogResult>(() =>
                MessageBox.Show(this, text, Lang.T("cpTitle"), MessageBoxButtons.YesNo, icon)));
        }

        void RefreshMods()
        {
            // keep a mods\_template\ (Data\ + lData\ layout) next to the user's mods
            try
            {
                if (!string.IsNullOrEmpty(modsBox.Text) && (Directory.Exists(modsBox.Text) || modsBox.Text == Path.Combine(ToolDir, "mods")))
                {
                    Directory.CreateDirectory(modsBox.Text);
                    if (ModTemplate.Ensure(modsBox.Text)) Log(Lang.F("templateCreated", Path.Combine(modsBox.Text, ModTemplate.FolderName)));
                }
            }
            catch (Exception ex) { Log(Lang.T("error") + ex.Message); }
            modList.Items.Clear();
            foreach (var d in Archive.ModFolders(modsBox.Text)) modList.Items.Add(Path.GetFileName(d), true);
        }

        string GameDir()
        {
            string g = (string)Invoke(new Func<string>(() => gameBox.Text));
            if (!File.Exists(Path.Combine(g, "Data", "CDFILES.DAT")))
                throw new IOException(Lang.T("gameDirBad") + g);
            if (Process.GetProcessesByName("HOT").Length > 0)
                throw new IOException(Lang.T("gameRunning"));
            return g;
        }

        string lastFolder;

        string AskFolder(string title)
        {
            string picked = FolderPicker.Show(this, title, lastFolder);
            if (picked != null) lastFolder = picked;
            return picked;
        }

        static string DetectGameDir()
        {
            var candidates = new List<string>
            {
                Path.GetDirectoryName(ToolDir),
                ToolDir,
                @"C:\Program Files (x86)\Activision Value\Hot Wheels",
                @"C:\Program Files\Activision Value\Hot Wheels",
                @"C:\Program Files (x86)\Activision\Hot Wheels Beat That",
            };
            foreach (var c in candidates)
                if (c != null && File.Exists(Path.Combine(c, "Data", "CDFILES.DAT"))) return c;
            return candidates[2];
        }

        static bool IsAdmin()
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }

        void OfferElevation()
        {
            if (IsAdmin()) return;
            var r = MessageBox.Show(this, Lang.T("elevateText"), Lang.T("elevateTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
                Close();
            }
            catch (Exception ex) { Log(Lang.T("elevateFail") + ex.Message); }
        }
    }
}
