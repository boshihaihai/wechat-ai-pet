using System;
using System.Security.Principal;
using System.Threading;
using WeChatPet.Runtime;

// Isolated named objects only: never starts the pet or touches its real mutex.
public static class SingleInstanceActivationTests {
    static int checks;
    static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    public static int Main() {
        string name="Local\\WeChatPet.ActivationTest."+Guid.NewGuid().ToString("N");
        var user=WindowsIdentity.GetCurrent().User;
        try {
            StartupOptions options;
            Check(StartupOptions.TryParse(new string[0],out options)&&!options.StartInTray,"normal startup requests a visible pet");
            Check(StartupOptions.TryParse(new[]{"--start-in-tray"},out options)&&options.StartInTray,"background startup is explicitly distinguished");
            Check(!StartupOptions.TryParse(new[]{"--unsupported"},out options)&&!StartupOptions.TryParse(new[]{"--preview","--start-in-tray"},out options),"invalid arguments are rejected before instance activation");
            using(var arrived=new AutoResetEvent(false)) {
                int activations=0;
                using(var primary=new SingleInstanceActivation(name,user)) {
                    Check(primary.IsPrimary,"first isolated instance owns its mutex");
                    using(var secondary=new SingleInstanceActivation(name,user)) {
                        Check(!secondary.IsPrimary,"second isolated instance cannot become primary");
                        secondary.SignalExisting(true);
                        primary.Listen(delegate{Interlocked.Increment(ref activations);arrived.Set();});
                        Check(arrived.WaitOne(2000)&&activations==1,"activation sent during primary startup is retained until listener is ready");
                        secondary.SignalExisting(false);
                        Check(!arrived.WaitOne(150)&&activations==1,"background relaunch does not restore or activate UI");
                        secondary.SignalExisting(true);
                        Check(arrived.WaitOne(2000)&&activations==2,"ordinary relaunch requests restoration of existing UI");
                    }
                    using(var unrelated=new SingleInstanceActivation(name+".OtherSessionOrUser",user)) {
                        Check(unrelated.IsPrimary,"distinct scoped identity has independent ownership");
                        unrelated.SignalExisting(true);
                        Check(!arrived.WaitOne(150)&&activations==2,"different scoped instance cannot signal this listener");
                    }
                }
                using(var restarted=new SingleInstanceActivation(name,user)) {
                    Check(restarted.IsPrimary,"dispose releases mutex and all primary handles for clean restart");
                    restarted.Listen(delegate{Interlocked.Increment(ref activations);arrived.Set();});
                    Check(!arrived.WaitOne(150)&&activations==2,"clean restart inherits no stale activation request");
                    using(var secondary=new SingleInstanceActivation(name,user))secondary.SignalExisting(true);
                    Check(arrived.WaitOne(2000)&&activations==3,"new listener receives exactly one request after restart");
                }
            }
            Console.WriteLine(checks+" single instance activation checks passed");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
