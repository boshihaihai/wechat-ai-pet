using System;
using System.Collections.Generic;

namespace WeChatPet.Runtime {
    public static class GenerationRecoveryTests {
        static readonly long Epoch=new DateTime(2026,10,21,6,0,0,DateTimeKind.Utc).Ticks;
        static long T(int ms) {return Epoch+TimeSpan.TicksPerMillisecond*ms;}
        static ChatSnapshot S(bool incoming=true,string revision="new") {
            var messages=new List<ChatMessage> {new ChatMessage {Key="old",Type=1,LocalId=1,Role="self",Text="Earlier self text",CreatedTicks=T(0)}};
            if(incoming)messages.Add(new ChatMessage {Key="new",Type=1,LocalId=2,Role="peer",Text="Synthetic incoming text",CreatedTicks=T(1000)});
            return new ChatSnapshot {Available=true,AtEnd=true,DraftEmpty=true,DraftConsistent=true,AccountId="account",FriendId="friend",Session="session",InputIdentity="input",ViewIdentity="view",Tail=incoming?"new":"old",Keys=incoming?new[]{"old","new"}:new[]{"old"},Messages=messages,ContentRevision=revision};
        }
        static RecommendationController Pending() {
            var c=new RecommendationController {Connected=true};c.Observe(S(false,"old"),T(0));c.Observe(S(),T(1000));c.Observe(S(),T(1610));return c;
        }
        static void Gap(RecommendationController c,int ms=1700,string reason="native_model_changed") {c.Observe(new ChatSnapshot {Reason=reason},T(ms));}
        static void Settle(RecommendationController c,ChatSnapshot s,int ms) {c.Observe(s,T(ms));c.Observe(s,T(ms+610));}
        public static void Run(Action<string,bool> check) {
            foreach(string reason in new[]{"native_model_changed","controls_unavailable","read_unavailable"}) {
                var c=Pending();string original=c.PendingId;Gap(c,1700,reason);c.Observe(S(),T(1800));c.Observe(S(),T(2390));
                check("生成中短读恢复仍等待600ms："+reason,c.TriggerCount==1&&c.PendingId==null&&!c.Complete(original,"stale",T(2400)));
                c.Observe(S(),T(2410));
                check("短读恢复用新请求版本重试一次："+reason,c.TriggerCount==2&&c.PendingId!=null&&c.PendingId!=original&&c.LastTriggerOrigin=="appended_message_recovery"&&c.Complete(c.PendingId,"fresh",T(2420))&&c.Ready.Tail=="new");
            }
            var revised=Pending();string oldRequest=revised.PendingId;revised.Observe(S(true,"updated"),T(1700));revised.Observe(S(true,"updated-again"),T(2100));revised.Observe(S(true,"updated-again"),T(2690));
            check("同尾正文修订连续变化重新等待稳定",revised.TriggerCount==1&&revised.PendingId==null&&!revised.Complete(oldRequest,"stale",T(2700)));
            revised.Observe(S(true,"updated-again"),T(2710));
            check("同尾正文稳定后采用最新上下文的新请求",revised.TriggerCount==2&&revised.PendingId!=oldRequest&&revised.Complete(revised.PendingId,"fresh",T(2720))&&revised.Ready.ContentRevision=="updated-again");
            var budget=Pending();Gap(budget);Settle(budget,S(),1800);Gap(budget,2500);Settle(budget,S(),2600);
            check("同一来信至多恢复一次不无限消耗模型",budget.TriggerCount==2&&budget.PendingId==null&&budget.Ready==null);
            var unstable=Pending();Gap(unstable);unstable.Observe(S(),T(1800));Gap(unstable,2100);unstable.Observe(S(),T(2200));unstable.Observe(S(),T(2790));
            check("恢复候选期间再次短读后重新计稳定窗口",unstable.TriggerCount==1&&unstable.PendingId==null);
            unstable.Observe(S(),T(2810));check("恢复候选重新稳定后只触发一次",unstable.TriggerCount==2);
            foreach(string guard in new[]{"draft","inconsistent","composing","activity","input"}) {
                var c=Pending();Gap(c);var blocked=S();
                if(guard=="draft")blocked.DraftEmpty=false;else if(guard=="inconsistent")blocked.DraftConsistent=false;else if(guard=="composing")blocked.Composing=true;else if(guard=="activity")blocked.Activity=1;else blocked.InputIdentity="replaced";
                c.Observe(blocked,T(1800));Settle(c,S(),2000);
                check("恢复票据遇输入保护永久撤销："+guard,c.TriggerCount==1&&c.PendingId==null&&c.Ready==null);
            }
            foreach(string cancel in new[]{"invalidate","reset","pause","input_activity","minimized"}) {
                var c=Pending();Gap(c);
                if(cancel=="invalidate")c.Invalidate();else if(cancel=="reset")c.Reset();else if(cancel=="pause"){c.SetPaused(true);c.SetPaused(false);}else Gap(c,1750,cancel=="minimized"?"wechat_hidden_or_minimized":"input_activity");
                Settle(c,S(),1800);
                check("外部撤销暂停配置重置等不恢复旧请求："+cancel,c.TriggerCount==1&&c.PendingId==null&&c.Ready==null);
            }
            var switched=Pending();oldRequest=switched.PendingId;Gap(switched);var other=S();other.Session="other-session";other.AccountId="other-account";Settle(switched,other,1800);
            check("恢复期间换账号只允许新账号打开请求",switched.TriggerCount==2&&switched.LastTriggerOrigin=="chat_open"&&!switched.Complete(oldRequest,"old-account",T(2420))&&switched.Complete(switched.PendingId,"new-account",T(2430))&&switched.Ready.Session=="other-session");
            var expired=Pending();Gap(expired);Settle(expired,S(),2800);check("恢复超过一秒窗口不补发",expired.TriggerCount==1&&expired.PendingId==null);
            var ready=Pending();ready.Complete(ready.PendingId,"ready",T(1620));Gap(ready);Settle(ready,S(),1800);check("已展示推荐被撤销后不借恢复反复重生成",ready.TriggerCount==1&&ready.Ready==null&&ready.PendingId==null);
            var baseline=new RecommendationController {Connected=true};Settle(baseline,S(false,"old"),0);Gap(baseline,800);Settle(baseline,S(false,"old-revised"),900);check("历史正文更新且没有在途请求不产生恢复",baseline.TriggerCount==0);
            var emoji=new RecommendationController {Connected=true};emoji.Observe(S(false,"old"),T(0));var emojiMessage=S();emojiMessage.Messages[1].Text="😂😂";string supplied=null;emoji.Generate+=(id,m)=>supplied=m[m.Count-1].Text;Settle(emoji,emojiMessage,1000);
            check("仅Unicode表情的文字来信正常触发并保留正文",emoji.TriggerCount==1&&supplied=="😂😂");
            var sticker=new RecommendationController {Connected=true};sticker.Observe(S(false,"old"),T(0));var stickerMessage=S();stickerMessage.Messages[1].Type=47;stickerMessage.Messages[1].Text=null;Settle(sticker,stickerMessage,1000);
            check("非文字贴纸Type47不凭空生成",sticker.TriggerCount==0&&sticker.PendingId==null);
        }
    }
}
