using System;

namespace WeChatPet.Runtime {
    internal static class FillNoticePolicyTests {
        static ChatSnapshot Snapshot() {return new ChatSnapshot {Available=true,Session="synthetic-session",Tail="synthetic-tail",AccountId="synthetic-account",FriendId="synthetic-friend",Activity=3,DraftEmpty=true};}
        internal static void Run(Action<string,bool> check) {
            foreach(string reason in new[]{"shortcut_key_release_unconfirmed","shortcut_recovery_blocked","shortcut_marker_capacity"}) {
                var policy=new FillNoticePolicy();policy.Record(true,false,reason,"unused",Snapshot());
                string message=policy.Message;var changed=Snapshot();changed.Activity++;changed.Session="another-session";
                policy.Observe(changed);policy.ClearAttempt();policy.Record(false,true,null,"unused",changed);
                check("表情永久故障跨快照与普通文字成功仍保留重启提示："+reason,policy.RequiresObserverRestart&&policy.Message==message&&message.Contains("普通文字仍可填入"));
                policy.ObserverStarted();check("新的观察器清除其前任的表情故障："+reason,policy.Message==null&&!policy.RequiresObserverRestart);
            }
            foreach(string reason in new[]{"native_clipboard_unavailable","native_ime_unconfirmed","native_verify_content_mismatch"}) {
                var policy=new FillNoticePolicy();var s=Snapshot();policy.Record(true,false,reason,"unused",s);string message=policy.Message;
                s.DraftEmpty=false;policy.Observe(s);policy.Observe(new ChatSnapshot{Available=false});policy.Observe(s);
                check("单次表情失败不被例行读取或自身草稿出现覆盖："+reason,policy.Message==message&&!policy.RequiresObserverRestart);
                s.Activity++;policy.Observe(s);check("用户继续编辑清除单次表情提示："+reason,policy.Message==null);
            }
            foreach(string changed in new[]{"session","tail","account","friend","group"}) {
                var policy=new FillNoticePolicy();policy.Record(true,false,"native_verify_context_changed","unverified_isolated_not_restored",Snapshot());
                check("剪贴板未恢复给出持续的具体提示："+changed,policy.Message.Contains("剪贴板恢复未完成"));
                policy.Observe(Snapshot());
                var s=Snapshot();if(changed=="session")s.Session="changed";else if(changed=="tail")s.Tail="changed";else if(changed=="account")s.AccountId="changed";else if(changed=="friend")s.FriendId="changed";else s.GroupId="changed";
                policy.Observe(s);check("单次失败不串到新消息或其他账号聊天："+changed,policy.Message==null);
            }
            var switched=new FillNoticePolicy();switched.Record(true,false,"native_verify_context_changed","unverified_isolated_not_restored",Snapshot());
            var newChat=Snapshot();newChat.Session="changed-during-fill";newChat.Activity++;
            switched.Observe(newChat);switched.Observe(newChat);
            check("导致填入失败的聊天切换不在首个后续快照擦除错误",switched.Message!=null&&switched.Message.Contains("剪贴板恢复未完成"));
            newChat.Activity++;switched.Observe(newChat);
            check("失败显示后的新输入可以清除单次提示",switched.Message==null);
            var temporary=new FillNoticePolicy();temporary.Record(true,false,"native_clipboard_unavailable","unused",Snapshot());temporary.ClearAttempt();
            check("新的生成请求可清除单次失败提示",temporary.Message==null);
            temporary.Record(true,false,"native_clipboard_unavailable","unused",Snapshot());temporary.Record(false,true,null,"unused",Snapshot());
            check("普通文字填入成功清除单次失败且不产生表情锁定",temporary.Message==null&&!temporary.RequiresObserverRestart);
            temporary.Record(false,false,"fill_exception_Example","unused",Snapshot());
            check("未开始写入的普通文字取消不留下故障提示",temporary.Message==null&&!temporary.RequiresObserverRestart);
            var plain=new FillNoticePolicy();var plainSnapshot=Snapshot();
            plain.Record(false,false,"fill_exception_ElementNotAvailableException","unused",plainSnapshot,true);
            string plainMessage=plain.Message;
            plainSnapshot.DraftEmpty=false;plain.Observe(plainSnapshot);plain.Observe(new ChatSnapshot{Available=false});plain.Observe(plainSnapshot);
            check("普通文字写入异常在例行快照与草稿出现后保留明确提示",plainMessage=="未确认文字填入，请检查草稿"&&plain.Message==plainMessage&&!plain.RequiresObserverRestart);
            plainSnapshot.Activity++;plain.Observe(plainSnapshot);
            check("用户继续输入后清除普通文字失败提示",plain.Message==null);
            foreach(string changed in new[]{"session","tail","account","friend","group"}) {
                plain=new FillNoticePolicy();plain.Record(false,false,null,"unused",Snapshot(),true);
                var s=Snapshot();if(changed=="session")s.Session="changed";else if(changed=="tail")s.Tail="changed";else if(changed=="account")s.AccountId="changed";else if(changed=="friend")s.FriendId="changed";else s.GroupId="changed";
                plain.Observe(s);
                check("普通文字失败提示不跨首个新消息或其他账号聊天："+changed,plain.Message==null);
            }
            plain.Record(false,false,null,"unused",Snapshot(),true);plain.Record(false,true,null,"unused",Snapshot(),true);
            check("普通文字重试成功清除旧失败提示",plain.Message==null);
            plain.Record(false,false,null,"unused",Snapshot(),true);plain.ClearAttempt();
            check("新推荐生成清除普通文字失败提示",plain.Message==null);
            plain.Record(false,false,null,"unused",new ChatSnapshot{Available=false},true);plain.Observe(Snapshot());
            check("失去会话上下文的文字失败不附到后来打开的好友",plain.Message==null);
        }
    }
}
