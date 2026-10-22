using System;

namespace WeChatPet.Runtime {
    // Diagnostics are optional. Failure must not stop recommendations, but a
    // stale on-disk record must not be presented as a successful current write.
    internal sealed class DiagnosticsWriteStatus {
        internal string ErrorCode {get;private set;}
        internal long LastSuccessfulTicks {get;private set;}
        internal int ConsecutiveFailures {get;private set;}
        internal bool TryWrite(Action write,long nowTicks) {
            try {
                write();LastSuccessfulTicks=nowTicks;ErrorCode=null;ConsecutiveFailures=0;return true;
            } catch(Exception error) {
                ErrorCode=error.GetType().Name;
                if(ConsecutiveFailures<int.MaxValue)ConsecutiveFailures++;
                return false;
            }
        }
    }
}
