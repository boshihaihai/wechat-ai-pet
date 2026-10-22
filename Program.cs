using System;
using System.IO;
using System.Windows;
using WeChatPet.Runtime;
namespace WeChatPet {
 public static class Program {
  [STAThread] public static int Main(string[] args) {
   StartupOptions options;
   if(!StartupOptions.TryParse(args,out options)){Console.Error.WriteLine("unsupported_argument");return 1;}
   string home=AppDomain.CurrentDomain.BaseDirectory;
   DesktopRuntime runtime=null;TrayIcon tray=null;
   try {
   Directory.CreateDirectory(LocalStore.Data);
   using(var instance=SingleInstanceActivation.CurrentUser()) {
    if(!instance.IsPrimary){instance.SignalExisting(!options.StartInTray);return 0;}
     var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
     var pet=new PetWindow(home,Path.Combine(LocalStore.Data,"appearance.json"));
     if(options.Inspect)pet.ShowInTaskbar=true;
     bool exiting=false;
     Action exit=async delegate {
      if(exiting)return;exiting=true;
      pet.IsEnabled=false;
      try {
       if(tray!=null)tray.Dispose();
       if(runtime!=null) {runtime.Dispose();await runtime.ShutdownCompletion;}
      } finally {pet.CloseForExit();app.Shutdown();}
     };
     pet.QuitRequested+=exit;pet.Closed+=delegate{exit();};
     app.Startup+=delegate{
      tray=new TrayIcon(pet,delegate{return runtime;},exit);pet.Show();if(!options.Preview)runtime=new DesktopRuntime(pet,home);if(options.StartInTray)pet.HideToTray();
      instance.Listen(delegate{if(!app.Dispatcher.HasShutdownStarted)app.Dispatcher.BeginInvoke(new Action(delegate{if(!exiting)pet.RestorePet();}));});
     };
     // Windows session termination cannot promise an async grace period. Do not
     // cancel logoff; the observer/guardian still receive their normal stop signal.
     app.SessionEnding+=delegate{exit();};app.Run();return 0;
   }
   }catch(Exception e) {
    try{if(tray!=null)tray.Dispose();}catch{ }
    try{if(runtime!=null)runtime.Dispose();}catch{ }
    // A blocked data directory may be the original failure. Diagnostics must
    // not throw again and hide the actionable startup message.
    try{File.WriteAllText(Path.Combine(LocalStore.Data,"startup-error.txt"),e.GetType().Name);}catch{ }
    MessageBox.Show("桌宠启动未完成，请检查安装文件及个人数据目录是否可访问。","微信 AI 桌宠");return 1;
   }
  }
 }
}
