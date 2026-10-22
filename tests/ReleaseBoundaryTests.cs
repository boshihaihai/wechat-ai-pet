using System;
using System.IO;
using System.Linq;
using System.Reflection;

// Inspect the actual production binaries, not the regression-test assembly.
// No application entry point, UI, credential access or observer is invoked.
static class ReleaseBoundaryTests {
    static int Main() {
        try {
            int checks=0;
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve+=delegate(object sender,ResolveEventArgs args){return Assembly.ReflectionOnlyLoad(args.Name);};
            foreach(string file in new[]{"WeChatPet.exe","WeChatObserver.exe"}) {
                // Metadata only: do not execute or activate application code,
                // and do not change Windows' downloaded-file trust policy.
                var assembly=Assembly.ReflectionOnlyLoad(File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,file)));
                var types=assembly.GetTypes();
                if(types.Any(t=>t.Name.EndsWith("Tests",StringComparison.Ordinal)||t.Name=="PetTestMain"))throw new Exception(file+" contains test code");
                checks++;
                if(types.Any(t=>t.Name.StartsWith("AutoReply",StringComparison.Ordinal)))throw new Exception(file+" contains removed automatic sending code");
                checks++;
                if(assembly.GetType("WeChatPet.Runtime.DesktopRuntime")==null||assembly.GetType("WeChatPet.Runtime.ObserverProgram")==null)throw new Exception(file+" missing runtime");
                checks++;
            }
            Console.WriteLine("PASS release boundaries: "+checks+" checks; no UI, network, or message actions");return 0;
        } catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
}
