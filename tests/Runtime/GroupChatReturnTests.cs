using System;
using System.Collections.Generic;
using System.Linq;

namespace WeChatPet.Runtime {
    public static class GroupChatReturnTests {
        static readonly long Epoch=new DateTime(2026,10,21,8,0,0,DateTimeKind.Utc).Ticks;
        static long T(int ms){return Epoch+TimeSpan.TicksPerMillisecond*ms;}
        static RecommendationController C(){return new RecommendationController{Connected=true};}
        static ChatSnapshot Group(){return new ChatSnapshot{Available=false,GroupChat=true,Reason="private_chat_required"};}
        static ChatSnapshot S(char account='a',char friend='b',string tail="existing") {
            string scope=account+":"+friend,key=scope+":"+tail;
            return new ChatSnapshot {
                Available=true,Reason="ok",AtEnd=true,DraftEmpty=true,DraftConsistent=true,
                AccountId=new string(account,64),FriendId=new string(friend,64),Session=scope,Contact="合成同名好友",
                InputIdentity="input-"+scope,ViewIdentity="view-"+scope,ContentRevision=key,Tail=key,Keys=new[]{key},
                Messages=new List<ChatMessage>{new ChatMessage{Key=key,Role="peer",Type=1,LocalId=10,Text="合成正文 "+key,CreatedTicks=T(-1000)}}
            };
        }
        static void Settle(RecommendationController c,ChatSnapshot s,int ms){c.Observe(s,T(ms));c.Observe(s,T(ms+610));}
        public static void Run(Action<string,bool> check) {
            check("群人数仅接受至少两人的配对整数格式",WeChatReader.HasGroupCount("(2)")&&WeChatReader.HasGroupCount("(152)"));
            bool invalidCounts=true;
            foreach(string text in new string[]{null,"","()","(0)","(1)","(abc)","152","(152","152)","((152))"})invalidCounts&=!WeChatReader.HasGroupCount(text);
            check("空值零一非数字及未配对人数标签不确认群聊",invalidCounts);

            var c=C();c.Observe(Group(),T(0));c.Observe(Group(),T(1000));
            check("明确群聊只撤销观察且不请求生成",c.TriggerCount==0&&c.PendingId==null&&c.Ready==null&&c.Current.GroupChat);
            Settle(c,S(),2000);string original=c.PendingId;
            c.Observe(Group(),T(2700));c.Observe(Group(),T(2900));
            check("离开私聊进入群聊立即撤销待生成请求并拒绝旧结果",original!=null&&c.TriggerCount==1&&c.PendingId==null&&c.Ready==null&&!c.Complete(original,"迟到的合成旧结果",T(2910)));
            c.Observe(S(),T(3000));c.Observe(S(),T(3590));
            check("群聊返回同一私聊仍需等待完整稳定窗口",c.TriggerCount==1&&c.PendingId==null);
            c.Observe(S(),T(3600));
            check("群聊返回同一私聊稳定六百毫秒后仅生成一次",c.TriggerCount==2&&c.PendingId!=null&&c.PendingId!=original&&c.LastTriggerOrigin=="chat_open");
            c.Observe(S(),T(4000));c.Observe(S(),T(5000));
            check("群聊返回后的连续相同采样不重复生成",c.TriggerCount==2);

            c=C();List<ChatMessage> context=null;c.Generate+=(id,messages)=>context=messages;
            Settle(c,S(),0);c.Observe(Group(),T(700));var arrived=S('a','b',"arrived-during-group");arrived.Messages[0].CreatedTicks=T(900);arrived.Messages[0].LocalId=11;
            Settle(c,arrived,1000);
            check("停留群聊期间出现的新私聊尾部在返回后可推荐",c.TriggerCount==2&&c.PendingId!=null&&c.LastTriggerOrigin=="chat_open"&&context!=null&&context.Count==1&&context[0].Key==arrived.Tail);

            foreach(string reason in new[]{"wechat_hidden_or_minimized","native_message_changed","controls_unavailable","read_unavailable","input_activity","private_chat_required"}) {
                c=C();Settle(c,S(),0);bool completed=c.Complete(c.PendingId,"已完成的合成回复",T(620));
                c.Observe(new ChatSnapshot{Reason=reason},T(700));Settle(c,S(),1000);
                check("未确认群聊的中断恢复不重复推荐："+reason,completed&&c.TriggerCount==1&&c.PendingId==null);
            }
            c=C();Settle(c,S(),0);bool firstCompleted=c.Complete(c.PendingId,"已完成的合成回复",T(620));
            c.Observe(new ChatSnapshot{GroupChat=true,Reason="read_unavailable"},T(700));Settle(c,S(),1000);
            check("群聊标志与原因不匹配时不清除上次私聊访问",firstCompleted&&c.TriggerCount==1&&c.PendingId==null);

            c=C();context=null;c.Generate+=(id,messages)=>context=messages;
            var oldChat=S('a','b');Settle(c,oldChat,0);original=c.PendingId;c.Observe(Group(),T(700));
            var currentChat=S('c','d',"current-account-message");Settle(c,currentChat,1000);string currentRequest=c.PendingId;
            check("群聊后跨账号进入不同好友仅使用当前私聊上下文",c.TriggerCount==2&&context!=null&&context.Count==1&&context[0].Key==currentChat.Tail&&context.All(m=>m.Key!=oldChat.Tail));
            check("群聊后跨账号旧结果被拒绝且新结果绑定当前会话",original!=null&&currentRequest!=null&&!c.Complete(original,"旧账号结果",T(1620))&&c.Complete(currentRequest,"当前账号结果",T(1630))&&c.Ready.Session==currentChat.Session&&c.Ready.Tail==currentChat.Tail);

            foreach(string protection in new[]{"draft","inconsistent","composing"}) {
                c=C();Settle(c,S(),0);c.Observe(Group(),T(700));var blocked=S();
                if(protection=="draft")blocked.DraftEmpty=false;else if(protection=="inconsistent")blocked.DraftConsistent=false;else blocked.Composing=true;
                Settle(c,blocked,1000);
                check("群聊返回时仍保留草稿与输入保护："+protection,c.TriggerCount==1&&c.PendingId==null&&c.Ready==null);
            }
        }
    }
}
