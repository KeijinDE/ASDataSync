// Tests for the setup logic (src\Setup.cs and Settings.SetupDone). Part of EngineTests, temp folders only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AzerothSync;

static partial class EngineTests
{
    static string Dir(string a, params string[] more)
    {
        string p = a;
        foreach (string m in more) p = Path.Combine(p, m);
        Directory.CreateDirectory(p);
        return p;
    }

    static Settings Cfg(string dir, string cloud)
    {
        return new Settings { CloudDir = cloud, SaveDir = Path.Combine(dir, "save"), ConfigDir = Path.Combine(dir, "cfg"), StateDir = Path.Combine(dir, "state"), GamePath = Path.Combine(dir, "g.exe") };
    }

    // ---------- Settings ----------

    static void T_Setup_SetupDoneRoundTrip(string dir)
    {
        var s = Cfg(dir, Path.Combine(dir, "c"));
        s.SetupDone = false;
        s.Save();
        Check(!Settings.Load(s.StateDir, null).SetupDone, "SetupDone=0 must stay false even with a cloud folder");
        s.SetupDone = true;
        s.Save();
        Check(Get(Path.Combine(s.StateDir, "settings.ini")).Contains("SetupDone=1"), "key written");
        Check(Settings.Load(s.StateDir, null).SetupDone, "SetupDone=1 loads");
        Check(Settings.SettingsFileExists(s.StateDir), "settings file exists");
        Check(!Settings.SettingsFileExists(Path.Combine(dir, "nothing")), "no settings file");
    }

    static void T_Setup_SetupDoneLegacyRule(string dir)
    {
        string st = Path.Combine(dir, "state");
        Put(Path.Combine(st, "settings.ini"), "GamePath=x\r\nCloudDir=" + Path.Combine(dir, "c") + "\r\n");
        Check(Settings.Load(st, null).SetupDone, "existing user with cloud folder counts as done");
        string st2 = Path.Combine(dir, "state2");
        Put(Path.Combine(st2, "settings.ini"), "GamePath=x\r\n");
        Check(!Settings.Load(st2, null).SetupDone, "no cloud folder: wizard");
        Check(!Settings.Load(Path.Combine(dir, "fresh"), null).SetupDone, "fresh install: wizard");
    }

    static void T_Setup_FreshPcInsideSyncFolder(string dir)
    {
        // exe sits in the sync folder of another PC, but this PC has no settings file: wizard, not "existing user"
        string sync = Dir(dir, "sync");
        Put(Path.Combine(sync, "manifest.json"), "{}");
        var fresh = Settings.Load(Path.Combine(dir, "freshstate"), sync);
        Check(fresh.CloudDir == sync, "folder found automatically");
        Check(!fresh.SetupDone, "fresh PC in the sync folder starts the wizard");
        string st = Path.Combine(dir, "oldstate");
        Put(Path.Combine(st, "config.json"), "{\"CloudDir\": \"" + Path.Combine(dir, "c").Replace("\\", "\\\\") + "\"}");
        Check(Settings.Load(st, null).SetupDone, "old config.json with a cloud folder counts as done");
        string st2 = Path.Combine(dir, "iniState");
        Put(Path.Combine(st2, "settings.ini"), "GamePath=x\r\n");
        Check(Settings.Load(st2, sync).SetupDone, "existing settings file, folder from the exe location: not a new user");
    }

    // ---------- FindClouds ----------

