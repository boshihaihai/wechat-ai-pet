using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;

// Compile with Installer.cs, using /main:InstallerSafetyTests. All targets are
// isolated below a fresh directory beside the test executable; no actual
// installation, user-data directory or registry is used.
public static class InstallerSafetyTests {
    static int checks;
    static string scratch,recovery;
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    static void Fails(Action action,string label){bool failed=false;try{action();}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}Check(failed,label);}
    static string Folder(string name){string path=Path.Combine(scratch,name);Directory.CreateDirectory(path);return path;}
    static string FileAt(string root,string name){return Path.Combine(root,name);}
    static string Read(string root,string name){return File.ReadAllText(FileAt(root,name));}
    static string Existing(string name) {
        string root=Folder(name);
        File.WriteAllText(FileAt(root,"WeChatPet.exe"),"old-main");
        File.WriteAllText(FileAt(root,"WeChatObserver.exe"),"old-observer");
        File.WriteAllText(FileAt(root,"Uninstall.exe"),"old-uninstaller");
        File.WriteAllText(FileAt(root,".wechat-pet-install"),"WeChatPet.Personal.v1");
        return root;
    }
    static MemoryStream Payload(params string[] pairs) {
        var stream=new MemoryStream();
        using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,true))for(int i=0;i<pairs.Length;i+=2)using(var writer=new StreamWriter(archive.CreateEntry(pairs[i]).Open(),new UTF8Encoding(false)))writer.Write(pairs[i+1]);
        stream.Position=0;return stream;
    }
    static MemoryStream Update(){return Payload("WeChatPet.exe","new-main","new-file.txt","new-file","WeChatObserver.exe","new-observer");}
    static void Install(string root,Action<int> fault=null){using(var payload=Update())PetInstaller.InstallPayload(root,root,payload,recovery,fault);}
    static bool NoStaging(string root){return Directory.GetDirectories(root,".install-*").Length==0;}
    const int BusyExitCode=73;
    static int ConcurrentOperation(string mode,string root) {
        using(var helper=Process.Start(new ProcessStartInfo {
            FileName=Assembly.GetExecutingAssembly().Location,
            Arguments=mode+" \""+root+"\" \""+recovery+"\"",
            UseShellExecute=false,CreateNoWindow=true
        })) {
            if(!helper.WaitForExit(10000)) {
                // Only this isolated test child is stopped if its test hangs.
                helper.Kill();helper.WaitForExit();throw new Exception("concurrent test child did not exit");
            }
            return helper.ExitCode;
        }
    }
    static void IsBusy(Action action,string label) {
        bool busy=false;try{action();}catch(PetInstaller.MaintenanceBusyException){busy=true;}Check(busy,label);
    }
    [STAThread]public static int Main(string[] args) {
        if(args.Length==1&&args[0]=="--hold"){Thread.Sleep(1400);return 0;}
        if(args.Length==3&&(args[0]=="--attempt-install"||args[0]=="--attempt-uninstall")) {
            recovery=args[2];
            try {
                if(args[0]=="--attempt-install")Install(args[1]);
                else PetInstaller.RemoveThenUnregister(args[1],args[1],recovery,delegate{});
                return 0;
            }catch(PetInstaller.MaintenanceBusyException){return BusyExitCode;}
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        scratch=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"WeChatPet-InstallerTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
        recovery=Path.Combine(scratch,"recovery.exe");File.WriteAllText(recovery,"recovery-uninstaller");
        try {
            string root=Existing("success");Install(root);
            Check(Read(root,"WeChatPet.exe")=="new-main"&&Read(root,"WeChatObserver.exe")=="new-observer","upgrade replaces all program files");
            Check(Read(root,"Uninstall.exe")=="recovery-uninstaller"&&Read(root,"new-file.txt")=="new-file"&&NoStaging(root),"upgrade commits uninstaller and removes staging");

            root=Existing("concurrent-upgrade");string independent=Existing("independent-upgrade");
            Install(root,delegate(int step) {
                if(step!=1)return;
                Check(Read(root,"WeChatPet.exe")=="new-main"&&Read(root,"WeChatObserver.exe")=="old-observer","concurrency probes run between actual file commits");
                Check(ConcurrentOperation("--attempt-install",Path.Combine(root.ToUpperInvariant(),"."))==BusyExitCode,"canonical same-target installer in another process is rejected");
                Check(ConcurrentOperation("--attempt-uninstall",root)==BusyExitCode,"uninstaller in another process cannot interrupt upgrade");
                IsBusy(delegate{Install(root);},"same-thread nested install cannot exploit mutex reentrancy");
                IsBusy(delegate{PetInstaller.RemoveProgramDirectory(root,root,recovery);},"same-thread nested uninstall cannot interrupt upgrade");
                Check(ConcurrentOperation("--attempt-install",independent)==0,"different install targets can update independently");
            });
            Check(Read(root,"WeChatPet.exe")=="new-main"&&Read(root,"WeChatObserver.exe")=="new-observer"&&NoStaging(root),"competing operations do not mix installed versions");
            Check(Read(independent,"WeChatPet.exe")=="new-main"&&NoStaging(independent),"independent target finishes normally");

            root=Existing("concurrent-uninstall");
            PetInstaller.RemoveThenUnregister(root,root,recovery,delegate {
                Check(!Directory.Exists(root),"unregister probe runs after all files are removed");
                Check(ConcurrentOperation("--attempt-install",root)==BusyExitCode,"uninstall lock remains held through registration cleanup");
                IsBusy(delegate{Install(root);},"same-thread nested install is rejected during unregister");
                Check(!Directory.Exists(root),"blocked install does not recreate removed target");
            },delegate(int step) {
                if(step!=1)return;
                Check(ConcurrentOperation("--attempt-install",root)==BusyExitCode,"installer cannot interrupt partial file removal");
                Check(ConcurrentOperation("--attempt-uninstall",root)==BusyExitCode,"second uninstaller cannot interrupt partial file removal");
            });
            Check(ConcurrentOperation("--attempt-install",root)==0,"completed uninstall releases cross-process lock for a later install");

            root=Existing("occupied");
            using(var held=new FileStream(FileAt(root,"WeChatObserver.exe"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Fails(delegate{Install(root);},"occupied observer rejects upgrade before commit");
            Check(Read(root,"WeChatPet.exe")=="old-main"&&Read(root,"WeChatObserver.exe")=="old-observer"&&!File.Exists(FileAt(root,"new-file.txt"))&&NoStaging(root),"preflight failure preserves original version");

            root=Existing("rollback");Fails(delegate{Install(root,delegate(int step){if(step==2)throw new IOException("synthetic commit failure");});},"mid-upgrade failure is reported");
            Check(Read(root,"WeChatPet.exe")=="old-main"&&Read(root,"WeChatObserver.exe")=="old-observer"&&Read(root,"Uninstall.exe")=="old-uninstaller"&&!File.Exists(FileAt(root,"new-file.txt"))&&NoStaging(root),"rollback restores originals and removes new files");
            Check(ConcurrentOperation("--attempt-install",root)==0,"failed upgrade releases cross-process lock after rollback");

            root=Existing("rollback-uninstaller");Fails(delegate{Install(root,delegate(int step){if(step==4)throw new IOException("synthetic marker failure");});},"failure after uninstaller replacement is reported");
            Check(Read(root,"WeChatPet.exe")=="old-main"&&Read(root,"Uninstall.exe")=="old-uninstaller"&&NoStaging(root),"rollback restores previous uninstaller too");

            root=Existing("traversal");using(var payload=Payload("../escaped.txt","bad"))Fails(delegate{PetInstaller.InstallPayload(root,root,payload,recovery);},"traversal resource is rejected");
            Check(!File.Exists(Path.Combine(scratch,"escaped.txt"))&&Read(root,"WeChatPet.exe")=="old-main"&&NoStaging(root),"traversal does not write outside target");
            using(var payload=Update())Fails(delegate{PetInstaller.InstallPayload(root,Path.Combine(scratch,"other"),payload,recovery);},"mismatched absolute install directory rejected");
            using(var payload=Payload("same.txt","one","SAME.txt","two"))Fails(delegate{PetInstaller.InstallPayload(root,root,payload,recovery);},"duplicate archive destinations rejected");

            root=Existing("uninstall-occupied");bool unregister=false;
            using(var held=new FileStream(FileAt(root,"WeChatObserver.exe"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Fails(delegate{PetInstaller.RemoveThenUnregister(root,root,recovery,delegate{unregister=true;});},"occupied file blocks uninstall");
            Check(!unregister&&File.Exists(FileAt(root,"Uninstall.exe"))&&Read(root,"WeChatPet.exe")=="old-main","failed uninstall retains registration and files");

            root=Existing("uninstall-partial");unregister=false;
            Fails(delegate{PetInstaller.RemoveThenUnregister(root,root,recovery,delegate{unregister=true;},delegate(int step){if(step==3)throw new IOException("synthetic delete failure");});},"mid-uninstall deletion failure is reported");
            Check(!unregister&&File.Exists(FileAt(root,"Uninstall.exe"))&&Read(root,".wechat-pet-install")=="WeChatPet.Personal.v1","partial uninstall restores retryable uninstaller and marker");
            PetInstaller.RemoveThenUnregister(root,root,recovery,delegate{Check(!Directory.Exists(root),"registration cleanup waits for full directory deletion");unregister=true;});
            Check(unregister&&!Directory.Exists(root),"partial uninstall can be retried successfully");

            root=Existing("cleanup-failure");Fails(delegate{PetInstaller.RemoveThenUnregister(root,root,recovery,delegate{throw new IOException("synthetic registration failure");});},"registration cleanup failure is reported");
            Check(File.Exists(FileAt(root,"Uninstall.exe"))&&File.Exists(FileAt(root,".wechat-pet-install")),"cleanup failure restores retry entry files");

            string settings=Folder("separate-user-data");File.WriteAllText(FileAt(settings,"credentials.dpapi"),"synthetic encrypted credential");
            root=Existing("uninstall-success");PetInstaller.RemoveThenUnregister(root,root,recovery,delegate{});
            Check(!Directory.Exists(root)&&Read(settings,"credentials.dpapi")=="synthetic encrypted credential","uninstall leaves separate user data untouched");
            root=Existing("outside-rejection");Fails(delegate{PetInstaller.RemoveThenUnregister(root,Path.Combine(scratch,"other"),recovery,delegate{throw new Exception("must not unregister");});},"mismatched uninstall target rejected");
            Check(File.Exists(FileAt(root,"WeChatPet.exe")),"rejected target is unchanged");

            string own=Folder("owned-process"),other=Folder("unrelated-process");
            File.Copy(Assembly.GetExecutingAssembly().Location,FileAt(own,"WeChatObserver.exe"));File.Copy(Assembly.GetExecutingAssembly().Location,FileAt(other,"WeChatPet.exe"));
            using(var helper=Process.Start(new ProcessStartInfo{FileName=FileAt(own,"WeChatObserver.exe"),Arguments="--hold",UseShellExecute=false,CreateNoWindow=true})) {
                try {
                    Fails(delegate{PetInstaller.WaitForInstalledProcesses(own,100);},"installer waits for its own observer");
                    Check(!helper.HasExited,"waiting never kills the observer");
                    PetInstaller.WaitForInstalledProcesses(own,4000);Check(helper.HasExited,"installer resumes after owned observer exits");
                }finally{helper.WaitForExit(4000);}
            }
            using(var unrelated=Process.Start(new ProcessStartInfo{FileName=FileAt(other,"WeChatPet.exe"),Arguments="--hold",UseShellExecute=false,CreateNoWindow=true})) {
                try{PetInstaller.WaitForInstalledProcesses(own,100);Check(!unrelated.HasExited,"same-name process outside install directory is untouched");}finally{unrelated.WaitForExit(4000);}
            }
            Console.WriteLine(checks+" installer safety checks passed");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally {
            string root=Path.GetFullPath(scratch),testDirectory=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\','/');
            if(root.StartsWith(testDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("WeChatPet-InstallerTests-",StringComparison.Ordinal)&&Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
