using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WeChatPet.Runtime {
    public static class FriendProfileTests {
        static string Id(char value){return new string(value,64);}
        static ChatSnapshot Snapshot(char account,char friend){return new ChatSnapshot{Available=true,AtEnd=true,AccountId=Id(account),FriendId=Id(friend),Contact="ShuY（合成测试）",Messages=new List<ChatMessage>()};}
        static Dictionary<string,object> Profile(Dictionary<string,object> fields){return (Dictionary<string,object>)fields["friendProfile"];}
        public static void Run(Action<string,bool> check) {
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"friend-profile-test-"+Guid.NewGuid().ToString("N"));
            try {
                var store=new MemoryStore(directory);long now=DateTime.UtcNow.Ticks;
                var current=Snapshot('a','b');store.Observe(current,now);var friend=store.Current(current);
                var legacy=new MemoryItem{Id="legacy",Text="温柔大方，喜欢同频交流",Kind="note",Manual=true,SourceTicks=now,UpdatedTicks=now};friend.Items.Add(legacy);
                check("未确认的旧备注不偷偷恢复为好友事实",store.Profile(current,now).FriendNotes.Count==0);
                store.Put(friend,legacy,legacy.Text,false,now+1);
                store.SetPreferences(friend,"自然亲近，少说教，不使用固定昵称");
                var request=RecommendationMemory.Fields(store,current,now+2);var profile=Profile(request);
                check("确认旧备注后单独提供好友信息",((string[])profile["friendNotes"]).Single().Contains(legacy.Text));
                check("回复风格单独提供而非埋入近期聊天",(string)profile["replyPreferences"]==friend.Preferences&&!((string)request["memory"]).Contains(friend.Preferences));
                check("确认备注不重复混入近期聊天",!((string)request["memory"]).Contains(legacy.Text));
                var mine=new MemoryItem{Id="self",Text="我喜欢安静",Kind="recent",SourceRole="self",SourceText="我喜欢安静",SourceTicks=now,UpdatedTicks=now};friend.Items.Add(mine);
                store.Put(friend,mine,mine.Text,true,now+3);
                var selfProfile=store.Profile(current,now+4);
                check("自己发出的偏好始终属于本人",selfProfile.SelfNotes.Any(x=>x.Contains(mine.Text))&&!selfProfile.FriendNotes.Any(x=>x.Contains(mine.Text)));
                var sameName=Snapshot('a','c');store.Observe(sameName,now);store.SetPreferences(store.Current(sameName),"另一好友的措辞");
                var otherAccount=Snapshot('d','b');store.Observe(otherAccount,now);store.SetPreferences(store.Current(otherAccount),"另一账号的措辞");
                check("同名好友独立取用自己的风格",store.Profile(sameName,now).ReplyPreferences=="另一好友的措辞"&&store.Profile(sameName,now).FriendNotes.Count==0);
                check("同好友标识换号也不混用资料",store.Profile(otherAccount,now).ReplyPreferences=="另一账号的措辞"&&store.Profile(otherAccount,now).FriendNotes.Count==0);
                store.SetPreferences(friend,"已修改的风格");store.Put(friend,legacy,"更新后的好友信息",false,now+5);
                check("生成中的风格和好友信息是独立快照",(string)profile["replyPreferences"]=="自然亲近，少说教，不使用固定昵称"&&((string[])profile["friendNotes"])[0].Contains("温柔大方"));
                var group=new ChatSnapshot{Available=true,GroupChat=true,AccountId=current.AccountId,GroupId=friend.Id};
                check("群请求从不附带好友资料",!RecommendationMemory.Fields(store,group,now).ContainsKey("friendProfile")&&store.Profile(group,now).FriendNotes.Count==0);
                store.SetFriendEnabled(friend,false,now);check("好友记忆暂停同时停用风格与资料",store.Profile(current,now).ReplyPreferences==""&&store.Profile(current,now).FriendNotes.Count==0);store.SetFriendEnabled(friend,true,now);
                store.SetEnabled(false,now);check("总开关暂停不附带任何好友资料",store.Profile(current,now).ReplyPreferences==""&&store.Profile(current,now).SelfNotes.Count==0);store.SetEnabled(true,now);
                var unknown=Snapshot('e','f');check("未知身份不回退到同名好友",store.Profile(unknown,now).ReplyPreferences=="");
                for(int n=0;n<8;n++)store.Put(friend,null,new string((char)('甲'+n),390),false,now+n+10);
                var bounded=store.Profile(current,now+30);check("资料预算保留整条来源不切断语义",bounded.FriendNotes.Concat(bounded.SelfNotes).Sum(x=>x.Length)<=1000&&bounded.FriendNotes.All(x=>x.Length<=500));
                // Current wording changes must beat an old confirmed/pinned preference.
                var dietary=Snapshot('a','f');store.Observe(dietary,now);var eater=store.Current(dietary);store.Put(eater,null,"我不吃香菜",true,now);
                dietary.Messages.Add(new ChatMessage{Type=1,MemoryKey=Id('d'),Role="peer",Text="我现在吃香菜了",CreatedTicks=now+50,LocalId=1});store.Observe(dietary,now+50);
                var latest=store.Profile(dietary,now+60);check("独立资料仍排除已被新表达取代的旧偏好",latest.FriendNotes.Any(x=>x.Contains("现在吃香菜"))&&!latest.FriendNotes.Any(x=>x.Contains("不吃香菜")));
                var crowded=Snapshot('e','a');store.Observe(crowded,now);var crowdedFriend=store.Current(crowded);
                int noteIndex=0;foreach(int length in new[]{400,400,90})store.Put(crowdedFriend,null,new string((char)('春'+noteIndex++),length),true,now);
                crowded.Messages.Add(new ChatMessage{Type=1,MemoryKey=Id('e'),Role="peer",Text="我不吃香菜",CreatedTicks=now+100,LocalId=2});store.Observe(crowded,now+100);
                var crowdedRequest=Profile(RecommendationMemory.Fields(store,crowded,now+101));
                check("旧长固定备注不会挤掉最新明确偏好",((string[])crowdedRequest["friendNotes"]).Any(x=>x.Contains("我不吃香菜")));
            } finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
    }
}