    static void T_Setup_FindClouds(string dir)
    {
        string g = Dir(dir, "G", "Meine Ablage"), g2 = Dir(dir, "H", "My Drive");
        Dir(dir, "G", "Other");
        string od = Dir(dir, "od"), odc = Dir(dir, "odc");
        string profile = Dir(dir, "profile"), legacy = Dir(dir, "profile", "Google Drive");
        string appData = Dir(dir, "roaming"), local = Dir(dir, "local");
        string db1 = Dir(dir, "Dropbox"), db2 = Dir(dir, "Dropbox (Work)");
        Put(Path.Combine(appData, "Dropbox", "info.json"), "{\"personal\": {\"path\": \"" + db1.Replace("\\", "\\\\") + "\", \"host\": 1}, \"business\": {\"path\": \"" + db2.Replace("\\", "\\\\") + "\"}}");
        Put(Path.Combine(local, "Dropbox", "info.json"), "{\"personal\": {\"path\": \"" + db1.Replace("\\", "\\\\") + "\"}}");
        var env = new Dictionary<string, string> { { "OneDrive", od }, { "OneDriveConsumer", od }, { "OneDriveCommercial", odc } };
        var list = Setup.FindClouds(new[] { Path.Combine(dir, "G"), Path.Combine(dir, "H"), Path.Combine(dir, "missing") },
            v => { string r; return env.TryGetValue(v, out r) ? r : null; }, profile, appData, local);
        Check(list.Count == 7, "expected 7 candidates, got " + list.Count + ": " + string.Join("; ", list.Select(c => c.Name + "=" + c.Root).ToArray()));
        Check(list.Count(c => c.Name == "Google Drive") == 3, "google x3 (two roots and legacy)");
        Check(list.Count(c => c.Name == "OneDrive") == 2, "onedrive distinct x2");
        Check(list.Count(c => c.Name == "Dropbox") == 2, "dropbox deduplicated x2");
        Check(list.Any(c => c.Root == g && c.SuggestedDir == Path.Combine(g, "AzerothSync")), "suggested dir");
        Check(list.Any(c => c.Root == g2) && list.Any(c => c.Root == legacy), "other google roots");
    }

    static void T_Setup_FindCloudsCache(string dir)
    {
        var a = Setup.FindClouds();
        a.Add(new CloudCandidate { Name = "x", Root = "x", SuggestedDir = "x" });
        var b = Setup.FindClouds();
        Check(b.Count == a.Count - 1, "cached result is returned as a copy");
        var c = Setup.FindClouds(true);
        Check(c.Count == b.Count, "refresh rescans");
        Check(!object.ReferenceEquals(Setup.FindClouds(false), Setup.FindClouds(false)), "copies are independent");
    }

    static void T_Setup_FindCloudsTolerant(string dir)
    {
        string appData = Dir(dir, "roaming");
        Put(Path.Combine(appData, "Dropbox", "info.json"), "{ this is not json");
        var list = Setup.FindClouds(new[] { Path.Combine(dir, "nope") }, v => "?:\\<>invalid", Path.Combine(dir, "p"), appData, null);
        Check(list.Count == 0, "nothing found, nothing thrown");
        Check(Setup.FindClouds(null, null, null, null, null).Count == 0, "all null");
    }

    // ---------- InspectCloud ----------

