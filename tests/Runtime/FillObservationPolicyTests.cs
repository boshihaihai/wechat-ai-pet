using System;
using System.Collections.Generic;

namespace WeChatPet.Runtime {
    public static class FillObservationPolicyTests {
        static readonly long Epoch=new DateTime(2026,10,22,0,0,0,DateTimeKind.Utc).Ticks;
        static long T(int ms) {return Epoch+E(ms);}
        static long E(int ms) {return TimeSpan.TicksPerMillisecond*ms;}
        static ChatSnapshot Snapshot() {
            return new ChatSnapshot {Available=true,AtEnd=true,DraftEmpty=true,DraftConsistent=true,Foreground=true,Focused=true,ImeAllowed=true,
                Window=123,Session="session",Tail="tail",ContentRevision="revision",InputIdentity="input",AccountId="account",FriendId="friend",Activity=7,CapturedTicks=T(1000),
                Keys=new[]{"tail"},Messages=new List<ChatMessage>{new ChatMessage{Key="tail",Role="peer",Type=1,LocalId=1,Text="synthetic",CreatedTicks=T(0)}}};
        }
        static Recommendation Reply() {
            return new Recommendation {Id="reply",Session="session",Tail="tail",ContentRevision="revision",InputIdentity="input",AccountId="account",Activity=7,CreatedTicks=T(900),Text="synthetic"};
        }
        static FillObservationPolicy Begin(Recommendation reply,ChatSnapshot snapshot) {
            var policy=new FillObservationPolicy();
            if(!policy.Begin(reply.Id,123,T(1000),reply,snapshot,true,T(1000),0))throw new Exception("synthetic fill begin rejected");
            return policy;
        }
        static bool Deferred(FillObservationPolicy policy,Recommendation reply,ChatSnapshot snapshot,int ms) {
            return policy.DeferStaleObservation(reply,snapshot,true,T(1000+ms),E(ms));
        }
        public static void Run(Action<string,bool> check) {
            var reply=Reply();var snapshot=Snapshot();var policy=new FillObservationPolicy();
            check("没有填入开始事件时仍按原规则处理过期快照",!Deferred(policy,reply,snapshot,1001));
            policy=Begin(reply,snapshot);
            check("同步填入超过一秒不会因为自己的快照暂停而被撤销",Deferred(policy,reply,snapshot,1001)&&Deferred(policy,reply,snapshot,4499));
            check("填入观察宽限在4500ms边界结束",!Deferred(policy,reply,snapshot,4500));
            snapshot.CapturedTicks=T(5500);
            check("已超时的开始事件不能复活同一推荐",!policy.Begin(reply.Id,123,T(5500),reply,snapshot,true,T(5500),E(4500)));

            snapshot=Snapshot();policy=Begin(reply,snapshot);
            snapshot.CapturedTicks=T(2000);
            check("重复开始事件不能续期",!policy.Begin(reply.Id,123,T(2000),reply,snapshot,true,T(2000),E(1000))&&!Deferred(policy,reply,snapshot,4500));
            snapshot=Snapshot();policy=new FillObservationPolicy();
            bool accepted=policy.Begin(reply.Id,123,T(1000),reply,snapshot,true,T(1800),E(10000));
            check("传输延迟计入固定宽限不会从接收时重新计时",accepted&&policy.DeferStaleObservation(reply,snapshot,true,T(5499),E(13699))&&!policy.DeferStaleObservation(reply,snapshot,true,T(5500),E(13700)));
            policy=Begin(reply,snapshot);
            check("错误结束ID不能结束其它填入",!policy.End("other")&&Deferred(policy,reply,snapshot,1001));
            check("匹配结束事件立即恢复过期处理",policy.End(reply.Id)&&!Deferred(policy,reply,snapshot,1002));
            check("结束后同一开始事件也不能重新延期",!policy.Begin(reply.Id,123,T(1000),reply,snapshot,true,T(1000),0));

            foreach(string guard in new[]{"no_ready","wrong_id","window","paused","future","late","stale_snapshot","future_snapshot","expired_reply","unavailable","draft","inconsistent","composing","background","unfocused","ime","context"}) {
                reply=Reply();snapshot=Snapshot();policy=new FillObservationPolicy();
                string id=reply.Id;long window=123,start=T(1000);bool enabled=true;
                if(guard=="no_ready")reply=null;
                else if(guard=="wrong_id")id="other";
                else if(guard=="window")window=456;
                else if(guard=="paused")enabled=false;
                else if(guard=="future")start=T(1001);
                else if(guard=="late")start=T(-1);
                else if(guard=="stale_snapshot")snapshot.CapturedTicks=T(-1);
                else if(guard=="future_snapshot")snapshot.CapturedTicks=T(1001);
                else if(guard=="expired_reply")reply.CreatedTicks=T(-60000);
                else if(guard=="unavailable")snapshot.Available=false;
                else if(guard=="draft")snapshot.DraftEmpty=false;
                else if(guard=="inconsistent")snapshot.DraftConsistent=false;
                else if(guard=="composing")snapshot.Composing=true;
                else if(guard=="background")snapshot.Foreground=false;
                else if(guard=="unfocused")snapshot.Focused=false;
                else if(guard=="ime")snapshot.ImeAllowed=false;
                else snapshot.Session="other";
                check("无权或过期填入事件不获得宽限："+guard,!policy.Begin(id,window,start,reply,snapshot,enabled,T(1000),0));
            }
            foreach(string guard in new[]{"activity","chat","account","friend","group","window","input","tail","revision","draft","inconsistent","composing","unavailable","missing","ready_replaced"}) {
                reply=Reply();snapshot=Snapshot();policy=Begin(reply,snapshot);
                if(guard=="activity")snapshot.Activity++;
                else if(guard=="chat")snapshot.Session="other";
                else if(guard=="account"){snapshot.AccountId="other";reply.AccountId="other";}
                else if(guard=="friend")snapshot.FriendId="other";
                else if(guard=="group")snapshot.GroupChat=true;
                else if(guard=="window")snapshot.Window=456;
                else if(guard=="input")snapshot.InputIdentity="replaced";
                else if(guard=="tail")snapshot.Tail="new";
                else if(guard=="revision")snapshot.ContentRevision="new";
                else if(guard=="draft")snapshot.DraftEmpty=false;
                else if(guard=="inconsistent")snapshot.DraftConsistent=false;
                else if(guard=="composing")snapshot.Composing=true;
                else if(guard=="unavailable")snapshot.Available=false;
                else if(guard=="missing")snapshot=null;
                else reply.Id="replacement";
                check("实际上下文变化仍结束宽限："+guard,!Deferred(policy,reply,snapshot,1001));
            }
            reply=Reply();snapshot=Snapshot();policy=Begin(reply,snapshot);
            check("暂停或断开模型仍立即结束宽限",!policy.DeferStaleObservation(reply,snapshot,false,T(2001),E(1001)));
            policy=Begin(reply,snapshot);policy.Cancel();
            check("显式取消或停止观察器立即结束宽限且不能复活",!Deferred(policy,reply,snapshot,1001)&&!policy.Begin(reply.Id,123,T(1000),reply,snapshot,true,T(1000),0));
            policy=Begin(reply,snapshot);
            check("系统时钟回拨不会延长单调时钟4500ms期限",policy.DeferStaleObservation(reply,snapshot,true,T(2000),E(4499))&&!policy.DeferStaleObservation(reply,snapshot,true,T(2000),E(4500)));
            policy=Begin(reply,snapshot);Deferred(policy,reply,snapshot,1001);
            check("异常单调时间回退按失败关闭",!policy.DeferStaleObservation(reply,snapshot,true,T(2002),E(1000)));

            // Replay the DesktopRuntime event wiring without a desktop, hooks,
            // clipboard, observer process, or real chat data.
            foreach(string interruption in new[]{"invalidate","pause","reset","input_snapshot","chat_snapshot","account_snapshot","unavailable_snapshot"}) {
                var controller=new RecommendationController{Connected=true};snapshot=Snapshot();snapshot.CapturedTicks=T(0);
                controller.Observe(snapshot,T(0));snapshot.CapturedTicks=T(610);controller.Observe(snapshot,T(610));
                if(!controller.Complete(controller.PendingId,"synthetic",T(620)))throw new Exception("synthetic recommendation missing");
                snapshot.CapturedTicks=T(1000);policy=Begin(controller.Ready,snapshot);var currentPolicy=policy;
                controller.Invalidated+=delegate {currentPolicy.Cancel();};
                if(interruption=="invalidate")controller.Invalidate();
                else if(interruption=="pause")controller.SetPaused(true);
                else if(interruption=="reset")controller.Reset();
                else {
                    var changed=Snapshot();changed.CapturedTicks=T(1100);
                    if(interruption=="input_snapshot")changed.Activity++;
                    else if(interruption=="chat_snapshot")changed.Session="other";
                    else if(interruption=="account_snapshot"){changed.Session="other-session";changed.AccountId="other-account";}
                    else {changed.Available=false;changed.Reason="read_unavailable";}
                    controller.Observe(changed,T(1100));
                }
                check("填入生命周期不屏蔽控制器真实取消："+interruption,!policy.DeferStaleObservation(controller.Ready,controller.Current,controller.Connected&&!controller.Paused,T(2001),E(1001))&&controller.Ready==null);
            }
        }
    }
}
