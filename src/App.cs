// AzerothSync window. Without arguments: settings and buttons.
// With --play: download, start the game, upload after it closes, then close.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
            Application.Run(new MainForm(args.Any(a => a.Equals("--play", StringComparison.OrdinalIgnoreCase))));
        }
    }

    class MainForm : Form
    {
        public const string Version = "1.1.1";
        readonly bool autoPlay;
        Settings settings;
        TextBox tbGame, tbCloud, tbSave, tbConfig;
        Button btnPlay, btnPull, btnPush, btnStatus, btnShortcut;
        RichTextBox log;
        bool busy;

        public MainForm(bool autoPlay)
        {
            this.autoPlay = autoPlay;
            settings = Settings.Load(Settings.DefaultStateDir(), Path.GetDirectoryName(Application.ExecutablePath));
            Text = "AzerothSync " + Version;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(720, 560);
            MinimumSize = new Size(560, 460);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var group = new GroupBox { Text = "Settings (this device only)", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbGame = AddRow(grid, "Game (AzerothSurvivors.exe)", settings.GamePath, true,
                "The game executable. Used to start the game and to detect whether it is running.");
            tbCloud = AddRow(grid, "Sync folder (cloud)", settings.CloudDir, false,
                "A folder that your cloud drive (Google Drive, OneDrive, Dropbox, ...) keeps in sync between your PCs.");
            tbSave = AddRow(grid, "Save game folder", settings.SaveDir, false,
                "Usually %APPDATA%\\wenilla-rogue");
            tbConfig = AddRow(grid, "Game config folder", settings.ConfigDir, false,
                "The benilla-config folder that contains AddOns and saved. Taken from next to the game if it exists there, otherwise " + Settings.FallbackConfigDir + ".");
            group.Controls.Add(grid);
            root.Controls.Add(group);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
            btnPlay = MakeButton("Play", "Download, start the game, upload after it closes", delegate { Run(e => e.Play()); });
            btnPlay.Font = new Font(Font, FontStyle.Bold);
            btnPull = MakeButton("Download", "Replace your local data with the cloud state (one confirmation)", delegate { Run(e => e.Pull(true)); });
            btnPush = MakeButton("Upload", "Replace the cloud state with your local data (one confirmation)", delegate { Run(e => e.Push(true)); });
            btnStatus = MakeButton("Status", "Shows which side is newer", delegate { Run(e => e.Status()); });
            btnShortcut = MakeButton("Desktop shortcut", "Creates \"Azeroth Survivors (Sync)\" on the desktop, which runs Play directly", delegate { CreateShortcut(); });
            buttons.Controls.AddRange(new Control[] { btnPlay, btnPull, btnPush, btnStatus, btnShortcut });
            root.Controls.Add(buttons);

            log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(24, 24, 28), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None };
            root.Controls.Add(log);

            Shown += delegate
            {
                if (autoPlay) Run(e => e.Play());
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
            };
        }

        TextBox AddRow(TableLayoutPanel grid, string label, string value, bool file, string tip)
        {
            int row = grid.RowCount++;
            var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
            new ToolTip().SetToolTip(l, tip);
            grid.Controls.Add(l, 0, row);
            var tb = new TextBox { Text = value, Dock = DockStyle.Fill };
            tb.Leave += delegate { SaveSettings(); };
            grid.Controls.Add(tb, 1, row);
            var b = new Button { Text = "...", Width = 32, Height = tb.Height + 2 };
            b.Click += delegate
            {
                if (file)
                {
                    using (var d = new OpenFileDialog { Filter = "Program (*.exe)|*.exe", FileName = tb.Text })
                    {
                        try { d.InitialDirectory = Path.GetDirectoryName(tb.Text); } catch { }
                        if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.FileName;
                    }
                }
                else
                {
                    using (var d = new FolderBrowserDialog { SelectedPath = tb.Text, ShowNewFolderButton = true })
                        if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.SelectedPath;
                }
                SaveSettings();
            };
            grid.Controls.Add(b, 2, row);
            return tb;
        }

        Button MakeButton(string text, string tip, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 8, 0) };
            new ToolTip().SetToolTip(b, tip);
            b.Click += click;
            return b;
        }

        void SaveSettings()
        {
            settings.GamePath = tbGame.Text.Trim();
            settings.CloudDir = tbCloud.Text.Trim();
            settings.SaveDir = tbSave.Text.Trim();
            settings.ConfigDir = tbConfig.Text.Trim();
            try { settings.Save(); } catch (Exception ex) { Write("Settings not saved: " + ex.Message, Level.Error); }
        }

        void Write(string text, Level level)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, Level>(Write), text, level); return; }
            Color c = Color.Gainsboro;
            if (level == Level.Good) c = Color.FromArgb(120, 220, 120);
            else if (level == Level.Warn) c = Color.FromArgb(240, 200, 90);
            else if (level == Level.Error) c = Color.FromArgb(255, 110, 110);
            else if (level == Level.Dim) c = Color.Gray;
            else if (text.EndsWith("...")) c = Color.FromArgb(110, 190, 255);
            log.SelectionStart = log.TextLength;
            log.SelectionColor = c;
            log.AppendText(text + "\n");
            log.ScrollToCaret();
        }

        void SetBusy(bool on)
        {
            busy = on;
            foreach (var b in new[] { btnPlay, btnPull, btnPush, btnStatus, btnShortcut }) b.Enabled = !on;
            foreach (var t in new[] { tbGame, tbCloud, tbSave, tbConfig }) t.ReadOnly = on;
            UseWaitCursor = on;
        }

        void Run(Action<Engine> work)
        {
            if (busy) return;
            SaveSettings();
            var engine = new Engine(settings);
            engine.Log = Write;
            engine.Confirm = q => (bool)Invoke(new Func<bool>(() =>
                MessageBox.Show(this, q, "AzerothSync", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes));
            engine.AskConflict = (sets, pulling) => (Choice)Invoke(new Func<Choice>(() => ConflictDialog.Ask(this, engine, sets, pulling)));
            SetBusy(true);
            Write("", Level.Info);
            var t = new Thread(() =>
            {
                bool ok = true;
                try { work(engine); }
                catch (Exception ex) { ok = false; Write("ERROR: " + ex.Message, Level.Error); }
                BeginInvoke(new Action(() =>
                {
                    SetBusy(false);
                    if (autoPlay && ok)
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
            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Azeroth Survivors (Sync).lnk");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic sc = shell.CreateShortcut(lnk);
                sc.TargetPath = Application.ExecutablePath;
                sc.Arguments = "--play";
                sc.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                sc.IconLocation = (File.Exists(settings.GamePath) ? settings.GamePath : Application.ExecutablePath) + ",0";
                sc.Save();
                Write("Shortcut created: " + lnk, Level.Good);
            }
            catch (Exception ex) { Write("Could not create the shortcut: " + ex.Message, Level.Error); }
        }
    }

    // One question for all areas in conflict
    static class ConflictDialog
    {
        public static Choice Ask(IWin32Window owner, Engine engine, List<SyncSet> sets, bool pulling)
        {
            var f = new Form
            {
                Text = "AzerothSync: conflict",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = new Font("Segoe UI", 9f), Padding = new Padding(12)
            };
            var lines = new List<string> { "Both your local data and the cloud changed since the last sync:", "" };
            foreach (var s in sets)
                lines.Add("• " + s.Label + ": local " + Engine.Newest(s.Dir, s.Exclude) + ", cloud " + Engine.Newest(s.Cloud, s.Exclude));
            lines.Add("");
            lines.Add("Cloud: " + engine.CloudInfo());
            lines.Add("");
            lines.Add("Your choice applies to all areas listed. The overwritten state is backed up first.");
            var text = new Label { Text = string.Join("\n", lines), AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 0, 0, 12) };
            var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight };
            Choice result = Choice.Abort;
            Func<string, Choice, Button> mk = (t, c) =>
            {
                var b = new Button { Text = t, AutoSize = true, Padding = new Padding(8, 3, 8, 3) };
                b.Click += delegate { result = c; f.Close(); };
                panel.Controls.Add(b);
                return b;
            };
            var bCloud = mk("Use cloud", Choice.Cloud);
            var bLocal = mk("Keep local", Choice.Local);
            var bAbort = mk("Cancel", Choice.Abort);
            f.AcceptButton = pulling ? bCloud : bLocal;
            f.CancelButton = bAbort;
            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            layout.Controls.Add(text);
            layout.Controls.Add(panel);
            f.Controls.Add(layout);
            f.ShowDialog(owner);
            return result;
        }
    }
}