    static void T_Setup_InspectCloud(string dir)
    {
        var none = new List<CloudCandidate>();
        Check(Setup.InspectCloud("", none).Content == CloudContent.NotSet, "not set");
        var un = Setup.InspectCloud(Path.Combine(dir, "gone", "AzerothSync"), none);
        Check(un.Content == CloudContent.Unreachable && un.Message.StartsWith("Not reachable"), "unreachable");
        Dir(dir, "parent");
        var mi = Setup.InspectCloud(Path.Combine(dir, "parent", "AzerothSync"), none);
        Check(mi.Content == CloudContent.Missing && mi.Message.Contains("created on the first upload"), "missing");
        string c = Dir(dir, "parent", "AzerothSync");
        var em = Setup.InspectCloud(c, none);
        Check(em.Content == CloudContent.Empty && em.Message.Contains("first upload"), "empty");
        Dir(c, "data", "save");
        Check(Setup.InspectCloud(c, none).Content == CloudContent.Empty, "empty data folders");
        Put(Path.Combine(c, "data", "save", "Saves", "a.sav"), "x");
        var hd = Setup.InspectCloud(c, none);
        Check(hd.Content == CloudContent.HasData && hd.Device == null, "has data without manifest");
        Put(Path.Combine(c, "manifest.json"), "{\"Format\":2,\"Device\":\"KDESKTOP\",\"Time\":\"2026-10-22T18:01:00.0000000+02:00\"}");
        hd = Setup.InspectCloud(c, none);
        Check(hd.Content == CloudContent.HasData && hd.Device == "KDESKTOP" && hd.Time.HasValue, "manifest parsed");
        string expect = "Holds progress from KDESKTOP, uploaded " + hd.Time.Value.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) + ".";
        Check(hd.Message == expect, "message: " + hd.Message);
        Check(!hd.LooksLikeDriveRoot, "normal subfolder is fine");
        Put(Path.Combine(c, "manifest.json"), "garbage");
        hd = Setup.InspectCloud(c, none);
        Check(hd.Content == CloudContent.HasData && hd.Device == null && hd.Message.Length > 0, "broken manifest tolerated");
    }

    static void T_Setup_InspectCloudDriveRoot(string dir)
    {
        string my = Dir(dir, "G", "Mein Ablage");
        Check(Setup.InspectCloud(Dir(dir, "G", "Meine Ablage"), new List<CloudCandidate>()).LooksLikeDriveRoot, "google root name");
        var known = new List<CloudCandidate> { new CloudCandidate { Name = "OneDrive", Root = my, SuggestedDir = Path.Combine(my, "AzerothSync") } };
        Check(Setup.InspectCloud(my, known).LooksLikeDriveRoot, "known cloud root");
        Check(!Setup.InspectCloud(Dir(my, "AzerothSync"), known).LooksLikeDriveRoot, "subfolder of it is fine");
        string driveRoot = Path.GetPathRoot(Path.GetTempPath());
        Check(Setup.InspectCloud(driveRoot, new List<CloudCandidate>()).LooksLikeDriveRoot, "drive root");
    }

    // ---------- Path checks ----------

    static void T_Setup_CheckGame(string dir)
    {
        Check(!Setup.CheckGame("").Ok && !Setup.CheckGame(Path.Combine(dir, "AzerothSurvivors.exe")).Ok, "missing");
        Put(Path.Combine(dir, "AzerothSurvivors.exe"), "x");
        var ok = Setup.CheckGame(Path.Combine(dir, "AzerothSurvivors.exe"));
        Check(ok.Ok && ok.Summary == "Found", "found");
        Put(Path.Combine(dir, "other.exe"), "x");
        var bad = Setup.CheckGame(Path.Combine(dir, "other.exe"));
        Check(!bad.Ok && bad.Hint.Length > 0, "wrong name");
    }

    static void T_Setup_CheckSave(string dir)
    {
        var miss = Setup.CheckSave(Path.Combine(dir, "save"));
        Check(!miss.Ok && miss.Hint.Contains("wenilla-rogue"), "missing");
        Dir(dir, "save");
        var empty = Setup.CheckSave(Path.Combine(dir, "save"));
        Check(empty.Ok && empty.Summary == "Folder exists, no save files yet", "no saves");
        for (int i = 0; i < 3; i++) Put(Path.Combine(dir, "save", "Saves", i + ".sav"), "x");
        var ok = Setup.CheckSave(Path.Combine(dir, "save"));
        Check(ok.Ok && ok.Summary.StartsWith("Found \u00B7 3 save files") && !ok.Summary.Contains("character"), "3 saves: " + ok.Summary);
        Put(Path.Combine(dir, "save", "characters.txt"), "c");
        Check(Setup.CheckSave(Path.Combine(dir, "save")).Summary.Contains("character list"), "characters.txt");
    }

    static void T_Setup_CheckConfig(string dir)
    {
        Check(!Setup.CheckConfig(Path.Combine(dir, "cfg")).Ok, "missing");
        Dir(dir, "cfg");
        var none = Setup.CheckConfig(Path.Combine(dir, "cfg"));
        Check(!none.Ok && none.Hint.Contains("benilla-config") && none.Hint.Contains(@"C:\Git\wenilla\benilla-config"), "empty folder");
        for (int i = 0; i < 12; i++) Dir(dir, "cfg", "AddOns", "A" + i);
        Dir(dir, "cfg", "saved");
        var ok = Setup.CheckConfig(Path.Combine(dir, "cfg"));
        Check(ok.Ok && ok.Summary == "Found \u00B7 12 addons, addon settings present", ok.Summary);
    }

    static void T_Setup_CheckCloud(string dir)
    {
        Check(!Setup.CheckCloud("").Ok, "not set");
        Check(!Setup.CheckCloud(Path.Combine(dir, "gone", "x")).Ok, "unreachable");
        string c = Dir(dir, "AzerothSync");
        var ok = Setup.CheckCloud(c);
        Check(ok.Ok && ok.Summary.StartsWith("Empty"), "empty is ok");
        Check(Setup.CheckCloud(Path.Combine(dir, "NewFolder")).Ok, "missing is ok");
        var root = Setup.CheckCloud(Path.GetPathRoot(Path.GetTempPath()));
        Check(!root.Ok && root.Hint.Contains("AzerothSync"), "drive root refused: " + root.Hint);
        Check(root.Hint.Contains(":\\AzerothSync") && !root.Hint.Contains(":AzerothSync"), "root hint has a separator: " + root.Hint);
    }

    // ---------- AdviseFirstSync ----------

    static Settings Advice(string dir, bool localSave, int cloudState)
    {
        // cloudState: 0 missing, 1 empty, 2 data with manifest
        var s = Cfg(dir, Path.Combine(dir, "cloud"));
        Dir(dir);
        if (localSave) Put(Path.Combine(s.SaveDir, "Saves", "a.sav"), "local");
        if (cloudState >= 1) Dir(s.CloudDir);
        if (cloudState == 2)
        {
            Put(Path.Combine(s.CloudDir, "data", "save", "Saves", "a.sav"), "cloud");
            Put(Path.Combine(s.CloudDir, "manifest.json"), "{\"Device\":\"OTHERPC\",\"Time\":\"2026-10-22T10:00:00.0000000Z\"}");
        }
        return s;
    }

    static void T_Setup_AdviseNothingYet(string dir)
    {
        var a = Setup.AdviseFirstSync(Advice(dir, false, 1));
        Check(a.Action == FirstSync.NothingYet && a.Message.Contains("Play once"), "both empty");
        Check(Setup.AdviseFirstSync(Cfg(dir, "")).Action == FirstSync.CloudNotReady, "not set");
    }

    static void T_Setup_AdviseUpload(string dir)
    {
        var a = Setup.AdviseFirstSync(Advice(dir, true, 1));
        Check(a.Action == FirstSync.UploadThisPc && a.LocalNewest.HasValue, "empty cloud");
        a = Setup.AdviseFirstSync(Advice(Dir(dir, "m"), true, 0));
        Check(a.Action == FirstSync.UploadThisPc, "missing cloud");
    }

    static void T_Setup_AdviseDownload(string dir)
    {
        var a = Setup.AdviseFirstSync(Advice(dir, false, 2));
        Check(a.Action == FirstSync.DownloadToThisPc && a.CloudDevice == "OTHERPC" && a.CloudTime.HasValue, "download");
        Check(a.Areas.Count == 1 && a.Areas[0] == "Save game", "areas: " + string.Join(",", a.Areas.ToArray()));
    }

    static void T_Setup_AdviseAreaMix(string dir)
    {
        // cloud has only AddOns, this PC has a save and a config folder: areas differ in opposite directions
        var s = Advice(Dir(dir, "m1"), true, 1);
        Put(Path.Combine(s.CloudDir, "data", "addons", "KeijinUI", "Core.lua"), "core");
        Dir(s.ConfigDir);
        var a = Setup.AdviseFirstSync(s);
        Check(a.Action == FirstSync.AskWhichIsNewer && a.Areas.Contains("Save game") && a.Areas.Contains("AddOns"), "mix: " + a.Action);

        // cloud only has AddOns, this PC has nothing at all: download, never "nothing yet"
        var d = Cfg(Dir(dir, "m2"), Path.Combine(dir, "m2", "cloud"));
        Dir(d.ConfigDir);
        Put(Path.Combine(d.CloudDir, "data", "addons", "KeijinUI", "Core.lua"), "core");
        a = Setup.AdviseFirstSync(d);
        Check(a.Action == FirstSync.DownloadToThisPc && a.Areas.Count == 1 && a.Areas[0] == "AddOns" && a.Message.Contains("AddOns"), "addons only: " + a.Action);

        // after the download of that area, nothing is left to do
        Put(Path.Combine(d.ConfigDir, "AddOns", "KeijinUI", "Core.lua"), "core");
        a = Setup.AdviseFirstSync(d);
        Check(a.Action == FirstSync.AlreadyInSync && a.Areas.Count == 0, "after download: " + a.Action);

        // only this PC has addon settings: upload, named
        var u = Cfg(Dir(dir, "m3"), Path.Combine(dir, "m3", "cloud"));
        Put(Path.Combine(u.ConfigDir, "saved", "KeijinUI.lua"), "sv");
        Dir(u.CloudDir);
        a = Setup.AdviseFirstSync(u);
        Check(a.Action == FirstSync.UploadThisPc && a.Areas.Count == 1 && a.Areas[0] == "Addon settings" && a.Message.Contains("Addon settings"), "saved only: " + a.Action);

        // config folder missing and the cloud holds AddOns: never "nothing yet" or "in sync"
        var n = Cfg(Dir(dir, "m4"), Path.Combine(dir, "m4", "cloud"));
        Put(Path.Combine(n.CloudDir, "data", "addons", "A", "a.lua"), "x");
        a = Setup.AdviseFirstSync(n);
        Check(a.Action == FirstSync.FixConfigFolder && a.ConfigMissing && a.Title == "Game config folder not found"
            && a.Skipped.Contains("AddOns") && a.Skipped.Contains("Addon settings") && a.Message.Contains("benilla-config"), "config missing with cloud addons: " + a.Action);

        // config folder missing, nothing in the cloud for those areas: normal result, Skipped still filled
        var k = Advice(Dir(dir, "m5"), false, 2);
        a = Setup.AdviseFirstSync(k);
        Check(a.Action == FirstSync.DownloadToThisPc && a.ConfigMissing && a.Skipped.Count == 2, "config missing, no cloud addons: " + a.Action);
        Put(Path.Combine(k.SaveDir, "Saves", "a.sav"), "cloud");
        a = Setup.AdviseFirstSync(k);
        Check(a.Action == FirstSync.AlreadyInSync && a.ConfigMissing && a.Skipped.Count == 2, "in sync, areas skipped: " + a.Action);
        var e = Setup.AdviseFirstSync(Advice(Dir(dir, "m6"), false, 1));
        Check(e.Action == FirstSync.NothingYet && e.Skipped.Count == 2, "nothing yet, skipped filled");
    }

    static void T_Setup_AdviseRetryLater(string dir)
    {
        var s = Advice(dir, true, 2);
        File.WriteAllText(Path.Combine(s.CloudDir, "data", "save", "Saves", "a.sav"), "local");
        FirstSyncAdvice a;
        using (new FileStream(Path.Combine(s.SaveDir, "Saves", "a.sav"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            a = Setup.AdviseFirstSync(s);
        Check(a.Action == FirstSync.RetryLater && a.Title == "Files are busy" && a.Areas.Contains("Save game")
            && a.Message.Contains("click Check again"), "locked file: " + a.Action);
        Check(Setup.AdviseFirstSync(s).Action == FirstSync.AlreadyInSync, "readable again");
        Check(Setup.AdviseFirstSync(Cfg(dir, "")).Action == FirstSync.CloudNotReady, "cloud not ready still wins");
    }

    static void T_Setup_AdviseFingerprint(string dir)
    {
        var s = Advice(dir, true, 2);
        string f1 = Setup.AdviseFirstSync(s).Fingerprint;
        Check(f1 == Setup.AdviseFirstSync(s).Fingerprint, "stable");
        Put(Path.Combine(s.CloudDir, "manifest.json"), "{\"Device\":\"OTHERPC\",\"Time\":\"2026-10-23T10:00:00.0000000Z\"}");
        Check(f1 != Setup.AdviseFirstSync(s).Fingerprint, "cloud time changes the fingerprint");
        var x = new FirstSyncAdvice { Action = FirstSync.UploadThisPc, Areas = new List<string> { "b", "a" } };
        var y = new FirstSyncAdvice { Action = FirstSync.UploadThisPc, Areas = new List<string> { "a", "b" } };
        Check(x.Fingerprint == y.Fingerprint, "area order does not matter");
        y.Skipped.Add("AddOns");
        Check(x.Fingerprint != y.Fingerprint, "skipped counts");
    }

    static void T_Setup_AdviseFingerprintContent(string dir)
    {
        var s = Advice(dir, true, 2);
        var first = Setup.AdviseFirstSync(s);
        Check(first.Action == FirstSync.AskWhichIsNewer, "both sides differ");
        string f = first.Fingerprint;
        Check(f == Setup.AdviseFirstSync(s).Fingerprint, "nothing changed, same fingerprint");

        string local = Path.Combine(s.SaveDir, "Saves", "a.sav");
        File.WriteAllText(local, "local, now with more progress");
        var changed = Setup.AdviseFirstSync(s);
        Check(changed.Action == first.Action && changed.Areas.Count == first.Areas.Count, "same action and areas");
        Check(changed.Fingerprint != f, "local content changes the fingerprint");
        f = changed.Fingerprint;
        Check(f == Setup.AdviseFirstSync(s).Fingerprint, "stable again");

        File.WriteAllText(Path.Combine(s.CloudDir, "data", "save", "Saves", "a.sav"), "cloud, changed without a new manifest");
        var cloudChanged = Setup.AdviseFirstSync(s);
        Check(cloudChanged.Action == first.Action && cloudChanged.CloudTime == first.CloudTime, "manifest unchanged");
        Check(cloudChanged.Fingerprint != f, "cloud content changes the fingerprint");
        Check(!cloudChanged.Message.Contains("="), "hashes do not leak into the message");
    }

    static void T_Setup_AdviseSameSaveDifferentAddons(string dir)
    {
        var s = Advice(dir, true, 2);
        File.WriteAllText(Path.Combine(s.CloudDir, "data", "save", "Saves", "a.sav"), "local");
        Put(Path.Combine(s.SaveDir, "language.txt"), "de");
        Put(Path.Combine(s.ConfigDir, "AddOns", "A", "a.lua"), "local");
        Put(Path.Combine(s.CloudDir, "data", "addons", "A", "a.lua"), "cloud");
        var a = Setup.AdviseFirstSync(s);
        Check(a.Action == FirstSync.AskWhichIsNewer && a.Areas.Count == 1 && a.Areas[0] == "AddOns", "addons differ: " + a.Action + " " + string.Join(",", a.Areas.ToArray()));
        File.WriteAllText(Path.Combine(s.CloudDir, "data", "addons", "A", "a.lua"), "local");
        a = Setup.AdviseFirstSync(s);
        Check(a.Action == FirstSync.AlreadyInSync, "language.txt is excluded, all equal: " + a.Action);
    }

    static void T_Setup_AdviseAsk(string dir)
    {
        var a = Setup.AdviseFirstSync(Advice(dir, true, 2));
        Check(a.Action == FirstSync.AskWhichIsNewer && a.LocalNewest.HasValue && a.CloudTime.HasValue && a.CloudDevice == "OTHERPC", "ask");
        Check(a.Message.Contains("OTHERPC"), a.Message);
    }

    static void T_Setup_AdviseAlreadyInSync(string dir)
    {
        var s = Advice(dir, true, 2);
        File.WriteAllText(Path.Combine(s.CloudDir, "data", "save", "Saves", "a.sav"), "local");
        var a = Setup.AdviseFirstSync(s);
        Check(a.Action == FirstSync.AlreadyInSync, "same content: " + a.Title);
    }
}
