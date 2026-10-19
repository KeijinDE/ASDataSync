// AzerothSync core: syncs save game, addons and addon settings of Azeroth Survivors through a cloud folder.
// No merging: per area one complete state wins, the overwritten one is backed up first.
// Safety rules: paths are validated, the cloud state must match the last upload's manifest,
// copies go to a staging folder, are verified and only then swapped in.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
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
        public const string GameExe = "AzerothSurvivors.exe";
        public const string DefaultGamePath = @"C:\Games\AzerothSurvivors\" + GameExe;
        public const string FallbackConfigDir = @"C:\Git\wenilla\benilla-config";

        public string GamePath, CloudDir, SaveDir, ConfigDir, StateDir;

        public static string DefaultStateDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzerothSync");
        }

        public static string DefaultSaveDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "wenilla-rogue");
        }

        // AddOns and saved (SavedVariables) live in benilla-config: next to the game if it exists there, otherwise the fallback
        public static string DefaultConfigDir(string gamePath)
        {
            try
            {
                string next = Path.Combine(Path.GetDirectoryName(gamePath), "benilla-config");
                if (Directory.Exists(next)) return next;
            }
            catch { }
            return FallbackConfigDir;
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
            // If this program sits in the game folder, that game always wins
            if (exeDir != null && File.Exists(Path.Combine(exeDir, GameExe))) s.GamePath = Path.Combine(exeDir, GameExe);
            if (s.CloudDir.Length == 0) s.CloudDir = DefaultCloudDir(exeDir);
            // Empty or no longer existing: use what is actually there (game folder, then fallback)
            if (s.ConfigDir.Length == 0 || !Directory.Exists(s.ConfigDir))
            {
                string found = DefaultConfigDir(s.GamePath);
                if (s.ConfigDir.Length == 0 || Directory.Exists(found)) s.ConfigDir = found;
            }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(StateDir);
            Engine.WriteAtomic(Path.Combine(StateDir, "settings.ini"), string.Join("\r\n", new[] {
                "GamePath=" + GamePath, "CloudDir=" + CloudDir, "SaveDir=" + SaveDir, "ConfigDir=" + ConfigDir }) + "\r\n");
        }

        // Every run works on its own copy, so changes in the window cannot affect a running sync
        public Settings Clone()
        {
            return (Settings)MemberwiseClone();
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
        public long Length;
    }

    // Read only stream wrapper that reports every chunk read, for progress
    class ProgressStream : Stream
    {
        readonly Stream inner;
        readonly Action<long> onRead;
        public ProgressStream(Stream inner, Action<long> onRead) { this.inner = inner; this.onRead = onRead; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            if (n > 0 && onRead != null) onRead(n);
            return n;
        }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return inner.Length; } }
        public override long Position { get { return inner.Position; } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    // Junctions and symbolic links are refused; cloud placeholders (OneDrive and others) are fine
    static class Links
    {
        const uint TagMountPoint = 0xA0000003, TagSymlink = 0xA000000C;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct FindData
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            public uint SizeHigh, SizeLow, Reserved0, Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AltName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileW(string name, out FindData data);

        [DllImport("kernel32.dll")]
        static extern bool FindClose(IntPtr handle);

        public static bool IsLink(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
            }
            catch { return false; }
            FindData d;
            IntPtr h = FindFirstFileW(path.TrimEnd('\\'), out d);
            if (h == new IntPtr(-1)) return false;
            FindClose(h);
            return d.Reserved0 == TagMountPoint || d.Reserved0 == TagSymlink;
        }
    }

    public class Engine
    {
        const int KeepLocalBackups = 20, KeepCloudBackups = 10;
        const string None = "NONE", Empty = "EMPTY";
        const string StageSuffix = ".azsync-new", OldSuffix = ".azsync-old";
        // Hashes in state.json carry this prefix; values without it are the old culture dependent format
        const string HashPrefix = "2:";

        public Settings S;
        public List<SyncSet> Sets;
        public Action<string, Level> Log = delegate { };
        // One question for all areas in conflict
        public Func<List<SyncSet>, bool, Choice> AskConflict = delegate { return Choice.Abort; };
        public Func<string, bool> Confirm = delegate { return false; };
        // Progress text and percent; percent -1 hides the progress display
        public Action<string, int> Progress = delegate { };
        // Called right before an area is written (used by the tests to simulate changes in between)
        public Action<SyncSet> BeforeApply = delegate { };

        string pLabel;
        long pDone, pTotal;
        int pFiles, pFilesTotal, pLastPct;
        DateTime pLastReport;

        string CloudData { get { return Path.Combine(S.CloudDir, "data"); } }
        string CloudHistory { get { return Path.Combine(S.CloudDir, "history"); } }
        string Manifest { get { return Path.Combine(S.CloudDir, "manifest.json"); } }
        string StateFile { get { return Path.Combine(S.StateDir, "state.json"); } }

        public Engine(Settings s)
        {
            S = s;
            Sets = new List<SyncSet> {
                new SyncSet { Name = "save", Label = "Save game", Dir = s.SaveDir, Exclude = new[] { "language.txt" } },
                new SyncSet { Name = "addons", Label = "AddOns", Dir = Path.Combine(s.ConfigDir ?? "", "AddOns"), Exclude = new string[0], Optional = true },
                new SyncSet { Name = "saved", Label = "Addon settings", Dir = Path.Combine(s.ConfigDir ?? "", "saved"), Exclude = new string[0], Optional = true },
            };
            foreach (var set in Sets) set.Cloud = Path.Combine(CloudData, set.Name);
        }

        // ---------- Path validation ----------

        static string Canon(string p)
        {
            return Path.GetFullPath(p).TrimEnd('\\');
        }

        static bool Inside(string a, string b)
        {
            return a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase);
        }

        static bool Overlap(string a, string b) { return Inside(a, b) || Inside(b, a); }

        static string[] BroadFolders()
        {
            var list = new List<string>();
            foreach (var f in new[] {
                Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData })
            {
                string p = Environment.GetFolderPath(f);
                if (!string.IsNullOrEmpty(p)) list.Add(Canon(p));
            }
            return list.ToArray();
        }

        // Throws before anything is touched if the folders could destroy each other or unrelated data
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(S.CloudDir))
                throw new Exception("No sync folder set. Choose a folder in Google Drive, OneDrive, Dropbox or similar above.");
            if (string.IsNullOrWhiteSpace(S.SaveDir)) throw new Exception("No save game folder set.");
            if (string.IsNullOrWhiteSpace(S.ConfigDir)) throw new Exception("No game config folder set.");
            string cloud, state;
            var local = new List<KeyValuePair<string, string>>();
            try
            {
                cloud = Canon(S.CloudDir);
                state = Canon(S.StateDir);
                foreach (var set in Sets) local.Add(new KeyValuePair<string, string>(set.Label, Canon(set.Dir)));
            }
            catch (Exception ex) { throw new Exception("A folder path is invalid: " + ex.Message); }

            if (Path.GetPathRoot(cloud).TrimEnd('\\').Equals(cloud, StringComparison.OrdinalIgnoreCase))
                throw new Exception("The sync folder must not be a drive root (" + cloud + "). Use a folder inside it.");
            var broad = BroadFolders();
            foreach (var l in local)
            {
                if (Path.GetPathRoot(l.Value).TrimEnd('\\').Equals(l.Value, StringComparison.OrdinalIgnoreCase))
                    throw new Exception(l.Key + " folder must not be a drive root: " + l.Value);
                if (broad.Any(b => b.Equals(l.Value, StringComparison.OrdinalIgnoreCase)))
                    throw new Exception(l.Key + " folder is a general Windows folder, choose the game's own folder: " + l.Value);
                if (Overlap(l.Value, cloud))
                    throw new Exception(l.Key + " folder and sync folder overlap (" + l.Value + " / " + cloud + "). They must be separate.");
                if (Overlap(l.Value, state))
                    throw new Exception(l.Key + " folder overlaps the AzerothSync data folder " + state + ".");
            }
            for (int i = 0; i < local.Count; i++)
                for (int j = i + 1; j < local.Count; j++)
                    if (Overlap(local[i].Value, local[j].Value))
                        throw new Exception(local[i].Key + " and " + local[j].Key + " folders overlap (" + local[i].Value + " / " + local[j].Value + ").");
            if (Overlap(cloud, state))
                throw new Exception("The sync folder overlaps the AzerothSync data folder " + state + ".");
            foreach (var p in local.Select(l => l.Value).Concat(new[] { cloud }))
                if (Directory.Exists(p) && Links.IsLink(p))
                    throw new Exception("Linked folders (junction or symbolic link) are not supported: " + p);
        }

        // ---------- Files ----------

        static List<FileEntry> Files(string dir, string[] exclude)
        {
            var list = new List<FileEntry>();
            if (!Directory.Exists(dir)) return list;
            string root = Path.GetFullPath(dir).TrimEnd('\\');
            Walk(root, root, exclude, list);
            return list;
        }

        static void Walk(string root, string dir, string[] exclude, List<FileEntry> list)
        {
            foreach (string f in Directory.GetFiles(dir))
            {
                string rel = f.Substring(root.Length + 1);
                if (!exclude.Contains(rel, StringComparer.OrdinalIgnoreCase)) list.Add(new FileEntry { Rel = rel, Full = f, Length = new FileInfo(f).Length });
            }
            foreach (string d in Directory.GetDirectories(dir))
            {
                if (Links.IsLink(d)) throw new Exception("Linked folders (junction or symbolic link) inside synced folders are not supported: " + d);
                Walk(root, d, exclude, list);
            }
        }

        static string Hex(byte[] b)
        {
            return BitConverter.ToString(b).Replace("-", "");
        }

        // Writes a text file through a temporary file, so a crash never leaves a half written file
        public static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, null); }
            catch (Exception)
            {
                // Some cloud drive file systems do not support ReplaceFile
                if (!File.Exists(tmp)) throw;
                File.Delete(path);
                File.Move(tmp, path);
            }
        }

        static void DeleteTree(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(path, true);
        }

        // ---------- Progress ----------

        void BeginProgress(string label, IEnumerable<FileEntry> files)
        {
            var list = files.ToList();
            pLabel = label;
            pTotal = list.Sum(f => f.Length);
            pFilesTotal = list.Count;
            pDone = 0; pFiles = 0; pLastPct = -1;
            pLastReport = DateTime.MinValue;
            ReportProgress(true);
        }

        public void EndProgress() { Progress("", -1); }

        void AddBytes(long n) { pDone += n; ReportProgress(false); }

        void FileDone() { pFiles++; ReportProgress(false); }

        static string Size(long b)
        {
            if (b < 1048576) return Math.Ceiling(b / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return (b / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        void ReportProgress(bool force)
        {
            int pct = pTotal > 0 ? (int)Math.Min(100, pDone * 100 / pTotal) : (pFilesTotal > 0 ? pFiles * 100 / pFilesTotal : 100);
            // Throttle: a new percent at most every 50 ms, the end always
            bool last = pFiles >= pFilesTotal;
            if (!force && !last && (pct == pLastPct || (DateTime.Now - pLastReport).TotalMilliseconds < 50)) return;
            pLastPct = pct;
            pLastReport = DateTime.Now;
            Progress(pLabel + ": " + Math.Min(pFiles, pFilesTotal) + " of " + pFilesTotal + " files, " + Size(Math.Min(pDone, pTotal)) + " of " + Size(pTotal), pct);
        }

        Stream OpenRead(string path, bool report)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            return new ProgressStream(fs, report ? (Action<long>)AddBytes : null);
        }

        // Files of all areas on both sides, for the checking step
        IEnumerable<FileEntry> AllFiles(IEnumerable<SyncSet> sets)
        {
            return sets.SelectMany(s => Files(s.Dir, s.Exclude).Concat(Files(s.Cloud, s.Exclude)));
        }

        // ---------- Fingerprints ----------

        // Format 2: lines "path:SHA256" with invariant lower case, sorted ordinal, joined with \n, UTF8.
        // legacy returns format 1 (culture lower case and sort) to recognize old state.json values.
        public string TreeHash(string dir, string[] exclude, out string legacy, bool report = true)
        {
            legacy = None;
            if (!Directory.Exists(dir)) return None;
            var files = Files(dir, exclude);
            legacy = Empty;
            if (files.Count == 0) return Empty;
            var hashes = new List<KeyValuePair<string, string>>();
            using (var sha = SHA256.Create())
            {
                foreach (var f in files)
                {
                    using (var st = OpenRead(f.Full, report))
                        hashes.Add(new KeyValuePair<string, string>(f.Rel, Hex(sha.ComputeHash(st))));
                    if (report) FileDone();
                }
                var v2 = hashes.Select(h => h.Key.ToLowerInvariant() + ":" + h.Value).ToList();
                v2.Sort(StringComparer.Ordinal);
                var v1 = hashes.Select(h => h.Key.ToLower() + ":" + h.Value).ToList();
                v1.Sort(StringComparer.Create(CultureInfo.CurrentCulture, true));
                legacy = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", v1))));
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", v2))));
            }
        }

        string TreeHash(string dir, string[] exclude)
        {
            string legacy;
            return TreeHash(dir, exclude, out legacy, false);
        }

        static bool IsEmpty(string h) { return h == None || h == Empty; }

        public static string Newest(string dir, string[] exclude)
        {
            try
            {
                var files = Files(dir, exclude);
                if (files.Count == 0) return "-";
                return files.Max(f => File.GetLastWriteTime(f.Full)).ToString("yyyy-MM-dd HH:mm");
            }
            catch { return "-"; }
        }

        // ---------- Copy with staging ----------

        // Restores or cleans up after an interrupted swap
        void Recover(string dir)
        {
            string stage = dir + StageSuffix, old = dir + OldSuffix;
            if (Directory.Exists(old))
            {
                if (!Directory.Exists(dir))
                {
                    Directory.Move(old, dir);
                    Log("  Restored " + dir + " after an interrupted sync.", Level.Warn);
                }
                else DeleteTree(old);
            }
            if (Directory.Exists(stage)) DeleteTree(stage);
        }

        void RecoverAll()
        {
            foreach (var set in Sets)
                foreach (var d in new[] { set.Dir, set.Cloud })
                {
                    string parent = Path.GetDirectoryName(Path.GetFullPath(d));
                    if (parent != null && Directory.Exists(parent)) Recover(Path.GetFullPath(d));
                }
        }

        // Makes dst an exact copy of src. Copies into a staging folder, verifies it against expectedHash
        // and only then swaps it in. Excluded files of dst are kept. On any error dst stays unchanged.
        void Mirror(string src, string dst, string[] exclude, string expectedHash)
        {
            dst = Path.GetFullPath(dst).TrimEnd('\\');
            string stage = dst + StageSuffix, old = dst + OldSuffix;
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            DeleteTree(stage);
            Directory.CreateDirectory(stage);
            try
            {
                foreach (var f in Files(src, exclude))
                {
                    string target = Path.Combine(stage, f.Rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = OpenRead(f.Full, true))
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                        input.CopyTo(output, 1 << 16);
                    File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(f.Full));
                    FileDone();
                }
                if (TreeHash(stage, exclude) != expectedHash)
                    throw new Exception("The source changed while copying (" + src + "). Nothing was replaced, please sync again.");
                // Keep local only files such as language.txt
                foreach (string ex in exclude)
                {
                    string keep = Path.Combine(dst, ex);
                    if (File.Exists(keep)) File.Copy(keep, Path.Combine(stage, ex), true);
                }
            }
            catch
            {
                DeleteTree(stage);
                throw;
            }
            bool hadDst = Directory.Exists(dst);
            try
            {
                if (hadDst) Directory.Move(dst, old);
            }
            catch (IOException ex)
            {
                DeleteTree(stage);
                throw new Exception("The folder is in use and was not replaced: " + dst + " (" + ex.Message + "). Close programs or Explorer windows that use it and try again.");
            }
            try
            {
                Directory.Move(stage, dst);
            }
            catch
            {
                if (hadDst && !Directory.Exists(dst)) Directory.Move(old, dst);
                DeleteTree(stage);
                throw;
            }
            try { DeleteTree(old); }
            catch (Exception ex) { Log("    Could not remove the old copy " + old + " (" + ex.Message + "), it is cleaned up next time.", Level.Dim); }
        }

        void Backup(string src, string[] exclude, string backupDir, string prefix, int keep)
        {
            var files = Files(src, exclude);
            if (files.Count == 0) return;
            Directory.CreateDirectory(backupDir);
            string zip = Path.Combine(backupDir, prefix + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_" + Environment.MachineName + ".zip");
            for (int n = 2; File.Exists(zip); n++)
                zip = Path.Combine(backupDir, prefix + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_" + Environment.MachineName + "_" + n + ".zip");
            string tmp = zip + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
            using (var a = new ZipArchive(fs, ZipArchiveMode.Create))
                foreach (var f in files)
                {
                    var entry = a.CreateEntry(f.Rel.Replace('\\', '/'));
                    entry.LastWriteTime = File.GetLastWriteTime(f.Full);
                    using (var input = OpenRead(f.Full, true))
                    using (var output = entry.Open())
                        input.CopyTo(output, 1 << 16);
                    FileDone();
                }
            File.Move(tmp, zip);
            // Only complete archives count; the newest ones stay
            foreach (string old in Directory.GetFiles(backupDir, prefix + "_*.zip").OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal).Skip(keep))
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
            catch (Exception ex)
            {
                string copy = StateFile + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(StateFile, copy, true); } catch { }
                Log("  The sync memory could not be read (" + ex.Message + "). A copy was kept as " + copy + ". Every area is treated as never synced.", Level.Warn);
            }
            return h;
        }

        // Last known fingerprint in format 2. Old values are mapped through the format 1 fingerprints of both sides.
        static string LastHash(Dictionary<string, string> state, string name, string local, string localV1, string cloud, string cloudV1)
        {
            string v;
            if (!state.TryGetValue(name, out v) || v == null) return null;
            if (v.StartsWith(HashPrefix)) return v.Substring(HashPrefix.Length);
            if (v == localV1) return local;
            if (v == cloudV1) return cloud;
            return v;
        }

        void SetLastHash(string name, string hash)
        {
            var h = LoadState();
            h[name] = HashPrefix + hash;
            Directory.CreateDirectory(S.StateDir);
            var o = new Dictionary<string, object> {
                { "Hashes", h }, { "LastSync", DateTime.Now.ToString("o") }, { "Device", Environment.MachineName } };
            WriteAtomic(StateFile, new JavaScriptSerializer().Serialize(o));
        }

        // ---------- Manifest ----------

        Dictionary<string, object> LoadManifest()
        {
            try
            {
                if (File.Exists(Manifest))
                    return (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Manifest));
            }
            catch { }
            return new Dictionary<string, object>();
        }

        // Fingerprint the last upload published for an area, or null for old manifests without it
        static string ExpectedHash(Dictionary<string, object> manifest, string name)
        {
            object areas, area, hash;
            if (manifest.TryGetValue("Areas", out areas) && areas is Dictionary<string, object>
                && ((Dictionary<string, object>)areas).TryGetValue(name, out area) && area is Dictionary<string, object>
                && ((Dictionary<string, object>)area).TryGetValue("Hash", out hash) && hash != null)
                return hash.ToString();
            return null;
        }

        // uploaded = false only records fingerprints of areas already in sync and keeps device and time
        void WriteManifest(Dictionary<string, string> published, bool uploaded)
        {
            var old = LoadManifest();
            var areas = new Dictionary<string, object>();
            object a;
            if (old.TryGetValue("Areas", out a) && a is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)a) areas[kv.Key] = kv.Value;
            foreach (var kv in published)
            {
                var files = Files(Path.Combine(CloudData, kv.Key), Sets.First(s => s.Name == kv.Key).Exclude);
                areas[kv.Key] = new Dictionary<string, object> { { "Hash", kv.Value }, { "Files", files.Count }, { "Bytes", files.Sum(f => f.Length) } };
            }
            object device = Environment.MachineName, time = DateTime.Now.ToString("o");
            if (!uploaded && old.ContainsKey("Device") && old.ContainsKey("Time")) { device = old["Device"]; time = old["Time"]; }
            var m = new Dictionary<string, object> {
                { "Format", 2 }, { "Device", device }, { "Time", time }, { "Areas", areas } };
            WriteAtomic(Manifest, new JavaScriptSerializer().Serialize(m));
        }

        public string CloudInfo()
        {
            try
            {
                var m = LoadManifest();
                if (m.ContainsKey("Time") && m.ContainsKey("Device"))
                {
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

        void AssertNotRunning(string what)
        {
            if (GameRunning()) throw new Exception("The game is running. Close it before you " + what + ".");
        }

        bool OldLayout()
        {
            bool oldSave = Directory.Exists(Path.Combine(CloudData, "Saves")) || File.Exists(Path.Combine(CloudData, "characters.txt"));
            return oldSave && !Directory.Exists(Path.Combine(CloudData, "save"));
        }

        // Very old layout: the save game was directly in data\, now it is data\save\
        void Migrate()
        {
            if (!OldLayout()) return;
            string tmp = Path.Combine(S.CloudDir, "data_old");
            Directory.Move(CloudData, tmp);
            Directory.CreateDirectory(CloudData);
            Directory.Move(tmp, Path.Combine(CloudData, "save"));
            Log("Moved old cloud layout to data\\save.", Level.Dim);
        }

        void AssertCloudParent()
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(S.CloudDir).TrimEnd('\\'));
            if (parent == null || !Directory.Exists(parent))
                throw new Exception("Sync folder not reachable: " + S.CloudDir + " (is your cloud drive running?)");
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

        class Eval
        {
            public SyncSet Set;
            public string Local, Cloud, Last, Expected;
        }

        Eval Evaluate(SyncSet set, Dictionary<string, string> state, Dictionary<string, object> manifest)
        {
            string l1, c1;
            var e = new Eval { Set = set };
            e.Local = TreeHash(set.Dir, set.Exclude, out l1);
            e.Cloud = TreeHash(set.Cloud, set.Exclude, out c1);
            e.Last = LastHash(state, set.Name, e.Local, l1, e.Cloud, c1);
            e.Expected = ExpectedHash(manifest, set.Name);
            return e;
        }

        // ---------- Status (read only) ----------

        public bool Status()
        {
            Validate();
            Log("Sync folder: " + S.CloudDir, Level.Info);
            if (!Directory.Exists(S.CloudDir))
            {
                Log("  The sync folder does not exist yet. It is created on the first upload.", Level.Warn);
                return true;
            }
            Log("  " + CloudInfo(), Level.Dim);
            if (OldLayout()) Log("  Old cloud layout found, it is converted on the next download or upload.", Level.Warn);
            var state = LoadState();
            var manifest = LoadManifest();
            var sets = Sets.Where(s => Available(s, true)).ToList();
            BeginProgress("Checking files", AllFiles(sets));
            foreach (var set in sets)
            {
                var e = Evaluate(set, state, manifest);
                string text; Level lv = Level.Warn;
                if (e.Local == e.Cloud) { text = "in sync"; lv = Level.Good; }
                else if (IsEmpty(e.Cloud)) text = "cloud empty, upload";
                else if (e.Expected != null && e.Cloud != e.Expected) text = "cloud copy incomplete or still syncing, wait";
                else if (e.Cloud == e.Last) text = "local is newer, upload";
                else if (e.Local == e.Last || IsEmpty(e.Local)) text = "cloud is newer, download";
                else text = "both changed or never synced";
                Log("  " + set.Label + ": " + text + " (local " + Newest(set.Dir, set.Exclude) + ", cloud " + Newest(set.Cloud, set.Exclude) + ")", lv);
            }
            EndProgress();
            return true;
        }

        // ---------- Download ----------

        // force = the user explicitly wants the cloud state: one confirmation for everything, no conflict questions.
        // Returns false if anything was cancelled or could not be downloaded.
        public bool Pull(bool force)
        {
            Log("Downloading from the cloud ...", Level.Info);
            Validate();
            AssertNotRunning("download");
            AssertCloudParent();
            if (!Directory.Exists(S.CloudDir))
            {
                Log("  The sync folder does not exist yet, nothing to download.", Level.Warn);
                return true;
            }
            Migrate();
            RecoverAll();
            var state = LoadState();
            var manifest = LoadManifest();
            var take = new List<Eval>();
            var conflicts = new List<Eval>();
            bool complete = true;
            var sets = Sets.Where(s => Available(s, true)).ToList();
            BeginProgress("Checking files", AllFiles(sets));
            foreach (var set in sets)
            {
                var e = Evaluate(set, state, manifest);
                if (IsEmpty(e.Cloud)) Log("  " + set.Label + ": cloud is empty, nothing to download.", Level.Warn);
                else if (e.Local == e.Cloud) { SetLastHash(set.Name, e.Local); Log("  " + set.Label + ": up to date.", Level.Good); }
                else if (e.Expected != null && e.Cloud != e.Expected && !force)
                {
                    Log("  " + set.Label + ": the cloud copy does not match the last upload yet (incomplete or still syncing). Nothing replaced, try again in a moment.", Level.Warn);
                    complete = false;
                }
                else if (force || IsEmpty(e.Local) || e.Local == e.Last) take.Add(e);
                else if (e.Cloud == e.Last) Log("  " + set.Label + ": local is newer, kept.", Level.Good);
                else conflicts.Add(e);
            }
            EndProgress();
            if (force && take.Count > 0)
            {
                // A forced download may also take a cloud state that was changed by hand (for example a restored backup)
                string names = string.Join(", ", take.Select(t => t.Set.Label + (t.Expected != null && t.Cloud != t.Expected ? " (does not match the last upload, maybe still syncing)" : "")));
                if (!Confirm("Replace your local data with the cloud state?\n\n" + names + "\n\nA backup is made first."))
                {
                    Log("  Cancelled, nothing changed.", Level.Warn);
                    return false;
                }
            }
            if (conflicts.Count > 0)
            {
                switch (AskConflict(conflicts.Select(c => c.Set).ToList(), true))
                {
                    case Choice.Cloud: take.AddRange(conflicts); break;
                    case Choice.Local:
                        foreach (var c in conflicts)
                        {
                            SetLastHash(c.Set.Name, c.Cloud);
                            Log("  " + c.Set.Label + ": local state kept, it will replace the cloud on the next upload.", Level.Good);
                        }
                        break;
                    default:
                        Log("  Cancelled, nothing changed.", Level.Warn);
                        return false;
                }
            }
            if (take.Count == 0) return complete;
            AssertNotRunning("download");
            // Backup of the local side plus copy of the cloud side
            BeginProgress("Downloading", AllFiles(take.Select(t => t.Set)));
            foreach (var e in take)
            {
                BeforeApply(e.Set);
                Backup(e.Set.Dir, e.Set.Exclude, Path.Combine(S.StateDir, "backups"), e.Set.Name, KeepLocalBackups);
                Mirror(e.Set.Cloud, e.Set.Dir, e.Set.Exclude, e.Cloud);
                SetLastHash(e.Set.Name, e.Cloud);
                Log("  " + e.Set.Label + ": downloaded from the cloud.", Level.Good);
            }
            EndProgress();
            Log("  Cloud state: " + CloudInfo(), Level.Dim);
            return complete;
        }

        // ---------- Upload ----------

        // Returns false if anything was cancelled or local changes were not uploaded.
        public bool Push(bool force)
        {
            Log("Uploading to the cloud ...", Level.Info);
            Validate();
            AssertNotRunning("upload");
            AssertCloudParent();
            Directory.CreateDirectory(S.CloudDir);
            Migrate();
            RecoverAll();
            var state = LoadState();
            var manifest = LoadManifest();
            var upload = new List<Eval>();
            var conflicts = new List<Eval>();
            var published = new Dictionary<string, string>();
            bool complete = true;
            var sets = Sets.Where(s => Available(s, true)).ToList();
            BeginProgress("Checking files", AllFiles(sets));
            foreach (var set in sets)
            {
                var e = Evaluate(set, state, manifest);
                if (IsEmpty(e.Local)) Log("  " + set.Label + ": nothing local, nothing uploaded.", Level.Warn);
                else if (e.Local == e.Cloud)
                {
                    SetLastHash(set.Name, e.Local);
                    if (e.Expected != e.Cloud) published[set.Name] = e.Cloud;
                    Log("  " + set.Label + ": cloud is up to date.", Level.Good);
                }
                else if (force || IsEmpty(e.Cloud) || e.Cloud == e.Last) upload.Add(e);
                else conflicts.Add(e);
            }
            EndProgress();
            if (force && upload.Count > 0)
            {
                string names = string.Join(", ", upload.Select(t => t.Set.Label));
                if (!Confirm("Replace the cloud state with your local data?\n\n" + names + "\n\nA backup is made first."))
                {
                    Log("  Cancelled, nothing changed.", Level.Warn);
                    return false;
                }
            }
            if (conflicts.Count > 0)
            {
                switch (AskConflict(conflicts.Select(c => c.Set).ToList(), false))
                {
                    case Choice.Local: upload.AddRange(conflicts); break;
                    case Choice.Cloud:
                        foreach (var c in conflicts) Log("  " + c.Set.Label + ": cloud kept, your local changes were NOT uploaded. Use \"Download\" to get the cloud state.", Level.Warn);
                        complete = false;
                        break;
                    default:
                        Log("  Cancelled, your local changes were NOT uploaded.", Level.Warn);
                        return false;
                }
            }
            if (upload.Count > 0)
            {
                AssertNotRunning("upload");
                // Backup of the cloud side plus copy of the local side
                BeginProgress("Uploading", AllFiles(upload.Select(t => t.Set)));
                foreach (var e in upload)
                {
                    BeforeApply(e.Set);
                    // Another PC may have uploaded since the check: never overwrite that without asking
                    if (TreeHash(e.Set.Cloud, e.Set.Exclude) != e.Cloud)
                    {
                        Log("  " + e.Set.Label + ": the cloud changed during this sync, NOT uploaded. Sync again to decide.", Level.Warn);
                        complete = false;
                        continue;
                    }
                    Backup(e.Set.Cloud, e.Set.Exclude, CloudHistory, e.Set.Name, KeepCloudBackups);
                    Mirror(e.Set.Dir, e.Set.Cloud, e.Set.Exclude, e.Local);
                    SetLastHash(e.Set.Name, e.Local);
                    published[e.Set.Name] = e.Local;
                    Log("  " + e.Set.Label + ": uploaded.", Level.Good);
                }
                EndProgress();
            }
            if (published.Count > 0)
            {
                bool uploaded = upload.Any(u => published.ContainsKey(u.Set.Name));
                WriteManifest(published, uploaded);
                if (uploaded) Log("  Written to the sync folder. Your cloud app uploads it in the background, wait until it is done before you switch PCs.", Level.Dim);
            }
            return complete;
        }

        // ---------- Play ----------

        // Returns true only if the progress after playing was uploaded completely
        public bool Play()
        {
            if (!File.Exists(S.GamePath)) throw new Exception("Game not found: " + S.GamePath);
            if (GameRunning()) throw new Exception("The game is already running.");
            if (!Pull(false) && !Confirm("The download was not complete.\nPlay with your local state anyway?")) return false;
            EndProgress();
            Log("Starting Azeroth Survivors ...", Level.Info);
            var p = Process.Start(new ProcessStartInfo(S.GamePath) { WorkingDirectory = Path.GetDirectoryName(S.GamePath), UseShellExecute = false });
            p.WaitForExit();
            while (GameRunning()) Thread.Sleep(2000);
            Log("Game closed.", Level.Info);
            Thread.Sleep(3000);
            return Push(false);
        }
    }
}
