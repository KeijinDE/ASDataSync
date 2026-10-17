// AzerothSync core: syncs save game, addons and addon settings of Azeroth Survivors through a cloud folder.
// No merging: per area one complete state wins, the overwritten one is backed up first.
// Hash, state.json and cloud layout are compatible with the older AzerothSync.ps1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AzerothSync
{
    public enum Choice { Cloud, Local, Abort }
    public enum Level { Info, Good, Warn, Error, Dim }

    public class Settings
    {
        public const string DefaultGamePath = @"C:\Games\AzerothSurvivors\AzerothSurvivors.exe";

        public string GamePath, CloudDir, SaveDir, ConfigDir, StateDir;

        public static string DefaultStateDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzerothSync");
        }

        public static string DefaultSaveDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "wenilla-rogue");
        }

        // The game keeps AddOns and saved (SavedVariables) in benilla-config next to its executable
        public static string DefaultConfigDir(string gamePath)
        {
            try { return Path.Combine(Path.GetDirectoryName(gamePath), "benilla-config"); }
            catch { return ""; }
        }

        // If this program sits inside an existing sync folder, that folder is the default
        public static string DefaultCloudDir(string exeDir)
        {
            if (exeDir != null && (File.Exists(Path.Combine(exeDir, "manifest.json")) || Directory.Exists(Path.Combine(exeDir, "data"))))
                return exeDir;
            return "";
        }

        // settings.ini in the state folder; without it config.json of the old script, then defaults
        public static Settings Load(string stateDir, string exeDir)
        {
            var s = new Settings();
            s.StateDir = stateDir;
            s.GamePath = DefaultGamePath;
            s.CloudDir = "";
            s.SaveDir = DefaultSaveDir();
            s.ConfigDir = "";
            string ini = Path.Combine(stateDir, "settings.ini");
            string json = Path.Combine(stateDir, "config.json");
            if (File.Exists(ini))
            {
                foreach (string line in File.ReadAllLines(ini))
                {
                    int i = line.IndexOf('=');
                    if (i < 1) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (v.Length == 0) continue;
                    if (k == "GamePath") s.GamePath = v;
                    else if (k == "CloudDir") s.CloudDir = v;
                    else if (k == "SaveDir") s.SaveDir = v;
                    else if (k == "ConfigDir") s.ConfigDir = v;
                }
            }
            else if (File.Exists(json))
            {
                try
                {
                    var d = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(json));
                    object v;
                    if (d.TryGetValue("GamePath", out v) && v != null) s.GamePath = v.ToString();
                    if (d.TryGetValue("CloudDir", out v) && v != null) s.CloudDir = v.ToString();
                    if (d.TryGetValue("ConfigDir", out v) && v != null) s.ConfigDir = v.ToString();
                }
                catch { }
            }
            if (s.CloudDir.Length == 0) s.CloudDir = DefaultCloudDir(exeDir);
            if (s.ConfigDir.Length == 0) s.ConfigDir = DefaultConfigDir(s.GamePath);
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(StateDir);
            File.WriteAllLines(Path.Combine(StateDir, "settings.ini"), new[] {
                "GamePath=" + GamePath, "CloudDir=" + CloudDir, "SaveDir=" + SaveDir, "ConfigDir=" + ConfigDir });
        }
    }

    public class SyncSet
    {
        public string Name, Label, Dir, Cloud;
        public string[] Exclude;
        public bool Optional;
    }

    class FileEntry
    {
        public string Rel, Full;
    }

    public class Engine
    {
        const int KeepLocalBackups = 20, KeepCloudBackups = 10;
        const string None = "NONE", Empty = "EMPTY";

        public Settings S;
        public List<SyncSet> Sets;
        public Action<string, Level> Log = delegate { };
        // One question for all areas in conflict
        public Func<List<SyncSet>, bool, Choice> AskConflict = delegate { return Choice.Abort; };
        public Func<string, bool> Confirm = delegate { return false; };

        string CloudData { get { return Path.Combine(S.CloudDir, "data"); } }
        string CloudHistory { get { return Path.Combine(S.CloudDir, "history"); } }
        string Manifest { get { return Path.Combine(S.CloudDir, "manifest.json"); } }
        string StateFile { get { return Path.Combine(S.StateDir, "state.json"); } }

        public Engine(Settings s)
        {
            S = s;
            Sets = new List<SyncSet> {
                new SyncSet { Name = "save", Label = "Save game", Dir = s.SaveDir, Exclude = new[] { "language.txt" } },
                new SyncSet { Name = "addons", Label = "AddOns", Dir = Path.Combine(s.ConfigDir, "AddOns"), Exclude = new string[0], Optional = true },
                new SyncSet { Name = "saved", Label = "Addon settings", Dir = Path.Combine(s.ConfigDir, "saved"), Exclude = new string[0], Optional = true },
            };
            foreach (var set in Sets) set.Cloud = Path.Combine(CloudData, set.Name);
        }

        // ---------- Files ----------

        static List<FileEntry> Files(string dir, string[] exclude)
        {
            var list = new List<FileEntry>();
            if (!Directory.Exists(dir)) return list;
            string root = Path.GetFullPath(dir).TrimEnd('\\');
            foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(root.Length + 1);
                if (!exclude.Contains(rel, StringComparer.OrdinalIgnoreCase)) list.Add(new FileEntry { Rel = rel, Full = f });
            }
            return list;
        }

        static string Hex(byte[] b)
        {
            return BitConverter.ToString(b).Replace("-", "");
        }

        // Same as Get-TreeHash in the script: lines "path:HASH", sorted like Sort-Object (culture, case insensitive)
        public static string TreeHash(string dir, string[] exclude)
        {
            if (!Directory.Exists(dir)) return None;
            var files = Files(dir, exclude);
            if (files.Count == 0) return Empty;
            var lines = new List<string>();
            using (var sha = SHA256.Create())
            {
                foreach (var f in files)
                    using (var st = File.OpenRead(f.Full))
                        lines.Add(f.Rel.ToLower() + ":" + Hex(sha.ComputeHash(st)));
                lines.Sort(StringComparer.Create(CultureInfo.CurrentCulture, true));
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
            }
        }

        static bool IsEmpty(string h) { return h == None || h == Empty; }

        public static string Newest(string dir, string[] exclude)
        {
            var files = Files(dir, exclude);
            if (files.Count == 0) return "-";
            return files.Max(f => File.GetLastWriteTime(f.Full)).ToString("yyyy-MM-dd HH:mm");
        }

        // Make the target an exact copy of the source (excluded files stay untouched)
        static void Mirror(string src, string dst, string[] exclude)
        {
            Directory.CreateDirectory(dst);
            var srcFiles = Files(src, exclude);
            var keep = new HashSet<string>(srcFiles.Select(f => f.Rel), StringComparer.OrdinalIgnoreCase);
            foreach (var f in Files(dst, exclude))
                if (!keep.Contains(f.Rel)) { File.SetAttributes(f.Full, FileAttributes.Normal); File.Delete(f.Full); }
            foreach (string d in Directory.GetDirectories(dst, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
            foreach (var f in srcFiles)
            {
                string target = Path.Combine(dst, f.Rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                File.Copy(f.Full, target, true);
            }
        }

        void Backup(string src, string[] exclude, string backupDir, string prefix, int keep)
        {
            var files = Files(src, exclude);
            if (files.Count == 0) return;
            Directory.CreateDirectory(backupDir);
            string zip = Path.Combine(backupDir, prefix + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".zip");
            if (File.Exists(zip)) File.Delete(zip);
            using (var a = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (var f in files) a.CreateEntryFromFile(f.Full, f.Rel.Replace('\\', '/'));
            foreach (string old in Directory.GetFiles(backupDir, prefix + "_*.zip").OrderByDescending(x => x).Skip(keep))
                File.Delete(old);
            Log("    Backup: " + zip, Level.Dim);
        }

        // ---------- State ----------

        Dictionary<string, string> LoadState()
        {
            var h = new Dictionary<string, string>();
            if (!File.Exists(StateFile)) return h;
            try
            {
                var j = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(StateFile));
                object v;
                if (j.TryGetValue("Hashes", out v) && v is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)v) h[kv.Key] = kv.Value == null ? null : kv.Value.ToString();
                else if (j.TryGetValue("LastHash", out v) && v != null) h["save"] = v.ToString();
            }
            catch { }
            return h;
        }

        void SetLastHash(string name, string hash)
        {
            var h = LoadState();
            h[name] = hash;
            Directory.CreateDirectory(S.StateDir);
            var o = new Dictionary<string, object> {
                { "Hashes", h }, { "LastSync", DateTime.Now.ToString("o") }, { "Device", Environment.MachineName } };
            File.WriteAllText(StateFile, new JavaScriptSerializer().Serialize(o), new UTF8Encoding(false));
        }

        public string CloudInfo()
        {
            try
            {
                if (File.Exists(Manifest))
                {
                    var m = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Manifest));
                    DateTime t = DateTime.Parse(m["Time"].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
                    return "last uploaded by " + m["Device"] + " on " + t.ToString("yyyy-MM-dd HH:mm");
                }
            }
            catch { }
            return "no upload info";
        }

        // ---------- Checks ----------

        public bool GameRunning()
        {
            string name = Path.GetFileNameWithoutExtension(S.GamePath);
            return Process.GetProcessesByName(name).Length > 0;
        }

        void PrepareCloud()
        {
            if (string.IsNullOrEmpty(S.CloudDir))
                throw new Exception("No sync folder set. Choose a folder in Google Drive, OneDrive, Dropbox or similar above.");
            string parent = Path.GetDirectoryName(S.CloudDir.TrimEnd('\\'));
            if (parent == null || !Directory.Exists(parent))
                throw new Exception("Sync folder not reachable: " + S.CloudDir + " (is your cloud drive running?)");
            Directory.CreateDirectory(S.CloudDir);
            // Very old layout: the save game was directly in data\, now it is data\save\
            bool oldSave = Directory.Exists(Path.Combine(CloudData, "Saves")) || File.Exists(Path.Combine(CloudData, "characters.txt"));
            if (oldSave && !Directory.Exists(Path.Combine(CloudData, "save")))
            {
                string tmp = Path.Combine(S.CloudDir, "data_old");
                Directory.Move(CloudData, tmp);
                Directory.CreateDirectory(CloudData);
                Directory.Move(tmp, Path.Combine(CloudData, "save"));
                Log("Moved old cloud layout to data\\save.", Level.Dim);
            }
        }

        bool Available(SyncSet set, bool say)
        {
            if (set.Optional && !Directory.Exists(Path.GetDirectoryName(set.Dir)))
            {
                if (say) Log("  " + set.Label + ": " + Path.GetDirectoryName(set.Dir) + " not found, skipped. Set the game config folder above.", Level.Warn);
                return false;
            }
            return true;
        }

        void AssertNotRunning(string what)
        {
            if (GameRunning()) throw new Exception("The game is running. Close it before you " + what + ".");
        }

        // ---------- Status ----------

        public void Status()
        {
            PrepareCloud();
            Log("Sync folder: " + S.CloudDir, Level.Info);
            Log("  " + CloudInfo(), Level.Dim);
            var state = LoadState();
            foreach (var set in Sets)
            {
                if (!Available(set, true)) continue;
                string local = TreeHash(set.Dir, set.Exclude), cloud = TreeHash(set.Cloud, set.Exclude), last;
                state.TryGetValue(set.Name, out last);
                string text; Level lv = Level.Warn;
                if (local == cloud) { text = "in sync"; lv = Level.Good; }
                else if (IsEmpty(cloud)) text = "cloud empty, upload";
                else if (cloud == last) text = "local is newer, upload";
                else if (local == last || IsEmpty(local)) text = "cloud is newer, download";
                else text = "both changed or never synced";
                Log("  " + set.Label + ": " + text + " (local " + Newest(set.Dir, set.Exclude) + ", cloud " + Newest(set.Cloud, set.Exclude) + ")", lv);
            }
        }

        // ---------- Download ----------

        // force = the user explicitly wants the cloud state: one confirmation for everything, no conflict questions
        public bool Pull(bool force)
        {
            Log("Downloading from the cloud ...", Level.Info);
            PrepareCloud();
            AssertNotRunning("download");
            var state = LoadState();
            var take = new List<SyncSet>();
            var conflicts = new List<SyncSet>();
            var cloudHash = new Dictionary<string, string>();
            foreach (var set in Sets)
            {
                if (!Available(set, true)) continue;
                string local = TreeHash(set.Dir, set.Exclude), cloud = TreeHash(set.Cloud, set.Exclude), last;
                state.TryGetValue(set.Name, out last);
                cloudHash[set.Name] = cloud;
                if (IsEmpty(cloud)) Log("  " + set.Label + ": cloud is empty, nothing to download.", Level.Warn);
                else if (local == cloud) { SetLastHash(set.Name, local); Log("  " + set.Label + ": up to date.", Level.Good); }
                else if (force || IsEmpty(local) || local == last) take.Add(set);
                else if (cloud == last) Log("  " + set.Label + ": local is newer, kept.", Level.Good);
                else conflicts.Add(set);
            }
            if (force && take.Count > 0)
            {
                string names = string.Join(", ", take.Select(t => t.Label));
                if (!Confirm("Replace your local data with the cloud state?\n\n" + names + "\n\nA backup is made first."))
                {
                    Log("  Cancelled, nothing changed.", Level.Warn);
                    return false;
                }
            }
            if (conflicts.Count > 0)
            {
                switch (AskConflict(conflicts, true))
                {
                    case Choice.Cloud: take.AddRange(conflicts); break;
                    case Choice.Local:
                        foreach (var c in conflicts)
                        {
                            SetLastHash(c.Name, cloudHash[c.Name]);
                            Log("  " + c.Label + ": local state kept, it will replace the cloud on the next upload.", Level.Good);
                        }
                        break;
                    default:
                        Log("  Cancelled, nothing changed.", Level.Warn);
                        return false;
                }
            }
            foreach (var set in take)
            {
                Backup(set.Dir, set.Exclude, Path.Combine(S.StateDir, "backups"), set.Name, KeepLocalBackups);
                Mirror(set.Cloud, set.Dir, set.Exclude);
                SetLastHash(set.Name, cloudHash[set.Name]);
                Log("  " + set.Label + ": downloaded from the cloud.", Level.Good);
            }
            if (take.Count > 0) Log("  Cloud state: " + CloudInfo(), Level.Dim);
            return true;
        }

        // ---------- Upload ----------

        public bool Push(bool force)
        {
            Log("Uploading to the cloud ...", Level.Info);
            PrepareCloud();
            AssertNotRunning("upload");
            var state = LoadState();
            var upload = new List<SyncSet>();
            var conflicts = new List<SyncSet>();
            var localHash = new Dictionary<string, string>();
            foreach (var set in Sets)
            {
                if (!Available(set, true)) continue;
                string local = TreeHash(set.Dir, set.Exclude), cloud = TreeHash(set.Cloud, set.Exclude), last;
                state.TryGetValue(set.Name, out last);
                localHash[set.Name] = local;
                if (IsEmpty(local)) Log("  " + set.Label + ": nothing local, nothing uploaded.", Level.Warn);
                else if (local == cloud) { SetLastHash(set.Name, local); Log("  " + set.Label + ": cloud is up to date.", Level.Good); }
                else if (force || IsEmpty(cloud) || cloud == last) upload.Add(set);
                else conflicts.Add(set);
            }
            if (force && upload.Count > 0)
            {
                string names = string.Join(", ", upload.Select(t => t.Label));
                if (!Confirm("Replace the cloud state with your local data?\n\n" + names + "\n\nA backup is made first."))
                {
                    Log("  Cancelled, nothing changed.", Level.Warn);
                    return false;
                }
            }
            if (conflicts.Count > 0)
            {
                switch (AskConflict(conflicts, false))
                {
                    case Choice.Local: upload.AddRange(conflicts); break;
                    case Choice.Cloud:
                        foreach (var c in conflicts) Log("  " + c.Label + ": cloud kept, use \"Download\" to get it.", Level.Warn);
                        break;
                    default:
                        Log("  Cancelled, nothing changed.", Level.Warn);
                        return false;
                }
            }
            foreach (var set in upload)
            {
                Backup(set.Cloud, set.Exclude, CloudHistory, set.Name, KeepCloudBackups);
                Mirror(set.Dir, set.Cloud, set.Exclude);
                SetLastHash(set.Name, localHash[set.Name]);
                Log("  " + set.Label + ": uploaded.", Level.Good);
            }
            if (upload.Count > 0)
            {
                var m = new Dictionary<string, object> { { "Device", Environment.MachineName }, { "Time", DateTime.Now.ToString("o") } };
                File.WriteAllText(Manifest, new JavaScriptSerializer().Serialize(m), new UTF8Encoding(false));
            }
            return true;
        }

        // ---------- Play ----------

        public void Play()
        {
            if (!File.Exists(S.GamePath)) throw new Exception("Game not found: " + S.GamePath);
            if (GameRunning()) throw new Exception("The game is already running.");
            if (!Pull(false) && !Confirm("The sync was cancelled.\nPlay with your local state anyway?")) return;
            Log("Starting Azeroth Survivors ...", Level.Info);
            var p = Process.Start(new ProcessStartInfo(S.GamePath) { WorkingDirectory = Path.GetDirectoryName(S.GamePath), UseShellExecute = false });
            p.WaitForExit();
            while (GameRunning()) Thread.Sleep(2000);
            Log("Game closed.", Level.Info);
            Thread.Sleep(3000);
            Push(false);
        }
    }
}
