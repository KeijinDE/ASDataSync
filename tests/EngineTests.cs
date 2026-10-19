// Engine tests with artificial folders under %TEMP%. Run with tests\run-tests.ps1.
// Every test builds its own folders, simulates two PCs (A and B) and checks files, return values and questions.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using AzerothSync;

static class EngineTests
{
    static string root;
    static int passed, failed;
    static List<string> asked = new List<string>();
    static List<string> log = new List<string>();

    static int Main()
    {
        root = Path.Combine(Path.GetTempPath(), "azsync-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var tests = typeof(EngineTests).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(m => m.Name.StartsWith("T_")).OrderBy(m => m.Name).ToList();
        foreach (var t in tests)
        {
            asked.Clear(); log.Clear();
            string dir = Path.Combine(root, t.Name);
            Directory.CreateDirectory(dir);
            try { t.Invoke(null, new object[] { dir }); Console.WriteLine("PASS " + t.Name); passed++; }
            catch (Exception ex)
            {
                var e = ex.InnerException ?? ex;
                Console.WriteLine("FAIL " + t.Name + ": " + e.Message);
                foreach (var l in log) Console.WriteLine("     | " + l);
                failed++;
            }
        }
        Console.WriteLine(passed + " passed, " + failed + " failed");
        try { Directory.Delete(root, true); } catch { }
        return failed == 0 ? 0 : 1;
    }

    // ---------- helpers ----------

    static Engine Dev(string dir, string name, Choice conflict = Choice.Abort, bool confirm = true)
    {
        var s = new Settings
        {
            CloudDir = Path.Combine(dir, "cloud"), SaveDir = Path.Combine(dir, name, "save"), ConfigDir = Path.Combine(dir, name, "cfg"),
            StateDir = Path.Combine(dir, name, "state"), GamePath = Path.Combine(dir, "nogame", "NotRunningGame.exe")
        };
        Directory.CreateDirectory(s.ConfigDir);
        var e = new Engine(s);
        e.Log = (t, l) => log.Add(name + ": " + t);
        e.AskConflict = (sets, pulling) => { asked.Add(string.Join(",", sets.Select(x => x.Name))); return conflict; };
        e.Confirm = q => { asked.Add("confirm"); return confirm; };
        return e;
    }

    static void Put(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    static string Get(string path) { return File.Exists(path) ? File.ReadAllText(path) : null; }

    static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }

    static void Throws(Action a, string contains)
    {
        try { a(); }
        catch (Exception ex) { Check(ex.Message.Contains(contains), "wrong error: " + ex.Message); return; }
        throw new Exception("expected an error containing '" + contains + "'");
    }

    static void Seed(string dir)
    {
        Put(Path.Combine(dir, "A", "save", "Saves", "a.sav"), "save1");
        Put(Path.Combine(dir, "A", "save", "characters.txt"), "chars1");
        Put(Path.Combine(dir, "A", "save", "language.txt"), "de");
        Put(Path.Combine(dir, "A", "cfg", "AddOns", "KeijinUI", "Core.lua"), "core1");
        Put(Path.Combine(dir, "A", "cfg", "saved", "KeijinUI.lua"), "sv1");
    }

    // ---------- normal flows ----------

    static void T_01_UploadThenDownloadToNewPc(string dir)
    {
        Seed(dir);
        Check(Dev(dir, "A").Push(false), "push A");
        Put(Path.Combine(dir, "B", "save", "language.txt"), "en");
        Check(Dev(dir, "B").Pull(false), "pull B");
        Check(Get(Path.Combine(dir, "B", "save", "Saves", "a.sav")) == "save1", "save copied");
        Check(Get(Path.Combine(dir, "B", "cfg", "AddOns", "KeijinUI", "Core.lua")) == "core1", "addon copied");
        Check(Get(Path.Combine(dir, "B", "save", "language.txt")) == "en", "language.txt kept");
        Check(!File.Exists(Path.Combine(dir, "cloud", "data", "save", "language.txt")), "language.txt not uploaded");
        Check(asked.Count == 0, "no questions");
    }

    static void T_02_OnlyCloudChangedIsTakenWithBackup(string dir)
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        Dev(dir, "B").Pull(false);
        Put(Path.Combine(dir, "B", "save", "Saves", "a.sav"), "save2");
        Check(Dev(dir, "B").Push(false), "push B");
        Check(Dev(dir, "A").Pull(false), "pull A");
        Check(Get(Path.Combine(dir, "A", "save", "Saves", "a.sav")) == "save2", "cloud taken");
        Check(Directory.GetFiles(Path.Combine(dir, "A", "state", "backups"), "save_*.zip").Length == 1, "local backup");
        Check(Directory.GetFiles(Path.Combine(dir, "cloud", "history"), "save_*.zip").Length == 1, "cloud backup");
        Check(asked.Count == 0, "no questions");
    }

