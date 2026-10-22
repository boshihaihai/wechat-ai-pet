using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using WeChatPet.Runtime;

public static class RuntimeSettingsPersistenceTests {
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
    static string Json(RuntimeSettings value){return new JavaScriptSerializer().Serialize(value);}
    static RuntimeSettings Original(){return new RuntimeSettings{Provider="chatgpt",Model="synthetic-old",Device="11111111-1111-1111-1111-111111111111",Validated=true,Paused=false,ReasoningEffort="low",SelectedProcess=11,SelectedProcessBirth=22,ModelsByProvider=new Dictionary<string,string>{{"chatgpt","synthetic-old"}}};}
    static RecommendationController ReadyController(bool complete) {
        var c=new RecommendationController{Connected=true};
        var s=new ChatSnapshot{Available=true,AtEnd=true,DraftEmpty=true,DraftConsistent=true,Session="synthetic-session",InputIdentity="synthetic-input",Tail="tail",Keys=new[]{"tail"},Messages=new List<ChatMessage>{new ChatMessage{Key="tail",Role="peer",Type=1,LocalId=1,Text="synthetic test"}}};
        c.Observe(s,10000000);c.Observe(s,17000000);
        Check(c.PendingId!=null,"synthetic generation exists");
        if(complete)Check(c.Complete(c.PendingId,"synthetic reply",18000000),"synthetic reply completes");
        return c;
    }
    static Action Invalidate(RecommendationController c){return delegate{c.Connected=false;c.Reset();};}
    static void FailedWriteRestoresSettings(string root) {
        string path=Path.Combine(root,"runtime.json");var settings=Original();LocalStore.Write(path,Json(settings),false);string baseline=File.ReadAllText(path);
        var reference=settings;var controller=ReadyController(true);bool invalidatedBeforeWrite=false;
        var persistence=new RuntimeSettingsPersistence(settings,delegate(RuntimeSettings value){invalidatedBeforeWrite=controller.Ready==null&&controller.PendingId==null&&!controller.Connected;LocalStore.Write(path,Json(value),false);});
        settings.Provider="deepseek-api";settings.Model="synthetic-new";settings.Device="22222222-2222-2222-2222-222222222222";
        settings.ReasoningEffort="high";settings.Validated=false;settings.Paused=true;settings.SelectedProcess=33;settings.SelectedProcessBirth=44;
        settings.ModelsByProvider["chatgpt"]="changed";settings.ModelsByProvider["deepseek-api"]="synthetic-new";
        using(new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(!persistence.TrySave(Invalidate(controller)),"locked settings save fails safely");
        Check(invalidatedBeforeWrite,"old reply invalidated before disk access");
        Check(Object.ReferenceEquals(settings,reference),"open windows keep their settings reference");
        Check(Json(settings)==baseline,"all fields and provider map roll back");
        Check(File.ReadAllText(path)==baseline,"failed write leaves saved configuration unchanged");
        Check(persistence.Error==RuntimeSettingsPersistence.SaveFailure,"failure has actionable display text");
        Check(controller.Ready==null&&controller.PendingId==null&&!controller.Connected,"failed configuration cannot enable old or new replies");
        settings.Model="synthetic-retry";settings.ModelsByProvider["chatgpt"]="synthetic-retry";
        Check(persistence.TrySave(Invalidate(controller)),"retry succeeds after lock release");
        Check(persistence.Error==null,"retry clears persistence error");
        string latest=File.ReadAllText(path);settings.Paused=true;
        using(new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(!persistence.TrySave(Invalidate(controller)),"later pause failure is contained");
        Check(Json(settings)==latest&&!settings.Paused,"rollback uses last successful commit");
    }
    static void ValidationFailureCannotEnable(string root) {
        string path=Path.Combine(root,"validation.json");var settings=Original();settings.Validated=false;LocalStore.Write(path,Json(settings),false);
        var controller=ReadyController(false);var persistence=new RuntimeSettingsPersistence(settings,delegate(RuntimeSettings value){LocalStore.Write(path,Json(value),false);});
        settings.Validated=true;
        using(new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(!persistence.TrySave(Invalidate(controller)),"validation enable save failure is contained");
        Check(!settings.Validated&&!controller.Connected&&controller.PendingId==null,"failed enable restores unvalidated state and cancels generation");
        string pending=path+".tmp";if(File.Exists(pending))File.Delete(pending);Directory.CreateDirectory(pending);settings.SelectedProcess=99;
        Check(!persistence.TrySave(Invalidate(controller)),"blocked temporary path is contained");
        Check(settings.SelectedProcess==11,"failed process selection restores prior target");
    }
    static void ReadFailureNeverOverwrites(string root) {
        string path=Path.Combine(root,"read.json"),baseline=Json(Original());File.WriteAllText(path,baseline);int writes=0;string error;
        RuntimeSettings loaded;
        using(new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
            loaded=LocalStore.LoadSettings(path,out error);Check(error==LocalStore.SettingsReadFailure,"read lock is not treated as fresh installation");
            var persistence=new RuntimeSettingsPersistence(loaded,delegate(RuntimeSettings value){writes++;LocalStore.Write(path,Json(value),false);},error);
            loaded.Provider="deepseek-api";Check(!persistence.TrySave(delegate{}),"read failure blocks later saves");
            Check(writes==0,"no writer runs after failed read");
        }
        Check(File.ReadAllText(path)==baseline,"locked original settings remain intact");
        foreach(string malformed in new[]{"{","null","{\"Provider\":null}","{\"ModelsByProvider\":null}","{\"Device\":\"broken\"}"}) {
            File.WriteAllText(path,malformed);loaded=LocalStore.LoadSettings(path,out error);Check(error!=null,"malformed settings explicitly rejected");
            var persistence=new RuntimeSettingsPersistence(loaded,delegate(RuntimeSettings value){writes++;LocalStore.Write(path,Json(value),false);},error);
            Check(!persistence.TrySave(delegate{})&&writes==0,"malformed settings cannot be overwritten with defaults");
            Check(File.ReadAllText(path)==malformed,"malformed source is retained unchanged");
        }
        string absent=Path.Combine(root,"missing","runtime.json");loaded=LocalStore.LoadSettings(absent,out error);
        Check(error==null,"missing file remains a fresh installation");
        var fresh=new RuntimeSettingsPersistence(loaded,delegate(RuntimeSettings value){LocalStore.Write(absent,Json(value),false);},error);
        Check(fresh.TrySave(delegate{})&&File.Exists(absent),"fresh installation can save defaults");
    }
    public static int Main() {
        string testBase=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),testName="WeChatPet-SettingsTests-"+Guid.NewGuid().ToString("N"),root=Path.Combine(testBase,testName);
        try {Directory.CreateDirectory(root);FailedWriteRestoresSettings(root);ValidationFailureCannotEnable(root);ReadFailureNeverOverwrites(root);Console.WriteLine("settings persistence checks passed: "+checks);return 0;}
        catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}
        finally {if(Directory.Exists(root)&&String.Equals(Path.GetDirectoryName(Path.GetFullPath(root)),testBase,StringComparison.OrdinalIgnoreCase)&&String.Equals(Path.GetFileName(root),testName,StringComparison.Ordinal))Directory.Delete(root,true);}
    }
}
