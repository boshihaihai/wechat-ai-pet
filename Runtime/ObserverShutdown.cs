using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WeChatPet.Runtime {
    internal static class ObserverShutdown {
        internal static Task<bool> StopAsync(Process process) {return StopAsync(process,9000,2000,6500);}
        // Injectable durations let tests use only isolated synthetic children.
        internal static Task<bool> StopAsync(Process process,int gracefulMilliseconds,int killedMilliseconds,int guardianMilliseconds) {
            try {process.StandardInput.Close();}catch { }
            return Task.Run(async delegate {
                try {
                    if(!process.WaitForExit(gracefulMilliseconds)) {process.Kill();if(!process.WaitForExit(killedMilliseconds))return false;}
                    // Even an external kill may skip finally. The guardian must
                    // have time to expire its heartbeat before a new lease starts.
                    await Task.Delay(guardianMilliseconds).ConfigureAwait(false);
                    return true;
                }catch{return false;}finally{process.Dispose();}
            });
        }
    }
}
