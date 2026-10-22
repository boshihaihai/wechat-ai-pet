using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace WeChatPet.Runtime {
    public static class GroupRecommendationTests {
        static readonly long Epoch=new DateTime(2026,10,21,8,0,0,DateTimeKind.Utc).Ticks;
        static long T(int ms){return Epoch+TimeSpan.TicksPerMillisecond*ms;}
        static string Id(char c){return new string(c,64);}
        static RecommendationController C(){return new RecommendationController{Connected=true};}
        static ChatSnapshot G(int ms=0,char account='a',char group='b',string tail="latest") {
            string scope="group:"+account+":"+group,key=scope+":"+tail;
            return new ChatSnapshot {
                Available=true,GroupChat=true,AccountId=Id(account),GroupId=Id(group),AtEnd=true,DraftEmpty=true,DraftConsistent=true,
                Session=scope,InputIdentity="input:"+scope,ViewIdentity="view:"+scope,ContentRevision="revision:"+key,Contact="合成测试群",
                Foreground=true,Focused=true,ImeAllowed=true,CapturedTicks=T(ms),Tail=key,Keys=new[]{scope+":older",key},
                Messages=new List<ChatMessage>{
                    new ChatMessage{Key=scope+":older",SenderId=Id('c'),SenderName="成员一",Role="self",Type=1,Text="前一条合成群消息",LocalId=1,CreatedTicks=T(-1000)},
                    new ChatMessage{Key=key,SenderId=Id('d'),SenderName="成员二",Role="peer",Type=1,Text="这次大家怎么安排？",LocalId=2,CreatedTicks=T(-500)}
                }
            };
        }
        static ChatSnapshot Private(int ms=0) {
            var s=G(ms);s.GroupChat=false;s.FriendId=s.GroupId;s.GroupId=null;s.Session="private:a:b";
            s.InputIdentity="private-input";s.ViewIdentity="private-view";
            return s;
        }
        static void Observe(RecommendationController c,ChatSnapshot s,int ms){s.CapturedTicks=T(ms);c.Observe(s,T(ms));}
        static bool Begin(RecommendationController c,ChatSnapshot s,int ms=0){Observe(c,s,ms);return c.RequestGroupRecommendation(T(ms));}
        public static void Run(Action<string,bool> check) {
            var c=C();var s=G();int generations=0;List<ChatMessage> captured=null;
            c.Generate+=(id,context)=>{generations++;captured=context;};
            Observe(c,s,0);Observe(c,G(700),700);Observe(c,G(1300,'a','b',"new-arrival"),1300);
            check("打开群聊和群新消息都不会自动生成",c.TriggerCount==0&&generations==0&&c.PendingId==null&&c.LastDecision=="group_waiting_for_click");
            check("点击群宠物以当前群上下文生成一次",c.RequestGroupRecommendation(T(1310))&&generations==1&&c.TriggerCount==1&&c.LastTriggerOrigin=="group_click"&&captured!=null&&captured.Count==2&&captured.Last().Key==c.Current.Tail&&captured.All(m=>MemoryIdentity.Valid(m.SenderId)));
            string request=c.PendingId;
            check("群生成中重复点击不会并发请求",!c.RequestGroupRecommendation(T(1320))&&generations==1&&c.PendingId==request);
            check("群生成结果携带账号和群身份",c.Complete(request,"合成群回复",T(1400))&&c.Ready.GroupChat&&c.Ready.GroupId==s.GroupId&&c.Ready.AccountId==s.AccountId);
            Observe(c,G(1500,'a','b',"new-arrival"),1500);
            check("同一群连续观察保留有效推荐且不重复生成",c.Ready!=null&&generations==1&&c.PendingId==null);
            check("群请求完成后空请求ID不能重新写入结果",!c.Complete(null,"不应出现",T(1510))&&c.Ready.Text=="合成群回复");
            Observe(c,G(62000,'a','b',"new-arrival"),62000);
            check("群推荐六十秒后撤销且不自动续期",c.Ready==null&&c.PendingId==null&&generations==1);

            c=C();captured=null;c.Generate+=(id,context)=>captured=context;s=G();s.Messages[0].Type=3;s.Messages[0].Text="不应解释的图片标签";
            check("群上下文保留非文字类型和成员而不猜测内容",Begin(c,s)&&captured!=null&&captured.Count==2&&captured[0].Type==3&&captured[0].Text==""&&captured[0].SenderId==Id('c'));
            c=C();captured=null;c.Generate+=(id,context)=>captured=context;s=G();s.Messages[0].Text=null;
            check("群上下文跳过尚未读取到正文的历史文字行",Begin(c,s)&&captured!=null&&captured.Count==1&&captured[0].Key==s.Tail);

            c=C();s=G();Begin(c,s);request=c.PendingId;
            Observe(c,G(300,'a','b',"next"),300);Observe(c,G(1000,'a','b',"next"),1000);
            check("生成中群新消息撤销旧请求且拒绝迟到结果",c.PendingId==null&&c.TriggerCount==1&&!c.Complete(request,"过时回复",T(1010)));
            check("新消息之后必须再次点击才生成",c.RequestGroupRecommendation(T(1010))&&c.TriggerCount==2&&c.LastTriggerOrigin=="group_click");
            request=c.PendingId;bool completed=c.Complete(request,"新消息回复",T(1020));
            Observe(c,G(1100,'a','b',"another"),1100);
            check("群新消息同时撤销已就绪推荐",completed&&c.Ready==null&&c.PendingId==null&&c.TriggerCount==2);

            var changes=new Dictionary<string,Action<ChatSnapshot>> {
                {"切换群",x=>x.GroupId=Id('e')},
                {"换号",x=>x.AccountId=Id('f')},
                {"已有草稿",x=>x.DraftEmpty=false},
                {"草稿读取不一致",x=>x.DraftConsistent=false},
                {"组词",x=>x.Composing=true},
                {"用户输入活动",x=>x.Activity++},
                {"输入框重建",x=>x.InputIdentity+="changed"},
                {"同尾消息正文变化",x=>x.ContentRevision+="changed"},
                {"离开消息底部",x=>x.AtEnd=false},
                {"成员无法识别",x=>x.Messages[0].SenderId=null}
            };
            foreach(var change in changes) {
                c=C();Begin(c,G());request=c.PendingId;var changed=G(100);change.Value(changed);Observe(c,changed,100);
                check("群"+change.Key+"撤销待生成且旧结果不能进入新上下文",c.PendingId==null&&c.Ready==null&&!c.Complete(request,"旧结果",T(110)));
                c=C();Begin(c,G());completed=c.Complete(c.PendingId,"已就绪回复",T(10));changed=G(100);change.Value(changed);Observe(c,changed,100);
                check("群"+change.Key+"撤销已就绪且不自动生成",completed&&c.Ready==null&&c.PendingId==null&&c.TriggerCount==1);
            }
            foreach(string reason in new[]{"read_unavailable","controls_unavailable","native_message_changed","wechat_hidden_or_minimized","input_activity"}) {
                c=C();Begin(c,G());request=c.PendingId;c.Observe(new ChatSnapshot{Reason=reason},T(100));
                Observe(c,G(200),200);Observe(c,G(1000),1000);
                check("群读取中断恢复不自动重试："+reason,c.TriggerCount==1&&c.PendingId==null&&c.Ready==null&&!c.Complete(request,"迟到结果",T(1010)));
            }
            c=C();Begin(c,G());request=c.PendingId;var privateChat=Private(100);Observe(c,privateChat,100);
            check("群切私聊立即拒绝群结果",!c.Complete(request,"旧群回复",T(101))&&c.Ready==null);
            Observe(c,Private(710),710);
            check("群切私聊仍保留原私聊自动推荐流程",c.TriggerCount==2&&c.PendingId!=null&&c.LastTriggerOrigin=="chat_open");
            request=c.PendingId;Observe(c,G(720),720);
            check("私聊切群不会沿用私聊请求或偏好流程",c.PendingId==null&&c.Ready==null&&!c.Complete(request,"旧私聊回复",T(730))&&c.TriggerCount==2);

            var invalid=new Dictionary<string,Action<ChatSnapshot>> {
                {"缺少账号",x=>x.AccountId=null}, {"账号格式错误",x=>x.AccountId="a"},
                {"缺少群身份",x=>x.GroupId=null}, {"群身份格式错误",x=>x.GroupId="b"},
                {"混有私聊身份",x=>x.FriendId=Id('b')}, {"不是群聊",x=>x.GroupChat=false},
                {"读取不可用",x=>x.Available=false}, {"不是底部",x=>x.AtEnd=false},
                {"非空草稿",x=>x.DraftEmpty=false}, {"草稿不一致",x=>x.DraftConsistent=false},
                {"正在组词",x=>x.Composing=true}, {"缺少会话",x=>x.Session=null},
                {"缺少输入框",x=>x.InputIdentity=null}, {"缺少正文版本",x=>x.ContentRevision=null},
                {"缺少消息",x=>x.Messages=null}, {"空消息列表",x=>x.Keys=new string[0]},
                {"尾消息已由自己回复",x=>x.Messages.Last().Role="self"}, {"尾消息非文字",x=>x.Messages.Last().Type=3},
                {"尾消息无正文",x=>x.Messages.Last().Text=" "}, {"尾消息过长",x=>x.Messages.Last().Text=new string('x',2001)},
                {"尾消息携带图像",x=>x.Messages.Last().Image=new ChatImage{Mime="image/png",Data="synthetic"}},
                {"上下文类型未知",x=>x.Messages[0].Type=0}, {"非文字缺少成员",x=>{x.Messages[0].Type=3;x.Messages[0].SenderId=null;}},
                {"上下文缺少成员",x=>x.Messages[0].SenderId=null}, {"成员格式错误",x=>x.Messages.Last().SenderId="member"},
                {"上下文角色不明",x=>x.Messages[0].Role="unknown"}, {"上下文行不属于当前模型",x=>x.Messages[0].Key="foreign"},
                {"尾部不匹配",x=>x.Tail="wrong"}
            };
            foreach(var entry in invalid) {
                c=C();s=G();entry.Value(s);Observe(c,s,0);
                check("群点击拒绝："+entry.Key,!c.RequestGroupRecommendation(T(0))&&c.TriggerCount==0&&c.PendingId==null);
                check("群拒绝原因与展示一致："+entry.Key,!String.IsNullOrEmpty(c.LastGroupBlockReason)&&DesktopRuntime.GroupIdleStatus(s)==DesktopRuntime.GroupBlockMessage(c.LastGroupBlockReason)&&DesktopRuntime.GroupIdleStatus(s)!="点击宠物生成群聊回复");
            }
            c=C();Observe(c,G(),0);
            check("群点击拒绝超过一秒的快照",!c.RequestGroupRecommendation(T(1001))&&c.TriggerCount==0);
            check("过期快照明确要求重新点击",c.LastGroupBlockReason=="group_snapshot_stale"&&DesktopRuntime.GroupBlockMessage(c.LastGroupBlockReason).Contains("过期"));
            c=C();s=G();Observe(c,s,0);s.CapturedTicks=T(2);
            check("群点击拒绝未来采样时间",!c.RequestGroupRecommendation(T(0))&&c.TriggerCount==0);
            c=C();Observe(c,G(),0);c.Paused=true;
            check("暂停时群点击不会生成",!c.RequestGroupRecommendation(T(0))&&c.TriggerCount==0);
            check("暂停拒绝优先于可用群上下文提示",c.LastGroupBlockReason=="paused");
            c=C();Observe(c,G(),0);c.Connected=false;
            check("模型断开时群点击不会生成",!c.RequestGroupRecommendation(T(0))&&c.TriggerCount==0);
            check("模型断开拒绝能提示连接",c.LastGroupBlockReason=="model_disconnected"&&DesktopRuntime.GroupBlockMessage(c.LastGroupBlockReason).Contains("模型"));
            foreach(string reason in new[]{"group_scope_unavailable","group_latest_sender_unavailable","group_history_sender_unavailable","group_latest_unsupported","controls_unavailable","native_message_changed"}) {
                var unavailable=new ChatSnapshot{GroupChat=true,Available=false,Reason=reason};
                check("读取失败保留具体原因："+reason,RecommendationController.GroupBlockReason(unavailable)==reason&&DesktopRuntime.GroupIdleStatus(unavailable)==DesktopRuntime.GroupBlockMessage(reason)&&DesktopRuntime.GroupIdleStatus(unavailable)!="点击宠物生成群聊回复");
            }
            c=C();s=G();s.Foreground=false;s.Focused=false;s.ImeAllowed=false;Begin(c,s);
            check("点击宠物后的前台变化不阻止只读群生成",c.TriggerCount==1&&c.PendingId!=null);
            c=C();Begin(c,G());request=c.PendingId;c.Current.AccountId=Id('f');
            check("完成时重新检查群账号防止引用被更改",!c.Complete(request,"错误账号结果",T(10))&&c.Ready==null);
            c=C();Begin(c,G());request=c.PendingId;c.Current.GroupId=Id('e');
            check("完成时重新检查群身份防止引用被更改",!c.Complete(request,"错误群结果",T(10))&&c.Ready==null);
            c=C();Begin(c,G());c.Reset();Observe(c,G(100),100);Observe(c,G(1000),1000);
            check("群进程或模型重置后仍需点击而不会自动生成",c.TriggerCount==1&&c.PendingId==null&&c.Ready==null);
            CheckMemoryIsolation(check);
        }
        static void CheckMemoryIsolation(Action<string,bool> check) {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"group-memory-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try {
                var identity=new MemoryIdentity(root);string account="synthetic-account",group="synthetic-group@chatroom",member="synthetic-member";
                string groupId=identity.Group(account,group),memberId=identity.GroupMember(account,group,member);
                check("群身份与同名好友身份具有独立命名空间",MemoryIdentity.Valid(groupId)&&groupId!=identity.Friend(account,group));
                check("群身份和群成员按账号及群隔离",groupId!=identity.Group("other-account",group)&&memberId!=identity.GroupMember("other-account",group,member)&&memberId!=identity.GroupMember(account,"another-group@chatroom",member));
                var reloaded=new MemoryIdentity(root);
                check("群与群成员标识跨身份模块重启稳定",groupId==reloaded.Group(account,group)&&memberId==reloaded.GroupMember(account,group,member));
                var store=new MemoryStore(root);var p=Private();p.Messages.ForEach(m=>m.MemoryKey=identity.Message(account,"synthetic-friend",m.Key));
                store.Observe(p,T(0));var friend=store.Current(p);
                if(friend==null){check("合成私聊记忆建立成功",false);return;}
                store.SetPreferences(friend,"仅私聊使用的合成回复偏好");store.SetExpressionExamples(friend,new[]{"仅私聊示例"});
                string before=new JavaScriptSerializer().Serialize(store.Data);var g=G();g.FriendId=p.FriendId;
                check("群即使携带错误私聊ID也不能读取好友记忆",!MemoryStore.Identified(g)&&store.Current(g)==null&&store.Context(g,T(0))==""&&store.ExpressionExamples(g).Length==0);
                store.Observe(g,T(0));
                check("群观察不会更新或新增私聊记忆",new JavaScriptSerializer().Serialize(store.Data)==before);
                check("群观察后原私聊偏好和表达示例完整保留",store.Context(p,T(0)).Contains("仅私聊使用")&&store.ExpressionExamples(p).Single()=="仅私聊示例");
            }finally {
                string full=Path.GetFullPath(root),parent=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(full))Directory.Delete(full,true);
            }
        }
    }
}