    static void T_03_ConflictAskedOnceAndCancelChangesNothing(string dir)
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        Dev(dir, "B").Pull(false);
        Put(Path.Combine(dir, "A", "save", "Saves", "a.sav"), "A2");
        Put(Path.Combine(dir, "A", "cfg", "saved", "KeijinUI.lua"), "svA");
        Dev(dir, "A").Push(false);
        Put(Path.Combine(dir, "B", "save", "Saves", "a.sav"), "B2");
        Put(Path.Combine(dir, "B", "cfg", "saved", "KeijinUI.lua"), "svB");
        Check(!Dev(dir, "B").Pull(false), "pull returns false on cancel");
        Check(asked.Count == 1 && asked[0] == "save,saved", "one question for both areas: " + string.Join("|", asked));
        Check(Get(Path.Combine(dir, "B", "save", "Saves", "a.sav")) == "B2", "local unchanged");
    }

    static void T_04_KeepLocalThenUploadWithoutSecondQuestion(string dir)
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        Dev(dir, "B").Pull(false);
        Put(Path.Combine(dir, "A", "save", "Saves", "a.sav"), "A2");
        Dev(dir, "A").Push(false);
        Put(Path.Combine(dir, "B", "save", "Saves", "a.sav"), "B2");
        Check(Dev(dir, "B", Choice.Local).Pull(false), "pull keep local");
        asked.Clear();
        Check(Dev(dir, "B").Push(false), "push");
        Check(asked.Count == 0, "no second question");
        Check(Get(Path.Combine(dir, "cloud", "data", "save", "Saves", "a.sav")) == "B2", "uploaded");
    }

    static void T_05_ForcedDownloadOneConfirmation(string dir)
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        Put(Path.Combine(dir, "A", "save", "Saves", "a.sav"), "local");
        Put(Path.Combine(dir, "A", "cfg", "saved", "KeijinUI.lua"), "local");
        Check(!Dev(dir, "A", Choice.Abort, false).Pull(true), "declined returns false");
        Check(Get(Path.Combine(dir, "A", "save", "Saves", "a.sav")) == "local", "unchanged after decline");
        asked.Clear();
        Check(Dev(dir, "A").Pull(true), "forced pull");
        Check(asked.Count == 1 && asked[0] == "confirm", "exactly one confirmation");
        Check(Get(Path.Combine(dir, "A", "save", "Saves", "a.sav")) == "save1", "cloud taken");
    }

    // ---------- safety (review findings) ----------

    static void T_10_IncompleteCloudIsNotTaken(string dir) // F01, review T09
    {
        Seed(dir);
        Put(Path.Combine(dir, "A", "save", "Saves", "b.sav"), "b1");
        Dev(dir, "A").Push(false);
        Dev(dir, "B").Pull(false);
        Put(Path.Combine(dir, "A", "save", "Saves", "a.sav"), "a2");
        Dev(dir, "A").Push(false);
        // B sees only part of the new upload: b.sav has not arrived yet
        File.Delete(Path.Combine(dir, "cloud", "data", "save", "Saves", "b.sav"));
        Check(!Dev(dir, "B").Pull(false), "pull reports incomplete");
        Check(Get(Path.Combine(dir, "B", "save", "Saves", "b.sav")) == "b1", "local file not deleted");
        Check(Get(Path.Combine(dir, "B", "save", "Saves", "a.sav")) == "save1", "local file not replaced");
        // The explicit Download button may still take it, after one confirmation
        asked.Clear();
        Check(Dev(dir, "B").Pull(true), "forced pull");
        Check(asked.Count == 1 && asked[0] == "confirm", "one confirmation");
        Check(Get(Path.Combine(dir, "B", "save", "Saves", "a.sav")) == "a2", "taken on purpose");
    }

    static void T_11_SourceChangedDuringCopyReplacesNothing(string dir) // F01, review T11
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        var b = Dev(dir, "B");
        Put(Path.Combine(dir, "B", "save", "Saves", "old.sav"), "keep me");
        bool changed = false;
        b.Confirm = q => true;
        b.BeforeApply = s => { if (!changed && s.Name == "save") { changed = true; Put(Path.Combine(dir, "cloud", "data", "save", "Saves", "a.sav"), "changed"); } };
        Throws(() => b.Pull(true), "changed while copying");
        Check(Get(Path.Combine(dir, "B", "save", "Saves", "old.sav")) == "keep me", "local unchanged");
        Check(!Directory.Exists(Path.Combine(dir, "B", "save.azsync-new")), "staging removed");
    }

    static void T_12_OverlappingPathsAreRefused(string dir) // F03, review T12
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        Put(Path.Combine(dir, "cloud", "foreign.txt"), "not ours");
        var b = Dev(dir, "B");
        b.S.SaveDir = Path.Combine(dir, "cloud");
        b = new Engine(b.S) { Log = (t, l) => log.Add(t), Confirm = q => true };
        Throws(() => b.Pull(true), "overlap");
        b.S.SaveDir = Path.Combine(dir, "cloud") + "\\data\\..\\";
        b = new Engine(b.S);
        Throws(() => b.Pull(true), "overlap");
        Check(Get(Path.Combine(dir, "cloud", "foreign.txt")) == "not ours", "foreign file untouched");
        var c = Dev(dir, "C");
        c.S.SaveDir = Path.GetPathRoot(dir);
        Throws(() => new Engine(c.S).Validate(), "drive root");
    }

    static void T_13_BackupsInTheSameSecondAreKept(string dir) // F08, review T15
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        for (int i = 0; i < 3; i++)
        {
            Put(Path.Combine(dir, "A", "save", "Saves", "a.sav"), "v" + i);
            Dev(dir, "A").Push(true);
        }
        Check(Directory.GetFiles(Path.Combine(dir, "cloud", "history"), "save_*.zip").Length == 3, "three backups");
    }

    static void T_14_HashIndependentOfCulture(string dir) // F09, review T14
    {
        Put(Path.Combine(dir, "x", "Inventory.txt"), "same");
        Put(Path.Combine(dir, "x", "Zeta.txt"), "z");
        Put(Path.Combine(dir, "x", "_under.txt"), "u");
        var e = Dev(dir, "A");
        string legacy;
        var before = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        string de = e.TreeHash(Path.Combine(dir, "x"), new string[0], out legacy, false);
        Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
        string tr = e.TreeHash(Path.Combine(dir, "x"), new string[0], out legacy, false);
        Thread.CurrentThread.CurrentCulture = before;
        Check(de == tr, "same hash in de-DE and tr-TR");
    }

    static void T_15_OldStateFormatCausesNoFalseConflict(string dir) // F09 migration
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        Dev(dir, "B").Pull(false);
        // Rewrite B's memory in the old format (culture fingerprint without prefix)
        var b = Dev(dir, "B");
        string legacy;
        b.TreeHash(Path.Combine(dir, "B", "save"), new[] { "language.txt" }, out legacy, false);
        File.WriteAllText(Path.Combine(dir, "B", "state", "state.json"), "{\"Hashes\":{\"save\":\"" + legacy + "\"}}");
        Put(Path.Combine(dir, "A", "save", "Saves", "a.sav"), "save2");
        Dev(dir, "A").Push(false);
        Check(Dev(dir, "B").Pull(false), "pull");
        Check(asked.Count == 0, "no conflict question");
        Check(Get(Path.Combine(dir, "B", "save", "Saves", "a.sav")) == "save2", "cloud taken");
    }

    static void T_16_StatusIsReadOnly(string dir) // F10, review T16/T17
    {
        Seed(dir);
        var a = Dev(dir, "A");
        Check(a.Status(), "status on missing sync folder");
        Check(!Directory.Exists(Path.Combine(dir, "cloud")), "sync folder not created");
        Put(Path.Combine(dir, "cloud", "data", "Saves", "a.sav"), "old layout");
        Dev(dir, "A").Status();
        Check(Directory.Exists(Path.Combine(dir, "cloud", "data", "Saves")), "old layout not moved by status");
        Dev(dir, "B").Pull(false);
        Check(Directory.Exists(Path.Combine(dir, "cloud", "data", "save", "Saves")), "old layout moved by pull");
    }

    static void T_17_CloudChangedDuringUploadIsNotOverwritten(string dir) // F01, review T21
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        Dev(dir, "B").Pull(false);
        Put(Path.Combine(dir, "A", "save", "Saves", "a.sav"), "A2");
        var a = Dev(dir, "A");
        a.BeforeApply = s => { if (s.Name == "save") Put(Path.Combine(dir, "cloud", "data", "save", "Saves", "a.sav"), "from B"); };
        Check(!a.Push(false), "push reports not uploaded");
        Check(Get(Path.Combine(dir, "cloud", "data", "save", "Saves", "a.sav")) == "from B", "other PC's upload kept");
        Check(Get(Path.Combine(dir, "A", "save", "Saves", "a.sav")) == "A2", "local kept");
    }

    static void T_18_UploadCancelledReturnsFalse(string dir) // F05, review T24
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        Dev(dir, "B").Pull(false);
        Put(Path.Combine(dir, "A", "save", "Saves", "a.sav"), "A2");
        Dev(dir, "A").Push(false);
        Put(Path.Combine(dir, "B", "save", "Saves", "a.sav"), "B2");
        Check(!Dev(dir, "B", Choice.Abort).Push(false), "cancel returns false");
        Check(!Dev(dir, "B", Choice.Cloud).Push(false), "keep cloud returns false, local not uploaded");
    }

    static void T_19_InterruptedSwapIsRecovered(string dir) // F02
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        // Simulate a crash after the old folder was moved away and before the new one was in place
        Directory.Move(Path.Combine(dir, "A", "save"), Path.Combine(dir, "A", "save.azsync-old"));
        Put(Path.Combine(dir, "A", "save.azsync-new", "half.sav"), "half");
        Check(Dev(dir, "A").Pull(false), "pull");
        Check(Get(Path.Combine(dir, "A", "save", "Saves", "a.sav")) == "save1", "old folder restored");
        Check(!Directory.Exists(Path.Combine(dir, "A", "save.azsync-new")), "staging removed");
        Check(!Directory.Exists(Path.Combine(dir, "A", "save.azsync-old")), "old copy removed");
    }

    static void T_20_LockedTargetStaysComplete(string dir) // F02, review T10
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        Put(Path.Combine(dir, "A", "save", "Saves", "a.sav"), "local");
        Put(Path.Combine(dir, "A", "save", "Saves", "extra.sav"), "extra");
        // Readable, but the folder cannot be moved while the file is open (like a game or editor holding it)
        using (File.Open(Path.Combine(dir, "A", "save", "characters.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var a = Dev(dir, "A");
            Throws(() => a.Pull(true), "in use");
        }
        Check(Get(Path.Combine(dir, "A", "save", "Saves", "a.sav")) == "local", "target unchanged");
        Check(Get(Path.Combine(dir, "A", "save", "Saves", "extra.sav")) == "extra", "no file deleted");
    }

    static void T_21_ManifestRecordsAreas(string dir)
    {
        Seed(dir);
        Dev(dir, "A").Push(false);
        string m = File.ReadAllText(Path.Combine(dir, "cloud", "manifest.json"));
        Check(m.Contains("\"Areas\"") && m.Contains("\"save\"") && m.Contains("\"addons\"") && m.Contains("\"saved\""), "manifest has all areas: " + m);
    }
}
