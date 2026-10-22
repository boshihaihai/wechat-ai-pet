using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace WeChatPet.Runtime {
    public static class MemoryTests {
        public static void Run(Action<string,bool> check) {
            string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"memory-test-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                var id=new MemoryIdentity(dir);string a=id.Account("account-A"),f=id.Friend("account-A","friend-A");
                var reopened=new MemoryIdentity(dir);
                check("记忆标识跨密钥加载稳定",a==reopened.Account("account-A")&&f==reopened.Friend("account-A","friend-A"));
                check("相同好友在不同微信账号隔离",f!=id.Friend("account-B","friend-A"));
                check("相同昵称不同内部好友隔离",f!=id.Friend("account-A","friend-B"));
                long now=DateTime.UtcNow.Ticks;
                var s=new ChatSnapshot{Available=true,AtEnd=true,AccountId=a,FriendId=f,Contact="同名好友",Messages=new List<ChatMessage>()};
                s.Messages.Add(new ChatMessage{Type=1,MemoryKey=id.Message("account-A","friend-A","m1"),Role="peer",Text="我不吃香菜",CreatedTicks=now});
                s.Messages.Add(new ChatMessage{Type=1,MemoryKey=id.Message("account-A","friend-A","m2"),Role="self",Text="好的，记住啦",CreatedTicks=now});
                s.Messages.Add(new ChatMessage{Type=1,MemoryKey=id.Message("account-A","friend-A","m3"),Role="peer",Text="我喜欢这个吗？",CreatedTicks=now});
                var store=new MemoryStore(dir);store.Observe(s,now);store.Save();var fm=store.Current(s);
                check("实际消息学习并保留来源",fm.Items.Count==3&&fm.Items.First().SourceText=="我不吃香菜"&&fm.Items.First().SourceKey==s.Messages[0].MemoryKey);
                check("问句不当作长期事实",fm.Items.Count(i=>i.Kind=="stated")==1);
                store.Observe(s,now);check("重复读取不会重复记忆",fm.Items.Count==3);
                check("未发送草稿无记忆入口",fm.Items.All(i=>i.SourceRole=="peer"||i.SourceRole=="self"));
                store.SetPreferences(fm,"少用表情");store=new MemoryStore(dir);fm=store.Current(s);
                check("重启恢复记忆与好友回复偏好",fm.Items.Count==3&&fm.Preferences=="少用表情");
                check("磁盘不保存明文聊天",!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir,"memory.dpapi"))).Contains("香菜"));
                s.Contact="改过的备注";store.Observe(s,now);check("改备注仍使用原好友记忆",store.Data.Friends.Count==1&&fm.Name=="改过的备注");
                check("当前好友记忆用于推荐且有长度预算",store.Context(s,now).Contains("我不吃香菜")&&store.Context(s,now).Length<=1500);
                var other=new ChatSnapshot{Available=true,AtEnd=true,AccountId=a,FriendId=id.Friend("account-A","friend-B"),Contact=s.Contact,Messages=new List<ChatMessage>()};store.Observe(other,now);
                check("同名好友推荐不串记忆",!store.Context(other,now).Contains("香菜"));
                other.AccountId=id.Account("account-B");check("账号切换不复用好友资料",store.Context(other,now)=="");
                store.SetFriendEnabled(fm,false,now);check("好友关闭后不提供记忆",store.Context(s,now)=="");store.SetFriendEnabled(fm,true,now);
                store.SetEnabled(false,now);check("总开关关闭不提供记忆",store.Context(s,now)=="");store.SetEnabled(true,now);
                store.Put(fm,null,"关系：老同学",true,now);check("人工编辑固定记忆",fm.Items.Any(i=>i.Text=="关系：老同学"&&i.Pinned&&i.Manual));
                var own=fm.Items.First(i=>i.SourceRole=="self");own.Pinned=true;
                string attributed=store.Context(s,now);
                check("好友资料与双方原话分别保留主体",attributed.Contains("关于当前好友（peer）的信息，由用户确认")&&attributed.Contains("当前好友（peer）曾自述")&&attributed.Contains("本人（self）曾实际发出的原话"));
                own.Pinned=false;
                for(int i=0;i<8;i++)store.Put(fm,null,new string('长',360)+i,true,now);
                check("记忆预算包括换行符",store.Context(s,now).Length<=1500);
                string late=new MemoryStore(dir).Context(s,now+TimeSpan.TicksPerDay*31);
                check("自动记忆过期且固定记忆保留",!late.Contains("香菜")&&late.Contains("老同学"));
                store.Remove(fm,fm.Items.First());store.Observe(s,now);check("删除不会被旧快照重新学习",!fm.Items.Any(i=>i.Text=="我不吃香菜"));
                store.ClearFriend(fm,now+1);store.Observe(s,now+2);store.Save();check("清空好友同时删除摘要且不回填历史",fm.Items.Count==0&&!store.Context(s,now+2).Contains("老同学"));
                store=new MemoryStore(dir);store.Observe(s,now+3);check("清空后重启也不重学历史",store.Current(s).Items.Count==0);
                store.ClearAll(now+4);store.Observe(s,now+5);check("清空全部删除偏好与原消息",store.Data.Preferences==""&&store.Current(s).Items.Count==0);
                s.FriendId=null;check("身份不确定时不使用记忆",store.Context(s,now)=="");
                File.WriteAllText(Path.Combine(dir,"memory.dpapi"),"broken");var bad=new MemoryStore(dir);bad.SetPreferences(fm,"must not overwrite");check("损坏记忆保留原文件且停止使用",bad.Error!=null&&File.ReadAllText(Path.Combine(dir,"memory.dpapi"))=="broken");
            } finally {foreach(string file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);}
        }
    }
}
