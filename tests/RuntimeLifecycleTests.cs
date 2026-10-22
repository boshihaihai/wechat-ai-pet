using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using WeChatPet.Runtime;

// Isolated helper processes only. No desktop, WeChat, hooks, leases or clipboard.
internal static class RuntimeLifecycleTests {
    static readonly List<object> checks=new List<object>();
    static readonly string self=Assembly.GetExecutingAssembly().Location;
    static void Check(string name,bool passed) {checks.Add(new{name=name,passed=passed});if(!passed)throw new Exception(name);}
    static Process Start(string arguments) {
        return Process.Start(new ProcessStartInfo(self,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true});
    }
    static string Line(Process process) {
        var read=Task.Run(() => process.StandardOutput.ReadLine());
        if(!read.Wait(2000))throw new Exception("Synthetic process output timed out");
        return read.Result;
    }
    static bool Alive(int id) {try {using(var p=Process.GetProcessById(id))return !p.HasExited;}catch(ArgumentException){return false;}}
    static void CleanupSynthetic(int id) {
        try {using(var p=Process.GetProcessById(id))if(!p.HasExited&&String.Equals(p.MainModule.FileName,self,StringComparison.OrdinalIgnoreCase)){p.Kill();p.WaitForExit(2000);}}catch(ArgumentException) { }
    }
    static void ShutdownChecks() {
        int id=0;
        try {
            using(var parent=Start("--parent-noawait")) {
                id=Int32.Parse(Line(parent));Check("不等待清理的反例父进程先退出",parent.WaitForExit(3000));
                Thread.Sleep(1200);Check("不等待清理的反例遗留假观察器",Alive(id));
            }
        }finally {if(id!=0)CleanupSynthetic(id);}
        id=0;
        try {
            using(var parent=Start("--parent-awaited")) {
                id=Int32.Parse(Line(parent));Check("等待清理的父进程有界退出",parent.WaitForExit(4000)&&parent.ExitCode==0);
                Check("等待清理的假观察器已退出",!Alive(id));
            }
        }finally {if(id!=0)CleanupSynthetic(id);}
        var child=Start("--child-stall");id=child.Id;
        try {
            var watch=Stopwatch.StartNew();Task<bool> stopped=ObserverShutdown.StopAsync(child,700,1000,250);
            Check("停止任务立即返回且不阻塞调用线程",watch.ElapsedMilliseconds<300&&!stopped.IsCompleted);
            int ticks=0;while(!stopped.IsCompleted&&watch.ElapsedMilliseconds<4000){Thread.Sleep(20);ticks++;}
            Check("异步清理期间调用线程仍能处理事件",ticks>5&&stopped.IsCompleted&&stopped.Result);
            Check("终止卡住的假观察器后保留守护等待窗口",!Alive(id)&&watch.ElapsedMilliseconds>=900&&watch.ElapsedMilliseconds<4000);
        }finally {CleanupSynthetic(id);}
        child=Start("--child-graceful");id=child.Id;
        try {
            var watch=Stopwatch.StartNew();Task<bool> stopped=ObserverShutdown.StopAsync(child,1500,1000,250);
            Check("关闭输入后正常退出的假观察器完成清理",stopped.Wait(4000)&&stopped.Result&&!Alive(id)&&watch.ElapsedMilliseconds>=200);
        }finally {CleanupSynthetic(id);}
    }
    static void GuardianChecks() {
        using(var child=Start("--guardian-default")) {
            try {
                Check("假守护进程已进入等待",Line(child)=="ready");
                var watch=Stopwatch.StartNew();
                // Deliberately leave stdin open without sending a heartbeat.
                Check("管道保持打开时生产六秒超时仍能结束进程",child.WaitForExit(8500)&&child.ExitCode==0&&watch.ElapsedMilliseconds>=5500&&watch.ElapsedMilliseconds<8500);
                Check("超时经过清理分支且后台读取不保活进程",child.StandardOutput.ReadToEnd().Contains("synthetic-cleanup"));
            }finally {CleanupSynthetic(child.Id);}
        }
        using(var child=Start("--guardian-default")) {
            try {
                if(Line(child)!="ready")throw new Exception("Synthetic guardian not ready");
                var watch=Stopwatch.StartNew();child.StandardInput.Close();
                Check("正常EOF立即结束而不等待六秒",child.WaitForExit(2000)&&child.ExitCode==0&&watch.ElapsedMilliseconds<2000);
                Check("EOF也经过同一清理分支",child.StandardOutput.ReadToEnd().Contains("synthetic-cleanup"));
            }finally {CleanupSynthetic(child.Id);}
        }
        using(var child=Start("--guardian-short")) {
            try {
                if(Line(child)!="ready")throw new Exception("Synthetic guardian not ready");
                var watch=Stopwatch.StartNew();bool acknowledged=true;
                for(int i=0;i<3;i++) {Thread.Sleep(200);child.StandardInput.WriteLine("heartbeat");child.StandardInput.Flush();acknowledged&=Line(child)=="heartbeat";}
                Check("连续心跳可跨过最初超时期限保持守护",acknowledged&&!child.HasExited&&watch.ElapsedMilliseconds>=600);
                child.StandardInput.WriteLine("stop");child.StandardInput.Flush();
                Check("明确stop指令立即结束守护",child.WaitForExit(2000)&&child.ExitCode==0);
                Check("stop也经过同一清理分支",child.StandardOutput.ReadToEnd().Contains("synthetic-cleanup"));
            }finally {CleanupSynthetic(child.Id);}
        }
    }
    static int Parent(bool wait) {
        var child=Start("--child-stall");Console.WriteLine(child.Id);Console.Out.Flush();
        Task<bool> stopped=ObserverShutdown.StopAsync(child,700,1000,250);
        return wait?(stopped.GetAwaiter().GetResult()?0:1):0;
    }
    static int Guardian(bool productionTimeout) {
        try {
            Console.WriteLine("ready");Console.Out.Flush();
            while(productionTimeout?GuardianHeartbeat.WaitForNext(Console.In):GuardianHeartbeat.WaitForNext(Console.In,350)) {
                Console.WriteLine("heartbeat");Console.Out.Flush();
            }
            return 0;
        }finally {Console.WriteLine("synthetic-cleanup");Console.Out.Flush();}
    }
    public static int Main(string[] args) {
        if(args.Length>0&&args[0]=="--child-stall"){Thread.Sleep(15000);return 0;}
        if(args.Length>0&&args[0]=="--child-graceful"){while(Console.ReadLine()!=null){}return 0;}
        if(args.Length>0&&args[0]=="--parent-noawait")return Parent(false);
        if(args.Length>0&&args[0]=="--parent-awaited")return Parent(true);
        if(args.Length>0&&args[0]=="--guardian-default")return Guardian(true);
        if(args.Length>0&&args[0]=="--guardian-short")return Guardian(false);
        try {
            ShutdownChecks();GuardianChecks();
            Console.WriteLine(new JavaScriptSerializer().Serialize(new {passed=checks.Count,checks=checks,wechatInteractions=0,realClipboardAccess=0,networkCalls=0,syntheticChildProcessesOnly=true}));return 0;
        }catch(Exception e) {Console.WriteLine(new JavaScriptSerializer().Serialize(new {passed=checks.Count-1,error=e.ToString(),checks=checks}));return 1;}
    }
}
