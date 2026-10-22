using System;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using WeChatPet;

// Offline only: constructed windows are never shown, and every settings path is
// inside a fresh test directory. No runtime, observer, or account is created.
public static class AppearancePersistenceTests {
    static int checks;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    static string Original() { return Json.Serialize(new PetPreferences { Skin = "panda", Size = 1.18, Motion = false }); }
    static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value); }
    static void NoTemporaryFiles(string path) { Check(Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".tmp-*").Length == 0, "temporary appearance files are cleaned"); }

    static void SaveFailureAndRetry(string root, string assets) {
        string path = Path.Combine(root, "save.json"), baseline = Original();
        File.WriteAllText(path, baseline);
        var pet = new PetWindow(assets, path);
        PetSettingsWindow settings = null;
        try {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                pet.SetMotion(true);
                Check(pet.AppearanceError == PetWindow.AppearanceSaveFailure && pet.CanRetryAppearance, "locked save reports a retryable error");
                Check(pet.MotionEnabled, "unsaved appearance remains available as a temporary preview");
            }
            Check(File.ReadAllText(path) == baseline, "atomic save failure preserves the original file byte for byte");
            NoTemporaryFiles(path);
            settings = new PetSettingsWindow(pet, assets, null, null, null, null);
            Check(Field<TextBlock>(settings, "saveStatus").Text == pet.AppearanceError, "appearance settings displays the persistence error");
            Check(Field<Button>(settings, "retrySave").Visibility == Visibility.Visible, "appearance settings exposes retry after write failure");
            Check(pet.RetrySaveAppearance(), "retry succeeds after the file lock is released");
            settings.Refresh();
            Check(pet.AppearanceError == null && !pet.CanRetryAppearance, "successful retry clears the error");
            var saved = Json.Deserialize<PetPreferences>(File.ReadAllText(path));
            Check(saved.Motion && saved.Skin == "panda" && saved.Size == 1.18, "retry commits pending appearance without changing other preferences");
            Check(Field<TextBlock>(settings, "saveStatus").Text == "更改自动保存" && Field<Button>(settings, "retrySave").Visibility == Visibility.Collapsed, "successful retry restores the saved-state UI");
            Check(!pet.IsBubbleVisible, "successful retry removes stale error notice");
            NoTemporaryFiles(path);
        } finally { if (settings != null) settings.Close(); pet.CloseForExit(); }
    }

    static void ReadFailurePreservesOriginal(string root, string assets) {
        string path = Path.Combine(root, "read.json"), baseline = Original();
        File.WriteAllText(path, baseline);
        PetWindow pet;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) pet = new PetWindow(assets, path);
        try {
            Check(pet.AppearanceError == PetWindow.AppearanceReadFailure && !pet.CanRetryAppearance, "locked read is distinguished from first launch");
            pet.SetMotion(false); pet.SetSize(.82);
            Check(!pet.RetrySaveAppearance(), "failed initial read blocks all later writes");
            Check(File.ReadAllText(path) == baseline, "unlocking and editing cannot overwrite unread original settings");
        } finally { pet.CloseForExit(); }
        Check(File.ReadAllText(path) == baseline, "window close cannot overwrite unread original settings");
        NoTemporaryFiles(path);
        var reopened = new PetWindow(assets, path);
        try { Check(reopened.AppearanceError == null && reopened.CurrentSkin == "panda" && reopened.PetScale == 1.18 && !reopened.MotionEnabled, "restart after lock release recovers the saved appearance"); }
        finally { reopened.CloseForExit(); }
    }

    static void InvalidFilesAndFreshInstall(string root, string assets) {
        int index = 0;
        foreach (string invalid in new[] { "{", "null", new string('x', 16384) }) {
            string path = Path.Combine(root, "invalid-" + index++ + ".json"); File.WriteAllText(path, invalid);
            var pet = new PetWindow(assets, path);
            try { Check(pet.AppearanceError == PetWindow.AppearanceReadFailure, "malformed or oversized preferences report a read error"); pet.SetMotion(false); Check(!pet.RetrySaveAppearance(), "malformed preferences stay protected"); }
            finally { pet.CloseForExit(); }
            Check(File.ReadAllText(path) == invalid, "malformed original is retained byte for byte"); NoTemporaryFiles(path);
        }
        string fresh = Path.Combine(root, "new", "appearance.json");
        var first = new PetWindow(assets, fresh);
        try { Check(first.AppearanceError == null, "missing preferences remain a normal first launch"); first.SetMotion(false); Check(File.Exists(fresh) && !Json.Deserialize<PetPreferences>(File.ReadAllText(fresh)).Motion, "first launch creates preferences successfully"); }
        finally { first.CloseForExit(); }
        NoTemporaryFiles(fresh);
    }

    [STAThread] public static int Main(string[] args) {
        string testBase = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\', '/');
        string name = "WeChatPet-AppearanceTests-" + Guid.NewGuid().ToString("N"), root = Path.Combine(testBase, name);
        string assets = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(testBase, ".."));
        try { Directory.CreateDirectory(root); SaveFailureAndRetry(root, assets); ReadFailurePreservesOriginal(root, assets); InvalidFilesAndFreshInstall(root, assets); Console.WriteLine("appearance persistence checks passed: " + checks); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally {
            if (Directory.Exists(root) && String.Equals(Path.GetDirectoryName(Path.GetFullPath(root)), testBase, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root) == name) Directory.Delete(root, true);
        }
    }
}

