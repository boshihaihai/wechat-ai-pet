using System;

namespace WeChatPet.Runtime {
    // A fill runs on the observer's read worker, so a missing periodic snapshot
    // is expected briefly. This lease only defers DesktopRuntime's synthetic
    // stale observation; real observations and every explicit invalidation pass
    // through normally. Time is injected for tests and monotonic in production.
    internal sealed class FillObservationPolicy {
        internal const long MaximumDurationTicks=TimeSpan.TicksPerMillisecond*4500;
        string activeId,lastStartedId,account,friend,group;
        long window,deadline,latestElapsed;

        internal bool Begin(string id,long targetWindow,long startedTicks,Recommendation ready,ChatSnapshot snapshot,bool enabled,long now,long elapsedTicks) {
            if(!enabled||activeId!=null||String.IsNullOrEmpty(id)||id==lastStartedId||ready==null||ready.Id!=id||
               targetWindow==0||snapshot==null||snapshot.Window!=targetWindow||!Matches(snapshot,ready)||
               !snapshot.Foreground||!snapshot.Focused||!snapshot.ImeAllowed||!RecommendationFresh(ready,now)||
               startedTicks<=0||now<startedTicks||now-startedTicks>TimeSpan.TicksPerSecond||
               snapshot.CapturedTicks<=0||startedTicks<snapshot.CapturedTicks||now-snapshot.CapturedTicks>TimeSpan.TicksPerSecond||elapsedTicks<0)return false;
            long remaining=MaximumDurationTicks-(now-startedTicks);
            if(elapsedTicks>long.MaxValue-remaining)return false;
            activeId=lastStartedId=id;window=targetWindow;
            account=snapshot.AccountId;friend=snapshot.FriendId;group=snapshot.GroupId;
            latestElapsed=elapsedTicks;deadline=elapsedTicks+remaining;
            return true;
        }

        internal bool DeferStaleObservation(Recommendation ready,ChatSnapshot snapshot,bool enabled,long now,long elapsedTicks) {
            if(activeId==null)return false;
            if(!enabled||elapsedTicks<latestElapsed||elapsedTicks>=deadline||ready==null||ready.Id!=activeId||
               !RecommendationFresh(ready,now)||snapshot==null||snapshot.Window!=window||!Matches(snapshot,ready)||
               snapshot.AccountId!=account||snapshot.FriendId!=friend||snapshot.GroupId!=group) {
                Cancel();return false;
            }
            latestElapsed=elapsedTicks;return true;
        }

        internal bool End(string id) {
            if(activeId==null||id!=activeId)return false;
            Cancel();return true;
        }

        internal void Cancel() {
            activeId=null;window=deadline=latestElapsed=0;account=friend=group=null;
            // Keep the consumed id: duplicate begin/end messages cannot revive
            // or renew a lease even if the controller has not cleared Ready yet.
        }

        static bool RecommendationFresh(Recommendation ready,long now) {
            return now>=ready.CreatedTicks&&now-ready.CreatedTicks<=TimeSpan.TicksPerSecond*60;
        }
        static bool Matches(ChatSnapshot snapshot,Recommendation ready) {
            return snapshot.Available&&snapshot.AtEnd&&snapshot.DraftEmpty&&snapshot.DraftConsistent&&!snapshot.Composing&&
                snapshot.GroupChat==ready.GroupChat&&snapshot.AccountId==ready.AccountId&&snapshot.GroupId==ready.GroupId&&
                snapshot.Session==ready.Session&&snapshot.Tail==ready.Tail&&snapshot.ContentRevision==ready.ContentRevision&&
                snapshot.InputIdentity==ready.InputIdentity&&snapshot.Activity==ready.Activity;
        }
    }
}
