// AzerothSync window. Without arguments: settings and buttons.
// With --play: download, start the game, upload after it closes, then close.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AzerothSync
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Only one AzerothSync per Windows user, two at once could work on the same folders
            bool first;
            using (var mutex = new Mutex(true, "Local\\AzerothSync-" + Environment.UserName, out first))
            {
                if (!first)
                {
                    MessageBox.Show("AzerothSync is already running.", "AzerothSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new MainForm(args.Any(a => a.Equals("--play", StringComparison.OrdinalIgnoreCase))));
            }
        }
    }

    partial class MainForm : Form
    {
        public const string Version = "1.5.0";
        readonly bool autoPlay;
        Settings settings;
        Control settingsGroup;
        TextBox tbGame, tbCloud, tbSave, tbConfig;
        Button btnPlay, btnPull, btnPush, btnStatus, btnShortcut;
        RichTextBox log;
        BronzeProgress progress;
        Label progressText;
        bool busy;

        public MainForm(bool autoPlay) : this(autoPlay, false) { }

        public MainForm(bool autoPlay, bool designPreview)
        {
            this.designPreview = designPreview;
            this.autoPlay = autoPlay;
            settings = designPreview ? SampleSettings() : Settings.Load(Settings.DefaultStateDir(), Path.GetDirectoryName(Application.ExecutablePath));
            pathCheckTimer.Tick += delegate { RefreshPathChecks(); };
            FormClosed += delegate { cloudScanSequence++;cloudInspectSequence++;wizardCheckSequence++;wizardActive=false;pathCheckTimer.Dispose(); };
            BuildWindow();

            Shown += delegate
            {
                Theme.DarkChrome(this);
                if (designPreview) { ShowSampleState(); return; }
                cloudWindowShown=true;StartCloudScan(false);
                if (!settings.SetupDone) OpenWizard();
                else if (autoPlay) Run(e => e.Play());
                else
                {
                    if (!File.Exists(settings.GamePath)) Write("Game not found. Put AzerothSync.exe into the game folder or choose the game above.", Level.Warn);
                    if (!Directory.Exists(settings.ConfigDir)) Write("Game config folder not found. Choose the benilla-config folder above, otherwise AddOns are skipped.", Level.Warn);
                    if (settings.CloudDir.Length == 0) Write("Choose a sync folder inside your cloud drive to get started.", Level.Warn);
                    else Run(e => e.Status());
                }
            };
            FormClosing += (s, e) =>
            {
                if (busy && e.CloseReason == CloseReason.UserClosing &&
                    MessageBox.Show(this, "A sync is still running. Close anyway?", "AzerothSync", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    e.Cancel = true;
                if (!e.Cancel && wizardActive)
                {
                    if (!busy) CompleteSetup(false);
                    else if (!designPreview)
                    {
                        settings.SetupDone = settings.CloudDir.Length > 0;
                        try { settings.Save(); } catch { }
                    }
                }
            };
        }

        Button MakeButton(string text, string tip, EventHandler click)
        {
            var b = new BronzeButton { Text = text, Width = 158, Height = 38, Margin = new Padding(0, 0, 9, 0) };
            lookTips.SetToolTip(b, tip);
            b.Click += click;
            return b;
        }

        void SaveSettings()
        {
            if (designPreview || busy) return;
            settings.GamePath = tbGame.Text.Trim();
            settings.CloudDir = tbCloud.Text.Trim();
            settings.SaveDir = tbSave.Text.Trim();
            settings.ConfigDir = tbConfig.Text.Trim();
            try { settings.Save(); } catch (Exception ex) { Write("Settings not saved: " + ex.Message, Level.Error); }
        }

        void Write(string text, Level level)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, Level>(Write), text, level); return; }
            Color c = Theme.Text;
            if (level == Level.Good) c = Theme.Good;
            else if (level == Level.Warn) c = Theme.Warning;
            else if (level == Level.Error) c = Theme.Error;
            else if (level == Level.Dim) c = Theme.Muted;
            else if (text.EndsWith("...")) c = Theme.Gold;
            log.SelectionStart = log.TextLength;
            log.SelectionColor = c;
            log.AppendText(text + "\n");
            log.ScrollToCaret();
            UpdateAreaState(text, level);
            if (text.StartsWith("Starting Azeroth Survivors")) progressText.Text = "Game running · sync follows after you close it";
            else if (text == "Game closed.") progressText.Text = "Game closed · saving your progress";
            if (text.Length > 0) FileLog(level.ToString().ToUpperInvariant() + " " + text.Trim());
        }

        // Local diagnosis log, about 1 MB, the previous one is kept as log.old.txt
        void FileLog(string line)
        {
            if (designPreview) return;
            try
            {
                string dir = settings.StateDir, file = Path.Combine(dir, "log.txt");
                Directory.CreateDirectory(dir);
                if (File.Exists(file) && new FileInfo(file).Length > 1048576)
                {
                    string old = Path.Combine(dir, "log.old.txt");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(file, old);
                }
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + "\r\n");
            }
            catch { }
        }

        // percent -1: keep the space, but show an empty bar and no text
        void ShowProgress(string text, int percent)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, int>(ShowProgress), text, percent); return; }
            progressText.Text = percent < 0 ? "Ready" : TranslateProgress(text);
            progressText.ForeColor = percent < 0 ? Theme.Muted : Theme.Text;
            progress.Value = Math.Max(0, Math.Min(100, percent));
            if (wizardActive && wizardFeedback != null) wizardFeedback.Text = progressText.Text;
        }

        void SetBusy(bool on)
        {
            busy = on;
            if(on){cloudScanSequence++;cloudScanning=false;InvalidateCloudInfo();}
            else if(cloudWindowShown)StartCloudScan(false);
            foreach (var b in new[] { btnPlay, btnPull, btnPush, btnStatus, btnShortcut }) b.Enabled = !on;
            settingsGroup.Enabled = !on;
            if (wizardPage != null) wizardPage.Enabled = !on;
            if (wizardFeedback != null) wizardFeedback.Text = on ? "Sync running. Please wait; details are in Log." : progressText.Text;
        }

        // work returns true only if everything it should do was done
        void Run(Func<Engine, bool> work, Action<bool> completed = null)
        {
            if (busy) return;
            if (designPreview) { AnimateSample(completed); return; }
            SaveSettings();
            // The running sync gets its own copy of the settings
            var engine = new Engine(settings.Clone());
            engine.Log = Write;
            engine.Progress = ShowProgress;
            engine.Confirm = q => (bool)Invoke(new Func<bool>(() =>
                ThemedDialog.Ask(this, "Confirm change", TranslateQuestion(q), false) == Choice.Local));
            engine.AskConflict = (sets, pulling) => (Choice)Invoke(new Func<Choice>(() => ConflictDialog.Ask(this, engine, sets, pulling)));
            SetBusy(true);
            pathCheckTimer.Stop();
            Write("", Level.Info);
            var t = new Thread(() =>
            {
                bool ok = false;
                try { ok = work(engine); }
                catch (Exception ex)
                {
                    Write("ERROR: " + ex.Message, Level.Error);
                    FileLog("EXCEPTION " + ex);
                }
                BeginInvoke(new Action(() =>
                {
                    SetBusy(false);
                    ShowProgress("", -1);
                    progressText.Text = ok ? "Done" : "Sync not complete · see Log";
                    progressText.ForeColor = ok ? Theme.Good : Theme.Warning;
                    if (wizardActive) wizardFeedback.Text = progressText.Text;
                    RefreshPathChecks();
                    if (completed != null) completed(ok);
                    if (wizardActive && wizardPage.Visible && wizardStep==2) CheckWizardAdvice(null);
                    if (autoPlay && !wizardActive && !ok)
                        Write("Not everything was synced, see above. The window stays open. Fix the cause, then use Upload or Download.", Level.Warn);
                    if (autoPlay && !wizardActive && ok)
                    {
                        Write("Done, closing in a moment.", Level.Dim);
                        var timer = new System.Windows.Forms.Timer { Interval = 4000 };
                        timer.Tick += delegate { timer.Stop(); Close(); };
                        timer.Start();
                    }
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        void CreateShortcut()
        {
            if (designPreview) return;
            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Azeroth Survivors (Sync).lnk");
                var link = (IShellLinkW)new ShellLink();
                link.SetPath(Application.ExecutablePath);
                link.SetArguments("--play");
                link.SetWorkingDirectory(Path.GetDirectoryName(Application.ExecutablePath));
                link.SetIconLocation(File.Exists(settings.GamePath) ? settings.GamePath : Application.ExecutablePath, 0);
                link.SetDescription("Sync, play Azeroth Survivors, sync again");
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(lnk, true);
                Write("Shortcut created: " + lnk, Level.Good);
            }
            catch (Exception ex) { Write("Could not create the shortcut: " + ex.Message, Level.Error); }
        }
    }

    // Windows shell link (.lnk) through the native Shell API
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    // One question for all areas in conflict
    static class ConflictDialog
    {
        public static Choice Ask(IWin32Window owner, Engine engine, List<SyncSet> sets, bool pulling)
        {
            var lines = new List<string> { "Your local data and the cloud both changed since the last sync.", "" };
            foreach (var set in sets)
                lines.Add(set.Label +
                    " · local " + Engine.Newest(set.Dir, set.Exclude) + " · cloud " + Engine.Newest(set.Cloud, set.Exclude));
            lines.Add("");
            lines.Add(engine.CloudInfo());
            lines.Add("");
            lines.Add("Your choice applies to all areas listed.\nThe replaced state is backed up first.");
            return ThemedDialog.Ask(owner, "Which state do you want to keep?", string.Join("\n", lines), true);
        }
    }
}
