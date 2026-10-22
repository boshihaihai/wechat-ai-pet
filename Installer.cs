using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

public static class PetInstaller {
    static string Target {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","WeChatPet");}}
    static string Shortcut {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"微信 AI 桌宠.lnk");}}
    const string RegistryPath="Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\WeChatPet.Personal";
    const string Marker=".wechat-pet-install",MarkerValue="WeChatPet.Personal.v1";
    static readonly object MaintenanceSync=new object();
    static readonly HashSet<string> ActiveMaintenance=new HashSet<string>(StringComparer.Ordinal);
    internal sealed class MaintenanceBusyException : IOException {
        internal MaintenanceBusyException():base("此目录已有安装或卸载正在进行，请稍后重试。") { }
    }
    sealed class MaintenanceLease : IDisposable {
        readonly string name;
        Mutex mutex;
        internal MaintenanceLease(string name,Mutex mutex){this.name=name;this.mutex=mutex;}
        public void Dispose() {
            if(mutex==null)return;
            try{mutex.ReleaseMutex();}finally{mutex.Dispose();mutex=null;lock(MaintenanceSync)ActiveMaintenance.Remove(name);}
        }
    }
    static IDisposable BeginMaintenance(string root) {
        string identity;
        using(var current=WindowsIdentity.GetCurrent())identity=current.User.Value;
        string name;
        using(var hash=SHA256.Create())name="Global\\WeChatPet.Maintenance."+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity+"\n"+root.ToUpperInvariant()))).Replace("-","");
        // Named mutexes are reentrant on their owning thread. Refuse nested calls
        // explicitly, as well as competing threads, before taking the OS mutex.
        lock(MaintenanceSync){if(!ActiveMaintenance.Add(name))throw new MaintenanceBusyException();}
        Mutex mutex=null;
        try {
            mutex=new Mutex(false,name);
            bool acquired;
            try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
            if(!acquired)throw new MaintenanceBusyException();
            return new MaintenanceLease(name,mutex);
        }catch{if(mutex!=null)mutex.Dispose();lock(MaintenanceSync)ActiveMaintenance.Remove(name);throw;}
    }
    [STAThread]public static int Main(string[] args) {
        try {
            if(args.Length==3&&args[0]=="--complete-uninstall")return CompleteUninstall(Int32.Parse(args[1]),Int64.Parse(args[2]));
            if(args.Contains("--uninstall"))return Uninstall();
            bool silent=args.Contains("--silent");
            if(!silent&&MessageBox.Show("将为当前用户安装微信 AI 桌宠。", "微信 AI 桌宠安装",MessageBoxButtons.OKCancel)!=DialogResult.OK)return 0;
            string root=ValidateDirectory(Target,Target);
            using(BeginMaintenance(root)) {
                WaitForInstalledProcesses(root,12000);
                using(var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                    InstallPayloadCore(root,payload,Application.ExecutablePath,null);
                Type shellType=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(shellType);
                object shortcut=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{Shortcut});
                shortcut.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{Path.Combine(root,"WeChatPet.exe")});
                shortcut.GetType().InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{root});
                shortcut.GetType().InvokeMember("IconLocation",BindingFlags.SetProperty,null,shortcut,new object[]{Path.Combine(root,"assets","wechat-pet","brand","wechat-pet.ico")});
                shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
                using(var key=Registry.CurrentUser.CreateSubKey(RegistryPath)) {
                    key.SetValue("DisplayName","微信 AI 桌宠（个人原型）");key.SetValue("DisplayVersion","0.7.1");
                    key.SetValue("InstallLocation",root);key.SetValue("UninstallString","\""+Path.Combine(root,"Uninstall.exe")+"\" --uninstall");
                    key.SetValue("DisplayIcon",Path.Combine(root,"WeChatPet.exe"));
                    key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
                }
                if(!silent)Process.Start(Path.Combine(root,"WeChatPet.exe"));
            }
            return 0;
        }catch(Exception e){if(args.Contains("--silent"))Console.Error.WriteLine(e.ToString());else MessageBox.Show(e.Message,"安装未完成");return 1;}
    }
    static int Uninstall() {
        ValidateDirectory(Target,Target);CheckMarker(Target);
        if(MessageBox.Show("卸载桌宠程序？个人设置和加密凭证将保留。", "卸载微信 AI 桌宠",MessageBoxButtons.OKCancel)!=DialogResult.OK)return 0;
        using(BeginMaintenance(ValidateDirectory(Target,Target))) {
            CheckMarker(Target);WaitForInstalledProcesses(Target,12000);
            string helper=Path.Combine(Path.GetTempPath(),"WeChatPet-Uninstall-"+Guid.NewGuid().ToString("N")+".exe");
            File.Copy(Application.ExecutablePath,helper);
            try {
                using(var current=Process.GetCurrentProcess())Process.Start(new ProcessStartInfo{FileName=helper,Arguments="--complete-uninstall "+current.Id+" "+current.StartTime.ToUniversalTime().Ticks,UseShellExecute=false,CreateNoWindow=true});
            }catch{File.Delete(helper);throw;}
        }
        return 0;
    }
    static int CompleteUninstall(int parentId,long parentBirth) {
        try {
            try {using(var parent=Process.GetProcessById(parentId))if(parent.StartTime.ToUniversalTime().Ticks==parentBirth&&!parent.WaitForExit(12000))throw new IOException("卸载器尚未退出，请稍后重试。");}catch(ArgumentException) { }
            string root=ValidateDirectory(Target,Target);
            using(BeginMaintenance(root)) {
                WaitForInstalledProcesses(root,12000);
                RemoveThenUnregisterCore(root,Application.ExecutablePath,delegate {
                    if(File.Exists(Shortcut))File.Delete(Shortcut);
                    Registry.CurrentUser.DeleteSubKeyTree(RegistryPath,false);
                },null);
            }
            return 0;
        }catch(Exception e){
            MessageBox.Show(e.Message+"\n未完成卸载，可从原卸载入口重试。","卸载未完成");return 1;
        }
        finally{ScheduleHelperCleanup();}
    }
    internal static string ValidateDirectory(string directory,string expected) {
        string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string wanted=Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(!String.Equals(root,wanted,StringComparison.OrdinalIgnoreCase)||String.Equals(root,Path.GetPathRoot(root).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new IOException("安装目录不在允许的位置。");
        if(Directory.Exists(root)&&(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("安装目录不能是链接目录。");
        return root;
    }
    internal static string ChildPath(string root,string relative) {
        if(Path.IsPathRooted(relative))throw new IOException("安装资源路径无效。");
        string path=Path.GetFullPath(Path.Combine(root,relative));
        if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||relative.IndexOf(':')>=0)throw new IOException("安装资源路径无效。");
        for(string part=path;part!=null&&!String.Equals(part,root,StringComparison.OrdinalIgnoreCase);part=Path.GetDirectoryName(part))
            if((File.Exists(part)||Directory.Exists(part))&&(File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("安装资源不能经过链接。");
        return path;
    }
    internal static void WaitForInstalledProcesses(string directory,int timeoutMilliseconds) {
        string root=ValidateDirectory(directory,directory);
        var paths=new Dictionary<string,string>{{"WeChatPet",Path.Combine(root,"WeChatPet.exe")},{"WeChatObserver",Path.Combine(root,"WeChatObserver.exe")},{"node",Path.Combine(root,"runtime","node.exe")}};
        var watch=Stopwatch.StartNew();
        while(true) {
            bool found=false;
            foreach(var pair in paths)foreach(var process in Process.GetProcessesByName(pair.Key))using(process) {
                try {if(String.Equals(process.MainModule.FileName,pair.Value,StringComparison.OrdinalIgnoreCase))found=true;}
                catch(InvalidOperationException) { }
                catch(NullReferenceException) { /* MainModule can disappear while a process starts/exits. */ }
                catch(System.ComponentModel.Win32Exception) { /* Unrelated inaccessible processes do not block this installation. */ }
            }
            if(!found)return;
            if(watch.ElapsedMilliseconds>=timeoutMilliseconds)throw new IOException("请先退出桌宠，等待后台组件结束后重试。");
            Thread.Sleep(100);
        }
    }
    sealed class InstallFile {internal string Destination,Staged,Backup;internal bool Existed,Committed;}
    internal static void InstallPayload(string directory,string expected,Stream payload,string installer,Action<int> beforeCommit=null) {
        string root=ValidateDirectory(directory,expected);
        using(BeginMaintenance(root))InstallPayloadCore(root,payload,installer,beforeCommit);
    }
    static void InstallPayloadCore(string root,Stream payload,string installer,Action<int> beforeCommit) {
        ValidateDirectory(root,root);Directory.CreateDirectory(root);
        string stage=ChildPath(root,".install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        var files=new List<InstallFile>();bool preserveBackup=false;
        try {
            string next=ChildPath(stage,"new"),backup=ChildPath(stage,"backup");Directory.CreateDirectory(next);Directory.CreateDirectory(backup);
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var archive=new ZipArchive(payload,ZipArchiveMode.Read,true))foreach(var entry in archive.Entries) {
                string destination=ChildPath(root,entry.FullName),staged=ChildPath(next,entry.FullName);
                if(entry.Name.Length==0){Directory.CreateDirectory(staged);continue;}
                if(!names.Add(destination)||String.Equals(entry.FullName,"Uninstall.exe",StringComparison.OrdinalIgnoreCase)||String.Equals(entry.FullName,Marker,StringComparison.OrdinalIgnoreCase))throw new IOException("安装资源重复或无效。");
                Directory.CreateDirectory(Path.GetDirectoryName(staged));using(var input=entry.Open())using(var output=File.Create(staged))input.CopyTo(output);
                files.Add(new InstallFile{Destination=destination,Staged=staged});
            }
            string stagedInstaller=ChildPath(next,"Uninstall.exe");File.Copy(installer,stagedInstaller);
            files.Add(new InstallFile{Destination=ChildPath(root,"Uninstall.exe"),Staged=stagedInstaller});
            string stagedMarker=ChildPath(next,Marker);File.WriteAllText(stagedMarker,MarkerValue);
            files.Add(new InstallFile{Destination=ChildPath(root,Marker),Staged=stagedMarker});
            // Complete every read/decompression/write-access check before replacing anything.
            for(int i=0;i<files.Count;i++) {
                var file=files[i];file.Backup=ChildPath(backup,i.ToString());file.Existed=File.Exists(file.Destination);
                if(Directory.Exists(file.Destination))throw new IOException("安装文件与现有目录冲突。");
                Directory.CreateDirectory(Path.GetDirectoryName(file.Destination));
                if(file.Existed)using(var input=new FileStream(file.Destination,FileMode.Open,FileAccess.ReadWrite,FileShare.None))using(var output=File.Create(file.Backup))input.CopyTo(output);
                else {using(new FileStream(file.Destination,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){}File.Delete(file.Destination);}
            }
            try {
                for(int i=0;i<files.Count;i++) {
                    var file=files[i];if(beforeCommit!=null)beforeCommit(i);
                    if(file.Existed)File.Replace(file.Staged,file.Destination,null);else File.Move(file.Staged,file.Destination);
                    file.Committed=true;
                }
            }catch(Exception failure) {
                var rollbackErrors=new List<Exception>();
                foreach(var file in files.Where(f=>f.Committed).Reverse())try {
                    if(file.Existed) {if(File.Exists(file.Destination))File.Replace(file.Backup,file.Destination,null);else File.Move(file.Backup,file.Destination);}
                    else if(File.Exists(file.Destination))File.Delete(file.Destination);
                }catch(Exception e){rollbackErrors.Add(e);}
                if(rollbackErrors.Count>0){preserveBackup=true;throw new IOException("更新未完成，恢复文件仍被占用。备份保留于："+stage,failure);}
                throw new IOException("更新未完成，原版本已恢复。请关闭占用文件后重试。",failure);
            }
        }finally{if(!preserveBackup&&Directory.Exists(stage)){ValidateDirectory(stage,ChildPath(root,Path.GetFileName(stage)));Directory.Delete(stage,true);}}
    }
    static void CheckMarker(string root) {if(!File.Exists(ChildPath(root,Marker))||File.ReadAllText(ChildPath(root,Marker))!=MarkerValue)throw new IOException("没有找到本应用安装标记。");}
    static List<string> ProgramFiles(string root,string directory) {
        var result=new List<string>();
        foreach(string file in Directory.GetFiles(directory)){ChildPath(root,file.Substring(root.Length+1));result.Add(file);}
        foreach(string child in Directory.GetDirectories(directory)){ChildPath(root,child.Substring(root.Length+1));result.AddRange(ProgramFiles(root,child));}
        return result;
    }
    internal static void RemoveProgramDirectory(string directory,string expected,string recoveryInstaller,Action<int> beforeDelete=null) {
        string root=ValidateDirectory(directory,expected);
        using(BeginMaintenance(root))RemoveProgramDirectoryCore(root,recoveryInstaller,beforeDelete);
    }
    static void RemoveProgramDirectoryCore(string root,string recoveryInstaller,Action<int> beforeDelete) {
        ValidateDirectory(root,root);CheckMarker(root);
        string recovery=Path.GetFullPath(recoveryInstaller);
        if(!File.Exists(recovery)||recovery.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("卸载恢复文件无效。");
        var files=ProgramFiles(root,root).OrderBy(path=>String.Equals(path,Path.Combine(root,"Uninstall.exe"),StringComparison.OrdinalIgnoreCase)||String.Equals(path,Path.Combine(root,Marker),StringComparison.OrdinalIgnoreCase)?1:0).ToList();
        foreach(string file in files)using(new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { }
        try {
            for(int i=0;i<files.Count;i++){if(beforeDelete!=null)beforeDelete(i);File.Delete(files[i]);}
            ValidateDirectory(root,root);Directory.Delete(root,true);
            if(Directory.Exists(root))throw new IOException("程序目录仍被占用。");
        }catch {
            // Partial deletion must not strand the registered uninstall entry.
            RestoreUninstallEntry(ValidateDirectory(root,root),recovery);
            throw;
        }
    }
    static void RestoreUninstallEntry(string root,string recovery) {
        Directory.CreateDirectory(root);
        if(!File.Exists(ChildPath(root,"Uninstall.exe")))File.Copy(recovery,ChildPath(root,"Uninstall.exe"));
        if(!File.Exists(ChildPath(root,Marker)))File.WriteAllText(ChildPath(root,Marker),MarkerValue);
    }
    internal static void RemoveThenUnregister(string directory,string expected,string recoveryInstaller,Action unregister,Action<int> beforeDelete=null) {
        string root=ValidateDirectory(directory,expected);
        using(BeginMaintenance(root))RemoveThenUnregisterCore(root,recoveryInstaller,unregister,beforeDelete);
    }
    static void RemoveThenUnregisterCore(string root,string recoveryInstaller,Action unregister,Action<int> beforeDelete) {
        RemoveProgramDirectoryCore(root,recoveryInstaller,beforeDelete);
        // Keep the original entry until all program files are confirmed gone.
        try{unregister();}catch{RestoreUninstallEntry(ValidateDirectory(root,root),recoveryInstaller);throw;}
    }
    static void ScheduleHelperCleanup() {
        try {
            string path=Path.GetFullPath(Application.ExecutablePath),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\','/');
            if(!path.StartsWith(temp+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(path).StartsWith("WeChatPet-Uninstall-",StringComparison.Ordinal))return;
            string script="Wait-Process -Id "+Process.GetCurrentProcess().Id+" -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '"+path.Replace("'","''")+"' -Force -ErrorAction SilentlyContinue";
            Process.Start(new ProcessStartInfo{FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell\\v1.0\\powershell.exe"),Arguments="-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),UseShellExecute=false,CreateNoWindow=true});
        }catch { /* A cleanup failure leaves only this temporary helper, never user data. */ }
    }
}
