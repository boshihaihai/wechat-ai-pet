using System;

namespace WeChatPet.Runtime {
    // Fill outcomes outlive periodic reads. A native shortcut fault belongs to
    // the observer process; an ordinary failed attempt belongs to one chat/input.
    internal sealed class FillNoticePolicy {
        string restartMessage,attemptMessage,session,tail,account,friend,group;
        long activity;
        bool hasContext,awaitingSnapshot;
        internal string Message {get{return restartMessage??attemptMessage;}}
        internal bool RequiresObserverRestart {get{return restartMessage!=null;}}

        internal void Record(bool native,bool verified,string reason,string clipboard,ChatSnapshot snapshot,bool attempted=false) {
            ClearAttempt();
            if(verified)return;
            if(!native) {
                // Losing focus or typing before SetValue is an ordinary cancellation.
                // A write that actually started must keep its unconfirmed outcome
                // visible, without rebinding the notice to a different conversation.
                if(!attempted||snapshot==null||!snapshot.Available)return;
                attemptMessage="未确认文字填入，请检查草稿";Bind(snapshot);return;
            }
            if(reason=="shortcut_key_release_unconfirmed"||reason=="shortcut_recovery_blocked"||reason=="shortcut_marker_capacity") {
                restartMessage="表情填入已暂停，请检查草稿并重启桌宠；普通文字仍可填入";
                return;
            }
            attemptMessage=clipboard!=null&&(clipboard.Contains("not_restored")||clipboard.Contains("restore_failed"))
                ?"未确认表情填入，剪贴板恢复未完成，请检查草稿"
                :reason=="native_clipboard_unavailable"?"剪贴板暂不可用，未填入表情"
                :reason!=null&&reason.StartsWith("native_ime_",StringComparison.Ordinal)?"输入法状态未确认，未填入表情"
                :"未确认表情转换，请检查草稿";
            awaitingSnapshot=true;Bind(snapshot);
        }
        internal void Observe(ChatSnapshot snapshot) {
            if(attemptMessage==null||snapshot==null||!snapshot.Available)return;
            // Current can predate the whole fill. The first later read may show
            // the very switch/input that caused failure; it must not erase the
            // newly reported failure before the user has had time to see it.
            if(awaitingSnapshot||!hasContext) {Bind(snapshot);awaitingSnapshot=false;return;}
            // Draft appearance can be the result being verified, not a user edit.
            // Only a new input epoch or conversation/message change dismisses it.
            if(snapshot.Session!=session||snapshot.Tail!=tail||snapshot.AccountId!=account||snapshot.FriendId!=friend||snapshot.GroupId!=group||snapshot.Activity!=activity)ClearAttempt();
        }
        void Bind(ChatSnapshot snapshot) {
            hasContext=snapshot!=null&&snapshot.Available;
            if(hasContext) {session=snapshot.Session;tail=snapshot.Tail;account=snapshot.AccountId;friend=snapshot.FriendId;group=snapshot.GroupId;activity=snapshot.Activity;}
        }
        internal void ClearAttempt() {attemptMessage=session=tail=account=friend=group=null;activity=0;hasContext=awaitingSnapshot=false;}
        internal void ObserverStarted() {restartMessage=null;ClearAttempt();}
    }
}
