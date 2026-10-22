using System;
using System.Collections.Generic;
using System.Linq;

namespace WeChatPet.Runtime {
    public static class EmptyChatTests {
        static readonly long Epoch = new DateTime(2026, 10, 21, 3, 0, 0, DateTimeKind.Utc).Ticks;
        static long T(int milliseconds) { return Epoch + TimeSpan.TicksPerMillisecond * milliseconds; }
        static ChatMessage Message(string key, uint localId, int createdMs) {
            return new ChatMessage { Key=key, MemoryKey=key, LocalId=localId, CreatedTicks=T(createdMs), Role="peer", Type=1, Text="我不吃香菜" };
        }
        static ChatSnapshot Snapshot(string session, string view, params ChatMessage[] messages) {
            return new ChatSnapshot {
                Available=true, Reason=messages.Length==0?"empty_chat":"ok", AtEnd=true,
                DraftEmpty=true, DraftConsistent=true, Session=messages.Length==0?null:session,
                Contact="同名好友", InputIdentity="input", ViewIdentity=view,
                Keys=messages.Select(m=>m.Key).ToArray(), Messages=messages.ToList(),
                Tail=messages.Length==0?null:messages[messages.Length-1].Key,
                ContentRevision=String.Join("|",messages.Select(m=>m.Key+":"+m.Text))
            };
        }
        static ChatSnapshot Empty() { return Snapshot(null,"view-A"); }
        static ChatSnapshot One(string key, uint localId, int createdMs) { return Snapshot("A","view-A",Message(key,localId,createdMs)); }
        static RecommendationController Known() {
            var controller=new RecommendationController { Connected=true };
            controller.Observe(One("old",10,0),T(0));
            return controller;
        }
        static void Settle(RecommendationController controller, ChatSnapshot snapshot, int firstMs) {
            controller.Observe(snapshot,T(firstMs));controller.Observe(snapshot,T(firstMs+610));
        }
        public static void Run(Action<string,bool> check) {
            var c=Known();
            c.Observe(Empty(),T(1000));c.Observe(Empty(),T(3000));c.Observe(Empty(),T(6000));
            check("确认空聊天保持五秒后仍持续监听",c.LastDecision=="empty_chat_watching"&&c.TriggerCount==0);
            var first=One("new",11,6500);Settle(c,first,6500);
            check("清空后同文不同消息标识触发推荐",c.TriggerCount==1&&c.PendingId!=null);
            string id=c.PendingId;
            check("清空后的推荐绑定实际会话和消息",c.Complete(id,"好，给你不放香菜。",T(7120))&&c.Ready.Session=="A"&&c.Ready.Tail=="new");
            c.Observe(first,T(7500));c.Observe(first,T(8000));
            check("清空后重复采样不重复生成",c.TriggerCount==1);
            c.Observe(Empty(),T(8100));
            check("再次清空立即撤销已有推荐",c.Ready==null&&c.PendingId==null&&c.LastDecision=="empty_chat_watching");
            Settle(c,first,9000);
            check("清空后恢复已见消息键不会重放",c.TriggerCount==1&&c.PendingId==null);

            c=new RecommendationController { Connected=true };
            c.Observe(Empty(),T(1000));c.Observe(Empty(),T(6000));Settle(c,One("first-ever",1,6500),6500);
            check("启动时就是空聊天也能接收首条新来信",c.TriggerCount==1&&c.PendingId!=null);

            c=Known();c.Observe(Empty(),T(1000));
            c.Observe(One("first",11,2000),T(2000));
            var second=Snapshot("A","view-A",Message("first",11,2000),Message("second",12,2300));
            c.Observe(second,T(2300));c.Observe(second,T(2890));
            check("清空后的连续来信继续等待合并窗口",c.TriggerCount==0);
            c.Observe(second,T(2910));
            check("清空后的连续来信仅为最新尾消息生成",c.TriggerCount==1&&c.Complete(c.PendingId,"最新回复",T(2920))&&c.Ready.Tail=="second");
            var third=Snapshot("A","view-A",Message("first",11,2000),Message("second",12,2300),Message("third",13,4000));
            Settle(c,third,4000);
            check("空态锚点消耗后仍能持续处理普通追加",c.TriggerCount==2&&c.PendingId!=null);

            c=Known();c.Observe(Empty(),T(5000));Settle(c,One("old-time-new-key",11,1000),6000);
            check("清空后未见但时间过旧的消息不触发",c.TriggerCount==0&&c.PendingId==null);
            c=Known();c.Observe(Empty(),T(1000));Settle(c,One("old-local-id",9,2000),2000);
            check("清空后低于原消息高水位的本地ID不触发",c.TriggerCount==0&&c.PendingId==null);
            c=Known();c.Observe(Empty(),T(1000));Settle(c,One("same-local-id",10,2000),2000);
            check("清空后等于原消息高水位的本地ID不触发",c.TriggerCount==0&&c.PendingId==null);
            c=Known();c.Observe(Empty(),T(1000));
            Settle(c,Snapshot("A","view-A",Message("old",10,0),Message("fresh-with-restored-history",11,2000)),2000);
            check("清空后混入已见历史的列表恢复不伪装成纯新增",c.TriggerCount==0&&c.PendingId==null);
            c=Known();Settle(c,One("replacement-without-empty",11,2000),2000);
            check("未观察到确认空态时尾消息消失仍只建基线",c.TriggerCount==0&&c.LastDecision=="history_baseline");

            c=Known();c.Observe(Empty(),T(1000));
            Settle(c,Snapshot("B","view-A",Message("other-friend",11,2000)),2000);
            check("同名且视图控件相同的另一原生会话生成打开推荐",c.TriggerCount==1&&c.PendingId!=null&&c.LastTriggerOrigin=="chat_open");
            c=Known();c.Observe(Empty(),T(1000));
            Settle(c,Snapshot("A","recreated-view",Message("same-friend-new-view",11,2000)),2000);
            check("空态后输入视图变化不复用旧空态锚点",c.TriggerCount==0&&c.LastDecision=="history_baseline");

            foreach(string reason in new[]{"wechat_hidden_or_minimized","read_unavailable","controls_unavailable","native_model_changed"}) {
                c=Known();c.Observe(Empty(),T(1000));c.Observe(new ChatSnapshot {Reason=reason},T(2200));
                Settle(c,One("arrived-during-gap",11,2300),2400);
                check("空态后中断恢复不补触发来信："+reason,c.TriggerCount==0&&c.PendingId==null&&c.LastDecision=="history_baseline");
            }
            c=Known();c.Observe(Empty(),T(1000));c.SetPaused(true);c.Observe(One("while-paused",11,2000),T(2000));c.SetPaused(false);
            Settle(c,One("while-paused",11,2000),3000);
            check("空态后暂停恢复不补发暂停期间来信",c.TriggerCount==0&&c.PendingId==null);
            c=Known();c.Observe(Empty(),T(1000));var history=Empty();history.AtEnd=false;c.Observe(history,T(1200));
            Settle(c,One("after-scroll",11,2000),2000);
            check("空态后滚动历史销毁首条来信锚点",c.TriggerCount==0&&c.PendingId==null);
            c=Known();c.Observe(new ChatSnapshot {Reason="native_ok"},T(2200));Settle(c,One("unknown-not-empty",11,2300),2300);
            check("旧native_ok失败信号不能充当确认空态",c.TriggerCount==0&&c.PendingId==null);

            foreach(string protection in new[]{"draft","composing"}) {
                c=Known();c.Observe(Empty(),T(1000));var blocked=One("protected-first",11,2000);
                if(protection=="draft")blocked.DraftEmpty=false;else blocked.Composing=true;
                Settle(c,blocked,2000);Settle(c,One("protected-first",11,2000),3000);
                check("空态首条遇到输入保护后不会迟补推荐："+protection,c.TriggerCount==0&&c.PendingId==null);
            }
            foreach(string interruption in new[]{"activity","input"}) {
                c=Known();c.Observe(Empty(),T(1000));c.Observe(One("interrupted-first",11,2000),T(2000));
                var interrupted=One("interrupted-first",11,2000);
                if(interruption=="activity")interrupted.Activity=1;else interrupted.InputIdentity="replacement-input";
                c.Observe(interrupted,T(2200));c.Observe(interrupted,T(3000));
                check("空态首条等待合并时用户输入变化取消候选："+interruption,c.TriggerCount==0&&c.PendingId==null);
            }

            c=Known();c.Observe(Empty(),T(1000));var delayed=One("delayed-first",11,2000);delayed.Messages[0].Text=null;
            c.Observe(delayed,T(2000));c.Observe(One("delayed-first",11,2000),T(2200));c.Observe(One("delayed-first",11,2000),T(2790));
            check("空态首条正文迟到后仍等待六百毫秒",c.TriggerCount==0&&c.LastDecision=="waiting_for_text");
            c.Observe(One("delayed-first",11,2000),T(2810));c.Observe(One("delayed-first",11,2000),T(3200));
            check("空态首条正文补齐后只生成一次",c.TriggerCount==1&&c.PendingId!=null);
            c=Known();c.Observe(Empty(),T(1000));c.Observe(delayed,T(2000));Settle(c,One("delayed-first",11,2000),4101);
            check("空态首条正文等待超过两秒不迟补生成",c.TriggerCount==0&&c.LastDecision=="text_wait_expired");

            foreach(string kind in new[]{"self","image"}) {
                c=Known();c.Observe(Empty(),T(1000));var unsupported=One("non-peer-text",11,2000);
                if(kind=="self")unsupported.Messages[0].Role="self";else unsupported.Messages[0].Type=3;
                Settle(c,unsupported,2000);
                check("空态首条仍只由对方文字触发："+kind,c.TriggerCount==0&&c.PendingId==null);
            }
        }
    }
}
