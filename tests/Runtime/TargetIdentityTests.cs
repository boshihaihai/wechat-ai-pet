using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace WeChatPet.Runtime {
    public static class TargetIdentityTests {
        static readonly long Epoch=new DateTime(2026,10,21,4,0,0,DateTimeKind.Utc).Ticks;
        static long T(int milliseconds) { return Epoch+TimeSpan.TicksPerMillisecond*milliseconds; }
        static ChatSnapshot Snapshot(string account,bool appended,bool second=false) {
            string[] keys=second?new[]{"old","new","newer"}:appended?new[]{"old","new"}:new[]{"old"};
            var messages=keys.Select((key,index)=>new ChatMessage {
                Key=key,MemoryKey=key,LocalId=(uint)(index+1),CreatedTicks=T(index*1000),
                Role="peer",Type=1,Text="同一条测试文字"
            }).ToList();
            return new ChatSnapshot {
                Available=true,Reason="ok",AtEnd=true,DraftEmpty=true,DraftConsistent=true,
                Session=NativeMessageReader.SessionScope(account,"same-friend"),
                AccountId=account,FriendId="same-friend",Contact="同名好友",InputIdentity="same-input",ViewIdentity="same-view",
                Keys=keys,Tail=keys[keys.Length-1],Messages=messages,ContentRevision=String.Join("|",keys)
            };
        }
        static RecommendationController Pending() {
            var controller=new RecommendationController {Connected=true};
            controller.Observe(Snapshot("account-A",false),T(0));
            controller.Observe(Snapshot("account-A",true),T(1000));
            controller.Observe(Snapshot("account-A",true),T(1610));
            return controller;
        }
        static void WindowChecks(Action<string,bool> check) {
            var results=new List<KeyValuePair<string,bool>>();Exception failure=null;
            var thread=new Thread(delegate() {
                try {
                    using(var process=Process.GetCurrentProcess())using(var form=new Form()) {
                        // Creating only a handle keeps the fixture hidden and never
                        // activates, changes or inspects any existing application.
                        form.ShowInTaskbar=false;
                        long hwnd=form.Handle.ToInt64(),birth=process.StartTime.ToUniversalTime().Ticks;
                        int pid=process.Id;
                        results.Add(new KeyValuePair<string,bool>("隐藏窗口仍属于有效目标，不把不可见当作进程退出",!Win.IsWindowVisible(new IntPtr(hwnd))&&TargetIdentity.IsCurrent(pid,birth,hwnd)));
                        results.Add(new KeyValuePair<string,bool>("窗口有效但进程启动时间不同会失效",!TargetIdentity.IsCurrent(pid,birth+1,hwnd)));
                        results.Add(new KeyValuePair<string,bool>("窗口有效但所属PID不同会失效",!TargetIdentity.IsCurrent(pid==Int32.MaxValue?pid-1:pid+1,birth,hwnd)));
                        results.Add(new KeyValuePair<string,bool>("零窗口句柄会失效",!TargetIdentity.IsCurrent(pid,birth,0)));
                        results.Add(new KeyValuePair<string,bool>("不存在的目标进程会失效且不抛出异常",!TargetIdentity.IsCurrent(Int32.MaxValue,birth,hwnd)));
                        form.Dispose();
                        results.Add(new KeyValuePair<string,bool>("已销毁窗口在原进程仍运行时失效",!TargetIdentity.IsCurrent(pid,birth,hwnd)));
                    }
                }catch(Exception e) {failure=e;}
            });
            thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
            if(!thread.Join(10000))throw new Exception("目标身份隐藏窗口测试超时");
            if(failure!=null)throw new Exception("目标身份隐藏窗口测试失败",failure);
            foreach(var result in results)check(result.Key,result.Value);
        }
        public static void Run(Action<string,bool> check) {
            WindowChecks(check);
            check("账号与好友相同的会话范围保持一致",NativeMessageReader.SessionScope("account-A","friend-A")==NativeMessageReader.SessionScope("account-A","friend-A"));
            check("同一好友在不同微信账号下属于不同会话",NativeMessageReader.SessionScope("account-A","same-friend")!=NativeMessageReader.SessionScope("account-B","same-friend"));
            check("同一账号的不同好友属于不同会话",NativeMessageReader.SessionScope("same-account","friend-A")!=NativeMessageReader.SessionScope("same-account","friend-B"));
            check("账号和好友分隔符组合不会造成会话碰撞",NativeMessageReader.SessionScope("A|B","C")!=NativeMessageReader.SessionScope("A","B|C")&&NativeMessageReader.SessionScope("A:B","C")!=NativeMessageReader.SessionScope("A","B:C"));

            var controller=Pending();string oldRequest=controller.PendingId;
            check("换号前存在等待中的推荐请求",oldRequest!=null&&controller.TriggerCount==1);
            controller.Observe(Snapshot("account-B",true),T(1700));
            check("同名好友同消息键同控件在换号后仍撤销旧请求",controller.PendingId==null&&controller.Ready==null&&controller.LastDecision=="chat_open_candidate");
            check("旧账号迟到模型结果不能进入新账号",!controller.Complete(oldRequest,"旧账号的回复",T(1800))&&controller.Ready==null);
            controller.Observe(Snapshot("account-B",true),T(1900));
            check("换号后的已有消息先等待打开稳定窗口",controller.TriggerCount==1&&controller.PendingId==null);
            controller.Observe(Snapshot("account-B",true,true),T(2000));
            controller.Observe(Snapshot("account-B",true,true),T(2610));
            check("新账号完成基线后继续响应新增来信",controller.TriggerCount==2&&controller.PendingId!=null&&controller.Complete(controller.PendingId,"新账号的回复",T(2620))&&controller.Ready.Session==NativeMessageReader.SessionScope("account-B","same-friend"));

            controller=Pending();
            check("换号前已生成可展示推荐",controller.Complete(controller.PendingId,"原账号推荐",T(1650))&&controller.Ready!=null);
            controller.Observe(Snapshot("account-B",true),T(1700));
            check("换号立即撤销旧账号可填入的推荐",controller.Ready==null&&controller.PendingId==null&&controller.LastDecision=="chat_open_candidate");

            controller=Pending();
            controller.Observe(new ChatSnapshot {Available=true,AtEnd=true,DraftEmpty=true,Keys=new string[0],Messages=new List<ChatMessage>(),ViewIdentity="same-view",InputIdentity="same-input"},T(1800));
            controller.Observe(Snapshot("account-B",true),T(2000));
            controller.Observe(Snapshot("account-B",true),T(2700));
            check("换号后的列表按新账号打开推荐而非旧空态来信触发",controller.TriggerCount==2&&controller.Ready==null&&controller.PendingId!=null&&controller.LastTriggerOrigin=="chat_open");
        }
    }
}
