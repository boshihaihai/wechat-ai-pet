using System;
using System.IO;
using System.Threading.Tasks;

namespace WeChatPet.Runtime {
    internal static class GuardianHeartbeat {
        internal const int TimeoutMilliseconds=6000;
        internal static bool WaitForNext(TextReader input) {return WaitForNext(input,TimeoutMilliseconds);}
        internal static bool WaitForNext(TextReader input,int timeoutMilliseconds) {
            if(input==null)throw new ArgumentNullException("input");
            if(timeoutMilliseconds<=0)throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            // Console.In is a synchronized TextReader on .NET Framework: its
            // ReadLineAsync can block before returning a Task. Start the blocking
            // read on a background worker so the guardian's timeout can run.
            // On timeout the caller must exit; the remaining read is background
            // work and must not keep that process or its lease alive.
            Task<string> read=Task.Run(() => input.ReadLine());
            return read.Wait(timeoutMilliseconds)&&read.Result!=null&&read.Result!="stop";
        }
    }
}
