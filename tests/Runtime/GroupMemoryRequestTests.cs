using System;
using System.Collections.Generic;
using System.IO;

namespace WeChatPet.Runtime {
    public static class GroupMemoryRequestTests {
        static string Id(char c){return new string(c,64);}
        static ChatSnapshot Group(char account,char group,long now) {
            string scope="group:"+account+":"+group;
            return new ChatSnapshot{Available=true,GroupChat=true,AccountId=Id(account),GroupId=Id(group),Contact="合成同名群",AtEnd=true,DraftEmpty=true,DraftConsistent=true,
                CapturedTicks=now,Session=scope,Tail=scope+":tail",Keys=new[]{scope+":tail"},InputIdentity=scope+":input",ViewIdentity=scope+":view",ContentRevision=scope+":revision",
                Messages=new List<ChatMessage>{new ChatMessage{Key=scope+":tail",Role="peer",Type=1,SenderId=Id('e'),Text="这周怎么安排？",CreatedTicks=now,LocalId=1}}};
        }
        static Dictionary<string,object> GroupData(Dictionary<string,object> request){return (Dictionary<string,object>)request["groupMemory"];}
        static bool Has(Dictionary<string,object> request,string preferences,string agreements){var g=GroupData(request);return (string)g["replyPreferences"]==preferences&&(string)g["agreements"]==agreements;}
        public static void Run(Action<string,bool> check) {
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"group-request-test-"+Guid.NewGuid().ToString("N"));
            try {
                long now=DateTime.UtcNow.Ticks;var store=new MemoryStore(directory);
                var a=Group('a','b',now);var b=Group('a','c',now);var other=Group('d','b',now);
                store.ObserveGroup(a,now);store.ObserveGroup(b,now);store.ObserveGroup(other,now);
                store.SetGroupPreferences(store.CurrentGroup(a),"甲群只说重点");store.SetGroupAgreements(store.CurrentGroup(a),"甲群周五前登记");
                store.SetGroupPreferences(store.CurrentGroup(b),"乙群活泼一些");store.SetGroupAgreements(store.CurrentGroup(b),"乙群周日讨论");
                store.SetGroupPreferences(store.CurrentGroup(other),"另一个账号的偏好");
                var friend=new FriendMemory{Id=a.GroupId,AccountId=a.AccountId,Name=a.Contact,Preferences="私聊秘密偏好"};friend.ExpressionExamples.Add("私聊专属示例");store.Data.Friends.Add(friend);
                var first=RecommendationMemory.Fields(store,a,now);
                check("群请求仅附当前账号当前群的偏好和约定",Has(first,"甲群只说重点","甲群周五前登记"));
                check("群请求清空私聊记忆与表达示例",(string)first["memory"]==""&&((string[])first["styleExamples"]).Length==0&&(string)first["contextMode"]=="group");
                check("切换同名群使用独立约定",Has(RecommendationMemory.Fields(store,b,now),"乙群活泼一些","乙群周日讨论"));
                check("换账号后同群标识不读取原账号约定",Has(RecommendationMemory.Fields(store,other,now),"另一个账号的偏好",""));
                var privateChat=new ChatSnapshot{Available=true,AccountId=a.AccountId,FriendId=a.GroupId,Contact=a.Contact};
                var privateRequest=RecommendationMemory.Fields(store,privateChat,now);
                check("同名同标识私聊请求不带群记忆",!privateRequest.ContainsKey("groupMemory")&&(string)privateRequest["contextMode"]=="private"&&(string)((Dictionary<string,object>)privateRequest["friendProfile"])["replyPreferences"]=="私聊秘密偏好");
                check("私聊表达示例仍仅属于好友",((string[])privateRequest["styleExamples"])[0]=="私聊专属示例");
                store.SetGroupPreferences(store.CurrentGroup(a),"保存后的甲群偏好");
                check("已建立请求的群偏好是独立快照",Has(first,"甲群只说重点","甲群周五前登记"));
                check("下一次群请求使用保存后的新值",Has(RecommendationMemory.Fields(store,a,now),"保存后的甲群偏好","甲群周五前登记"));
                a.Available=false;check("失去账号确认后群请求不附带记忆",Has(RecommendationMemory.Fields(store,a,now),"",""));a.Available=true;
                a.FriendId=friend.Id;check("混合好友身份的群快照不附带记忆",Has(RecommendationMemory.Fields(store,a,now),"",""));a.FriendId=null;
                store.SetGroupEnabled(store.CurrentGroup(a),false);check("禁用本群不附带记忆",Has(RecommendationMemory.Fields(store,a,now),"",""));store.SetGroupEnabled(store.CurrentGroup(a),true);
                check("没有群资料时不会回退到同名好友",Has(RecommendationMemory.Fields(store,Group('a','f',now),now),"",""));

                var controller=new RecommendationController{Connected=true};Dictionary<string,object> sent=null;int cancelled=0;
                controller.Generate+=(id,messages)=>{sent=RecommendationMemory.Fields(store,controller.Current,now);};
                controller.Invalidated+=delegate{cancelled++;};controller.Observe(a,now);
                check("群记忆不会让打开群自动触发生成",controller.PendingId==null);
                check("点击群生成时捕获该群记忆",controller.RequestGroupRecommendation(now)&&Has(sent,"保存后的甲群偏好","甲群周五前登记"));
                string oldId=controller.PendingId;store.SetGroupAgreements(store.CurrentGroup(a),"改为周四登记");controller.Invalidate();
                check("保存群约定后取消旧请求并拒绝旧结果",cancelled>0&&controller.PendingId==null&&!controller.Complete(oldId,"旧约定回复",now));
                check("再次点击群使用新约定",controller.RequestGroupRecommendation(now)&&Has(sent,"保存后的甲群偏好","改为周四登记"));
                oldId=controller.PendingId;controller.Observe(b,now);
                check("生成期间切群丢弃前一群结果",controller.PendingId==null&&!controller.Complete(oldId,"甲群回复",now));
                check("切群后下一次点击仅使用新群约定",controller.RequestGroupRecommendation(now)&&Has(sent,"乙群活泼一些","乙群周日讨论"));
                oldId=controller.PendingId;controller.Observe(other,now);
                check("生成期间换号拒绝原账号结果",controller.PendingId==null&&!controller.Complete(oldId,"旧账号回复",now));
                check("换号后的点击仅使用当前账号群偏好",controller.RequestGroupRecommendation(now)&&Has(sent,"另一个账号的偏好",""));
                store.SetEnabled(false,now);check("关闭全局记忆后群推荐不附带记忆",Has(RecommendationMemory.Fields(store,other,now),"",""));
            } finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
    }
}
