using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace WeChatPet.Runtime {
    internal sealed class StartupOptions {
        internal bool Inspect, Preview, StartInTray;
        internal static bool TryParse(string[] args,out StartupOptions options) {
            options=new StartupOptions();
            if(args.Length==0)return true;
            if(args.Length!=1)return false;
            switch(args[0]) {
                case "--inspect-ui":options.Inspect=true;return true;
                case "--preview":options.Preview=true;return true;
                case "--start-in-tray":options.StartInTray=true;return true;
                default:return false;
            }
        }
    }

    // Both objects live in the caller's Windows session and grant only its user
    // access. Create the event before the mutex so an early second launch can
    // leave a restore request while the first launch is still constructing UI.
    internal sealed class SingleInstanceActivation : IDisposable {
        readonly object gate=new object();
        readonly EventWaitHandle activate;
        readonly Mutex mutex;
        RegisteredWaitHandle registration;
        bool disposed;
        internal bool IsPrimary {get;private set;}
        internal static SingleInstanceActivation CurrentUser() {
            var user=WindowsIdentity.GetCurrent().User;
            return new SingleInstanceActivation("Local\\WeChatPet.Personal."+user.Value,user);
        }
        internal SingleInstanceActivation(string name,SecurityIdentifier user) {
            var events=new EventWaitHandleSecurity();events.SetAccessRuleProtection(true,false);
            events.AddAccessRule(new EventWaitHandleAccessRule(user,EventWaitHandleRights.FullControl,AccessControlType.Allow));
            var mutexes=new MutexSecurity();mutexes.SetAccessRuleProtection(true,false);
            mutexes.AddAccessRule(new MutexAccessRule(user,MutexRights.FullControl,AccessControlType.Allow));
            bool created;
            activate=new EventWaitHandle(false,EventResetMode.AutoReset,name+".Activate",out created,events);
            try {mutex=new Mutex(true,name,out created,mutexes);IsPrimary=created;}
            catch {activate.Dispose();throw;}
        }
        internal void SignalExisting(bool restore) {
            lock(gate){if(disposed)throw new ObjectDisposedException("SingleInstanceActivation");if(!IsPrimary&&restore)activate.Set();}
        }
        internal void Listen(Action restore) {
            if(restore==null)throw new ArgumentNullException("restore");
            lock(gate) {
                if(disposed)throw new ObjectDisposedException("SingleInstanceActivation");
                if(!IsPrimary||registration!=null)throw new InvalidOperationException("activation_listener_state");
                registration=ThreadPool.RegisterWaitForSingleObject(activate,delegate {
                    lock(gate){if(!disposed)restore();}
                },null,Timeout.Infinite,false);
            }
        }
        public void Dispose() {
            lock(gate) {
                if(disposed)return;disposed=true;
                if(registration!=null){registration.Unregister(null);registration=null;}
                activate.Dispose();if(IsPrimary)mutex.ReleaseMutex();mutex.Dispose();
            }
        }
    }
}
