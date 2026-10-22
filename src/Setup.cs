// AzerothSync setup logic without any UI: finds cloud drive folders, inspects and checks the four paths
// and advises what the first sync should do. The setup wizard and the paths page only display these results.
// Nothing here changes any file. All functions are tolerant: IO and permission errors never escape.
// Engine.Validate stays the safety net: the UI still relies on it when syncing.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace AzerothSync
{
    // A folder of a cloud drive client (Google Drive for desktop, OneDrive, Dropbox) found on this PC
    public class CloudCandidate
    {
        public string Name;         // "Google Drive", "OneDrive" or "Dropbox"
        public string Root;         // the drive's own folder, e.g. G:\My Drive
        public string SuggestedDir; // Root\AzerothSync
    }

    public enum CloudContent { NotSet, Unreachable, Missing, Empty, HasData }

    public class CloudInfoResult
    {
        public CloudContent Content;
        public string Device;           // from manifest.json, may be null
        public DateTime? Time;          // local time of the last upload, from manifest.json
        public bool LooksLikeDriveRoot; // a drive root or the root of a cloud drive: a subfolder should be chosen
        public string Message;          // short plain English sentence for the UI
    }

    public class PathCheck
    {
        public bool Ok;
        public string Summary; // e.g. "Found · 3 save files"
        public string Hint;    // how to fix it, may be empty
    }

    public enum FirstSync { NothingYet, CloudNotReady, AlreadyInSync, UploadThisPc, DownloadToThisPc, AskWhichIsNewer, FixConfigFolder, RetryLater }

    public class FirstSyncAdvice
    {
        public FirstSync Action;
        public string Title;
        public string Message;
        public DateTime? LocalNewest;
        public DateTime? CloudTime;
        public string CloudDevice;
        public List<string> Areas = new List<string>();   // user facing labels of the areas that differ (RetryLater: that could not be read)
        public List<string> Skipped = new List<string>(); // labels of the areas skipped because the game config folder is missing
        public bool ConfigMissing;                        // the game config folder (benilla-config) does not exist
        internal List<string> ContentHashes = new List<string>(); // "label=localHash=cloudHash" per checked area, never shown in the UI

        // Cheap equality key: the UI compares the displayed advice with a freshly computed one.
        // Includes the content hashes, so changed files are noticed even if the action and the manifest stay the same.
        public string Fingerprint
        {
            get
            {
                var areas = new List<string>(Areas ?? new List<string>());
                areas.Sort(StringComparer.Ordinal);
                var skipped = new List<string>(Skipped ?? new List<string>());
                skipped.Sort(StringComparer.Ordinal);
                var content = new List<string>(ContentHashes ?? new List<string>());
                content.Sort(StringComparer.Ordinal);
                return Action + "|" + (CloudTime.HasValue ? CloudTime.Value.Ticks.ToString(CultureInfo.InvariantCulture) : "") + "|" + CloudDevice
                    + "|" + string.Join(",", areas.ToArray()) + "|" + string.Join(",", skipped.ToArray())
                    + "|" + (LocalNewest.HasValue ? LocalNewest.Value.Ticks.ToString(CultureInfo.InvariantCulture) : "")
                    + "|" + string.Join(",", content.ToArray());
            }
        }
    }

    public static class Setup
    {
        const string Dot = " \u00B7 ";

        // Names of the root folder of Google Drive for desktop in the common Windows languages
        static readonly string[] GoogleRootNames = {
            "My Drive", "Meine Ablage", "Mi unidad", "Mon Drive", "Il mio Drive", "Mijn Drive", "Meu Drive",
            "M\u00F3j dysk", "\u041C\u043E\u0439 \u0434\u0438\u0441\u043A" };

        // ---------- Cloud drives ----------

        static readonly object cacheLock = new object();
        static List<CloudCandidate> cachedClouds;

        // The drive scan can be slow (sleeping disks, network drives): once per process, a copy of the result
        public static List<CloudCandidate> FindClouds()
        {
            return FindClouds(false);
        }

        public static List<CloudCandidate> FindClouds(bool refresh)
        {
            lock (cacheLock)
            {
                if (refresh || cachedClouds == null) cachedClouds = ScanClouds();
                return new List<CloudCandidate>(cachedClouds);
            }
        }

        static List<CloudCandidate> ScanClouds()
        {
            var roots = new List<string>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try { if (d.DriveType != DriveType.CDRom && d.IsReady) roots.Add(d.RootDirectory.FullName); }
                    catch { }
                }
            }
            catch { }
            return FindClouds(roots, Environment.GetEnvironmentVariable,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        internal static List<CloudCandidate> FindClouds(IEnumerable<string> driveRoots, Func<string, string> env,
            string userProfile, string appData, string localAppData)
        {
            var list = new List<CloudCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> add = delegate(string name, string path)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
                    string full = Path.GetFullPath(path).TrimEnd('\\');
                    if (full.Length == 0 || !seen.Add(full)) return;
                    list.Add(new CloudCandidate { Name = name, Root = full, SuggestedDir = Path.Combine(full, "AzerothSync") });
                }
                catch { }
            };

            // Google Drive for desktop: a virtual drive with a localized "My Drive" folder
            foreach (string root in driveRoots ?? new string[0])
            {
                try
                {
                    foreach (string sub in Directory.GetDirectories(root))
                        if (GoogleRootNames.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase)) add("Google Drive", sub);
                }
                catch { }
            }
            try { if (!string.IsNullOrEmpty(userProfile)) add("Google Drive", Path.Combine(userProfile, "Google Drive")); }
            catch { }

            // OneDrive: the client sets these variables
            foreach (string v in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                try { add("OneDrive", env == null ? null : env(v)); }
                catch { }
            }

            // Dropbox: info.json lists the personal and business folder
            foreach (string baseDir in new[] { appData, localAppData })
            {
                try
                {
                    if (string.IsNullOrEmpty(baseDir)) continue;
                    string info = Path.Combine(Path.Combine(baseDir, "Dropbox"), "info.json");
                    if (!File.Exists(info)) continue;
                    var paths = new List<string>();
                    CollectPaths(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(info)), paths);
                    foreach (string p in paths) add("Dropbox", p);
                }
                catch { }
            }
            return list;
        }

        // Takes every "path" string from the parsed json, wherever it sits (personal, business, later formats)
        static void CollectPaths(object node, List<string> paths)
        {
            var dict = node as Dictionary<string, object>;
            if (dict == null) return;
            foreach (var kv in dict)
            {
                if (kv.Key == "path" && kv.Value is string) paths.Add((string)kv.Value);
                else CollectPaths(kv.Value, paths);
            }
        }

        // ---------- Cloud folder ----------

        public static CloudInfoResult InspectCloud(string cloudDir)
        {
            return InspectCloud(cloudDir, null);
        }

        // clouds = the known cloud drive folders (null: look them up); used to tell if the folder is a drive's root itself
        internal static CloudInfoResult InspectCloud(string cloudDir, List<CloudCandidate> clouds)
        {
            var r = new CloudInfoResult();
            if (string.IsNullOrWhiteSpace(cloudDir))
            {
                r.Content = CloudContent.NotSet;
                r.Message = "No folder chosen yet.";
                return r;
            }
            string full;
            try { full = Path.GetFullPath(cloudDir).TrimEnd('\\'); }
            catch
            {
                r.Content = CloudContent.Unreachable;
                r.Message = "This is not a valid folder path.";
                return r;
            }
            string parent = null;
            try { parent = Path.GetDirectoryName(full); }
            catch { }
            bool isRoot = parent == null || full.Length <= 2; // "G:" after trimming
            r.LooksLikeDriveRoot = isRoot || GoogleRootNames.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase)
                || IsCloudRoot(full, clouds);
            try
            {
                string probe = isRoot ? full + "\\" : parent;
                if (!Directory.Exists(probe))
                {
                    r.Content = CloudContent.Unreachable;
                    r.Message = "Not reachable. Is your cloud drive app running?";
                    return r;
                }
                if (!Directory.Exists(full))
                {
                    r.Content = CloudContent.Missing;
                    r.Message = "Folder does not exist yet. It is created on the first upload.";
                    return r;
                }
                string manifest = Path.Combine(full, "manifest.json");
                bool hasManifest = File.Exists(manifest);
                string data = Path.Combine(full, "data");
                if (!hasManifest && !(Directory.Exists(data) && Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Any()))
                {
                    r.Content = CloudContent.Empty;
                    r.Message = "Empty. The first upload fills it.";
                    return r;
                }
                r.Content = CloudContent.HasData;
                if (hasManifest) ReadManifest(manifest, r);
                if (r.Device != null && r.Time.HasValue) r.Message = "Holds progress from " + r.Device + ", uploaded " + Fmt(r.Time.Value) + ".";
                else if (r.Device != null) r.Message = "Holds progress from " + r.Device + ".";
                else r.Message = "Holds progress from another PC.";
            }
            catch (Exception)
            {
                r.Content = CloudContent.Unreachable;
                r.Message = "Not reachable. Is your cloud drive app running?";
            }
            return r;
        }

        static bool IsCloudRoot(string full, List<CloudCandidate> clouds)
        {
            try
            {
                if (clouds == null) clouds = FindClouds();
                return clouds.Any(c => c.Root.Equals(full, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        // Same fields and formats as the Engine's manifest (Device, Time in round trip format); unreadable stays null
        static void ReadManifest(string path, CloudInfoResult r)
        {
            try
            {
                var m = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                if (m == null) return;
                object device, time;
                if (m.TryGetValue("Device", out device) && device != null && device.ToString().Length > 0) r.Device = device.ToString();
                if (m.TryGetValue("Time", out time) && time != null)
                    r.Time = DateTime.Parse(time.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
            }
            catch { }
        }

        static string Fmt(DateTime t)
        {
            return t.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        // ---------- Path checks ----------

        static PathCheck Check(bool ok, string summary, string hint)
        {
            return new PathCheck { Ok = ok, Summary = summary, Hint = hint ?? "" };
        }

        public static PathCheck CheckGame(string gamePath)
        {
            const string hint = "Choose AzerothSurvivors.exe in the game folder, normally C:\\Games\\AzerothSurvivors.";
            try
            {
                if (string.IsNullOrWhiteSpace(gamePath)) return Check(false, "Not set", hint);
                if (!File.Exists(gamePath)) return Check(false, "Not found", hint);
                if (!Path.GetFileName(gamePath).Equals(Settings.GameExe, StringComparison.OrdinalIgnoreCase))
                    return Check(false, "Found, but this is not " + Settings.GameExe, hint);
                return Check(true, "Found", "");
            }
            catch { return Check(false, "Not readable", hint); }
        }

        public static PathCheck CheckSave(string saveDir)
        {
            const string hint = "Start the game once, then it is created. Normally %APPDATA%\\wenilla-rogue.";
            try
            {
                if (string.IsNullOrWhiteSpace(saveDir) || !Directory.Exists(saveDir)) return Check(false, "Not found", hint);
                string saves = Path.Combine(saveDir, "Saves");
                int n = Directory.Exists(saves) ? Directory.GetFiles(saves, "*.sav").Length : 0;
                if (n == 0) return Check(true, "Folder exists, no save files yet", "");
                string s = "Found" + Dot + n + (n == 1 ? " save file" : " save files");
                if (File.Exists(Path.Combine(saveDir, "characters.txt"))) s += ", character list present";
                return Check(true, s, "");
            }
            catch { return Check(false, "Not readable", hint); }
        }

        public static PathCheck CheckConfig(string configDir)
        {
            const string hint = "This is the benilla-config folder. The game installer puts it at C:\\Git\\wenilla\\benilla-config, otherwise it is a folder named benilla-config next to the game.";
            try
            {
                if (string.IsNullOrWhiteSpace(configDir) || !Directory.Exists(configDir)) return Check(false, "Not found", hint);
                string addons = Path.Combine(configDir, "AddOns"), saved = Path.Combine(configDir, "saved");
                bool hasAddons = Directory.Exists(addons), hasSaved = Directory.Exists(saved);
                if (!hasAddons && !hasSaved) return Check(false, "Folder exists, but has no AddOns or saved folder", hint);
                var parts = new List<string>();
                if (hasAddons)
                {
                    int n = Directory.GetDirectories(addons).Length;
                    parts.Add(n + (n == 1 ? " addon" : " addons"));
                }
                if (hasSaved) parts.Add("addon settings present");
                return Check(true, "Found" + Dot + string.Join(", ", parts.ToArray()), "");
            }
            catch { return Check(false, "Not readable", hint); }
        }

        public static PathCheck CheckCloud(string cloudDir)
        {
            return CheckCloud(InspectCloud(cloudDir), cloudDir);
        }

        internal static PathCheck CheckCloud(CloudInfoResult info, string cloudDir)
        {
            const string choose = "Choose a folder inside your cloud drive, for example ...\\AzerothSync. It needs a cloud drive app such as Google Drive for desktop, OneDrive or Dropbox.";
            switch (info.Content)
            {
                case CloudContent.NotSet: return Check(false, "Not set", choose);
                case CloudContent.Unreachable: return Check(false, info.Message, "Start your cloud drive app and wait until the drive shows up, then check again.");
            }
            if (info.LooksLikeDriveRoot)
                return Check(false, "This is the top level of a drive", "Choose or create a subfolder such as " + SubfolderHint(cloudDir) + ".");
            return Check(true, info.Message, "");
        }

        // "G:\" and "G:" both give G:\AzerothSync (Path.Combine alone would produce "G:AzerothSync")
        static string SubfolderHint(string cloudDir)
        {
            string b = cloudDir.Trim().TrimEnd('\\', '/');
            if (b.EndsWith(":")) b += "\\";
            return Path.Combine(b, "AzerothSync");
        }

        // ---------- First sync ----------

        // Compares every area that exists on this PC (save game, AddOns, addon settings) with the cloud, using the
        // Engine's fingerprints. Not set or unreachable cloud folders give CloudNotReady with an explanation.
        public static FirstSyncAdvice AdviseFirstSync(Settings s)
        {
            var a = new FirstSyncAdvice { Action = FirstSync.NothingYet };
            var cloud = InspectCloud(s.CloudDir);
            a.CloudTime = cloud.Time;
            a.CloudDevice = cloud.Device;
            if (cloud.Content == CloudContent.NotSet || cloud.Content == CloudContent.Unreachable)
            {
                a.Action = FirstSync.CloudNotReady;
                a.Title = "Cloud folder not ready";
                a.Message = cloud.Message;
                return a;
            }
            var local = LocalSaves(s.SaveDir);
            if (local.Length > 0) a.LocalNewest = local.Max(f => File.GetLastWriteTime(f));

            bool anyData = false, down = false, up = false, both = false;
            var unreadable = new List<string>();
            var skippedSets = new List<SyncSet>();
            foreach (var set in AvailableSets(s, skippedSets))
            {
                string lh, ch;
                try
                {
                    var e = new Engine(s);
                    string legacy;
                    lh = e.TreeHash(set.Dir, set.Exclude, out legacy, false);
                    ch = e.TreeHash(set.Cloud, set.Exclude, out legacy, false);
                }
                catch { unreadable.Add(set.Label); continue; } // locked while the cloud app writes: the user retries
                a.ContentHashes.Add(set.Label + "=" + lh + "=" + ch);
                bool lEmpty = Engine.IsEmptyHash(lh), cEmpty = Engine.IsEmptyHash(ch);
                if (lEmpty && cEmpty) continue;
                anyData = true;
                if (!lEmpty && !cEmpty && lh == ch) continue;
                a.Areas.Add(set.Label);
                if (lEmpty) down = true;
                else if (cEmpty) up = true;
                else both = true;
            }

            // Areas that cannot be synced without the game config folder; a problem only if the cloud holds data for them
            bool skippedCloudData = false;
            foreach (var set in skippedSets)
            {
                a.Skipped.Add(set.Label);
                try
                {
                    string legacy;
                    string sh = new Engine(s).TreeHash(set.Cloud, set.Exclude, out legacy, false);
                    a.ContentHashes.Add(set.Label + "=skipped=" + sh);
                    if (!Engine.IsEmptyHash(sh)) skippedCloudData = true;
                }
                catch { unreadable.Add(set.Label); }
            }
            a.ConfigMissing = skippedSets.Count > 0;

            if (unreadable.Count > 0)
            {
                a.Areas = unreadable;
                a.Action = FirstSync.RetryLater;
                a.Title = "Files are busy";
                a.Message = "Some files could not be read, maybe your cloud app is still syncing. Wait a moment and click Check again.";
                return a;
            }

            if (a.Areas.Count == 0)
            {
                if (skippedCloudData)
                {
                    a.Action = FirstSync.FixConfigFolder;
                    a.Title = "Game config folder not found";
                    a.Message = "AddOns and addon settings cannot be synced until the benilla-config folder is chosen. The installer default is C:\\Git\\wenilla\\benilla-config, or benilla-config next to the game.";
                }
                else if (anyData)
                {
                    a.Action = FirstSync.AlreadyInSync;
                    a.Title = "Already in sync";
                    a.Message = "This PC and the cloud hold the same progress.";
                }
                else
                {
                    a.Title = "Nothing to sync yet";
                    a.Message = "Play once, your progress is uploaded after you close the game.";
                }
                return a;
            }

            string areas = string.Join(", ", a.Areas.ToArray());
            string newest = a.LocalNewest.HasValue ? " (newest save " + Fmt(a.LocalNewest.Value) + ")" : "";
            if (both || (down && up))
            {
                a.Action = FirstSync.AskWhichIsNewer;
                a.Title = "Progress on both sides";
                a.Message = "This PC and the cloud differ in: " + areas + ". This PC" + newest + ". Cloud: " + cloud.Message.TrimEnd('.')
                    + ". Choose which one to keep. The other one is backed up first.";
            }
            else if (down)
            {
                a.Action = FirstSync.DownloadToThisPc;
                a.Title = "Get your progress from the cloud";
                a.Message = cloud.Message + " This PC has nothing yet for: " + areas + ". It will be downloaded.";
            }
            else
            {
                a.Action = FirstSync.UploadThisPc;
                a.Title = "Upload this PC's progress";
                a.Message = "The cloud folder holds nothing yet for: " + areas + ". The progress of this PC" + newest + " will be uploaded.";
            }
            return a;
        }

        // The sync areas that are present on this PC; AddOns and addon settings are skipped without a game config folder,
        // like in the Engine. The skipped ones are collected in 'skipped'.
        static List<SyncSet> AvailableSets(Settings s, List<SyncSet> skipped)
        {
            var list = new List<SyncSet>();
            try
            {
                foreach (var set in new Engine(s).Sets)
                {
                    bool missing;
                    try { missing = set.Optional && !Directory.Exists(Path.GetDirectoryName(set.Dir)); }
                    catch { missing = true; }
                    if (missing) { skipped.Add(set); continue; }
                    list.Add(set);
                }
            }
            catch { }
            return list;
        }

        static string[] LocalSaves(string saveDir)
        {
            try
            {
                string saves = Path.Combine(saveDir ?? "", "Saves");
                return Directory.Exists(saves) ? Directory.GetFiles(saves, "*.sav") : new string[0];
            }
            catch { return new string[0]; }
        }
    }
}
