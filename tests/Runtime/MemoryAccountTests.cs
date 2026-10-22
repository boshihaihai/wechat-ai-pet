using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace WeChatPet.Runtime {
    public static class MemoryAccountTests {
        static string Id(char c){return new string(c,64);}
        static ChatSnapshot Chat(string account,string friend,long now,string text="我不吃香菜") {
            return new ChatSnapshot{Available=true,AtEnd=true,AccountId=account,FriendId=friend,Contact="同名好友",Messages=new List<ChatMessage>{new ChatMessage{Type=1,MemoryKey=Id('d'),Role="peer",Text=text,CreatedTicks=now,LocalId=1}}};
        }
        static void Add(ChatSnapshot chat,char key,long ticks,string text,string role="peer") {
            chat.Messages.Add(new ChatMessage{Type=1,MemoryKey=Id(key),Role=role,Text=text,CreatedTicks=ticks,LocalId=(uint)(chat.Messages.Count+1)});
        }
        static string Serialized(object value){return new JavaScriptSerializer().Serialize(value);}
        public static void Run(Action<string,bool> check) {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"memory-account-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try {
                long now=DateTime.UtcNow.Ticks,second=TimeSpan.TicksPerSecond;
                var a=Chat(Id('a'),Id('b'),now);var b=Chat(Id('c'),Id('b'),now,"我喜欢吃辣");var other=Chat(Id('a'),Id('e'),now,"我不喝咖啡");
                var store=new MemoryStore(root);store.Observe(a,now);store.Observe(b,now);store.Observe(other,now);
                var fa=store.Current(a);var fb=store.Current(b);var fo=store.Current(other);
                store.SetPreferences(fa,"A账号的亲昵语气");store.SetExpressionExamples(fa,new[]{"A专属示例"});
                store.SetPreferences(fb,"B账号的正式语气");store.SetExpressionExamples(fb,new[]{"B专属示例"});
                store.SetPreferences(fo,"A另一好友语气");store.SetExpressionExamples(fo,new[]{"另一好友示例"});
                check("同昵称同好友标识跨微信账号仍隔离偏好记忆和示例",store.Context(a,now).Contains("亲昵")&&!store.Context(a,now).Contains("吃辣")&&store.Context(b,now).Contains("正式")&&!store.Context(b,now).Contains("香菜")&&store.ExpressionExamples(a).Single()=="A专属示例"&&store.ExpressionExamples(b).Single()=="B专属示例");
                var aContext=store.Context(a,now);var bContext=store.Context(b,now);var otherContext=store.Context(other,now);
                bool isolated=true;for(int i=0;i<20;i++)isolated&=store.Context(a,now)==aContext&&store.Context(b,now)==bContext&&store.Context(other,now)==otherContext;
                check("重复换号和切换同名好友不串用任何上下文",isolated);
                string preservedB=Serialized(fb),preservedOther=Serialized(fo);
                check("移除当前账号好友在成功持久化后删除资料",store.RemoveFriend(fa,now+second)&&store.Current(a)==null&&store.Data.RemovedFriends.Count==1);
                check("移除好友不修改同名其他账号及同账号其他好友",Serialized(store.Current(b))==preservedB&&Serialized(store.Current(other))==preservedOther);
                store.Observe(a,now+2*second);store.Save();
                check("移除后持续观察旧聊天不重建列表或重新学习",store.Current(a)==null&&store.Context(a,now)==""&&store.ExpressionExamples(a).Length==0);
                store=new MemoryStore(root);store.Observe(a,now+2*second);
                check("移除标记跨重启保留且仍然屏蔽旧聊天",store.Error==null&&store.Current(a)==null&&store.Data.RemovedFriends.Count==1);
                Add(a,'f',now+2*second,"自己新发出的消息","self");store.Observe(a,now+2*second);
                check("只有本人新消息不会恢复已移除的好友记忆",store.Current(a)==null);
                Add(a,'1',now+3*second,"我现在吃香菜了");store.Observe(a,now+3*second);store.Save();
                fa=store.Current(a);
                check("明确新来信重新建立空偏好好友且不回填删除前历史",fa!=null&&fa.Preferences==""&&fa.ExpressionExamples.Count==0&&fa.Items.All(i=>i.SourceTicks>now+second)&&fa.Items.Any(i=>i.Text=="我现在吃香菜了")&&!fa.Items.Any(i=>i.Text=="我不吃香菜")&&store.Data.RemovedFriends.Count==0);
                store=new MemoryStore(root);store.Observe(a,now+4*second);
                check("重新建立后的截止时间跨重启防止旧偏好回流",!store.Context(a,now+4*second).Contains("我不吃香菜")&&!store.Context(a,now+4*second).Contains("亲昵")&&store.ExpressionExamples(a).Length==0);
                preservedB=Serialized(store.Current(b));long globalCutoff=store.Data.IgnoreBefore;
                check("账号清空删除该账号所有好友并不改全局截止时间",store.ClearAccount(a.AccountId,now+5*second)&&store.Data.Friends.All(f=>f.AccountId!=a.AccountId)&&store.Data.IgnoreBefore==globalCutoff);
                check("账号清空保留另一个账号的全部原样资料",Serialized(store.Current(b))==preservedB&&store.Context(b,now+5*second).Contains("吃辣")&&store.ExpressionExamples(b).Single()=="B专属示例");
                store.Observe(a,now+6*second);store.Observe(other,now+6*second);store.Save();store=new MemoryStore(root);store.Observe(a,now+6*second);store.Observe(other,now+6*second);
                check("账号清空跨重启屏蔽本账号所有旧好友历史",store.Error==null&&store.Current(a)==null&&store.Current(other)==null&&store.Current(b)!=null);
                Add(a,'2',now+7*second,"我喜欢清淡");store.Observe(a,now+7*second);store.Save();
                check("账号清空之后的新来信仅恢复对应好友的新记忆",store.Current(a)!=null&&store.Current(other)==null&&store.Current(a).Items.Count==1&&store.Current(a).Items.Single().Text=="我喜欢清淡"&&Serialized(store.Current(b))==preservedB);
                check("无效账号标识不会清空其它账号",!store.ClearAccount("",now)&&!store.ClearAccount(null,now)&&Serialized(store.Current(b))==preservedB);
                var foreign=new FriendMemory{AccountId=b.AccountId,Id=b.FriendId};check("脱离存储的同标识对象不能删除好友",!store.RemoveFriend(foreign,now)&&store.Current(b)!=null);

                // Block only the isolated temporary save path. No user data or
                // live file permissions are read or changed by these checks.
                string path=Path.Combine(root,"memory.dpapi");byte[] disk=File.ReadAllBytes(path);var before=store.Data;fb=store.Current(b);
                Directory.CreateDirectory(path+".tmp");
                check("移除好友写入失败保留集合及磁盘原件",!store.RemoveFriend(fb,now+8*second)&&store.Error!=null&&Object.ReferenceEquals(store.Data,before)&&Object.ReferenceEquals(store.Current(b),fb)&&disk.SequenceEqual(File.ReadAllBytes(path)));
                check("账号清空写入失败不伪装成功或删除其它账号",!store.ClearAccount(b.AccountId,now+8*second)&&Object.ReferenceEquals(store.Data,before)&&store.Current(a)!=null&&Object.ReferenceEquals(store.Current(b),fb)&&disk.SequenceEqual(File.ReadAllBytes(path)));
                Directory.Delete(path+".tmp");
                check("存储恢复可重试账号清空并解除错误",store.ClearAccount(b.AccountId,now+9*second)&&store.Error==null&&store.Current(b)==null&&store.Current(a)!=null);
                store=new MemoryStore(root);store.Observe(b,now+10*second);check("失败后重试清空已可靠落盘且不复活旧数据",store.Current(b)==null&&store.Current(a)!=null&&store.Error==null);
                fa=store.Current(a);disk=File.ReadAllBytes(path);Directory.CreateDirectory(path+".tmp");store.RemoveFriend(fa,now+10*second);Directory.Delete(path+".tmp");
                check("存储恢复可重试移除好友且成功持久化",store.RemoveFriend(fa,now+11*second)&&store.Error==null&&store.Current(a)==null&&new MemoryStore(root).Current(a)==null&&!disk.SequenceEqual(File.ReadAllBytes(path)));

                string bounded=Path.Combine(root,"bounded");Directory.CreateDirectory(bounded);var limit=new MemoryStore(bounded);limit.Observe(a,now+7*second);limit.Save();
                for(int i=0;i<5000;i++)limit.Data.RemovedFriends.Add(new RemovedFriendMemory{AccountId=Id('9'),Id=i.ToString("x64"),IgnoreBefore=now});
                var retained=limit.Current(a);
                check("删除标记达到上限不静默淘汰而保留待删好友",!limit.RemoveFriend(retained,now+second)&&limit.Current(a)==retained&&limit.Data.RemovedFriends.Count==5000&&limit.Error!=null);
                check("标记已满仍可按账号清空完成安全删除",limit.ClearAccount(a.AccountId,now+second)&&limit.Error==null&&limit.Current(a)==null&&limit.Data.RemovedFriends.Count==5000);
                foreach(var marker in limit.Data.RemovedFriends)marker.IgnoreBefore=now-31*TimeSpan.TicksPerDay;
                Add(a,'3',now+11*second,"新消息");limit.Observe(a,now+11*second);
                check("删除标记超过三十天仍不自动淘汰",!limit.RemoveFriend(limit.Current(a),now+12*second)&&limit.Data.RemovedFriends.Count==5000);
                check("清空账号合并该账号删除标记释放容量",limit.ClearAccount(Id('9'),now+12*second)&&limit.Data.RemovedFriends.Count==0&&limit.Current(a)!=null);
                limit.RemoveFriend(limit.Current(a),now+13*second);
                limit=new MemoryStore(bounded);check("新增账号和好友删除标记可加密回读",limit.Error==null&&limit.Data.RemovedFriends.Count==1&&limit.Data.AccountIgnoreBefore.ContainsKey(a.AccountId));
                long later=now+32*TimeSpan.TicksPerDay;limit.Observe(a,later);
                check("超过三十天打开旧聊天仍不会恢复已删除好友",limit.Current(a)==null&&limit.Data.RemovedFriends.Count==1);
                var unknown=Chat(Id('9'),Id('7'),now);limit.Observe(unknown,later);
                check("超过三十天账号清空屏障仍阻止旧好友重建",limit.Current(unknown)==null);
                Add(a,'4',later,"我现在喜欢茶");limit.Observe(a,later);limit.Save();
                check("超过三十天的新对方来信仍可恢复且只学新消息",limit.Current(a)!=null&&limit.Current(a).Items.Count==1&&limit.Current(a).Items.Single().Text=="我现在喜欢茶"&&limit.Current(a).Preferences==""&&limit.Data.RemovedFriends.Count==0);

                string capacity=Path.Combine(root,"capacity");Directory.CreateDirectory(capacity);var cap=new MemoryStore(capacity);cap.Observe(a,now+11*second);
                for(int i=0;i<499;i++)cap.Data.Friends.Add(new FriendMemory{AccountId=Id('8'),Id=i.ToString("x64"),Name="合成容量好友"});
                var pending=Chat(Id('8'),Id('f'),now);cap.Observe(pending,now+11*second);
                check("五百好友上限拒绝新增而不覆盖已有好友",cap.Data.Friends.Count==500&&cap.Current(a)!=null&&cap.Current(pending)==null);
                cap.RemoveFriend(cap.Current(a),now+12*second);cap.Observe(pending,now+12*second);cap.Save();
                check("移除好友释放容量且新好友不继承被删偏好",cap.Data.Friends.Count==500&&cap.Current(pending)!=null&&cap.Current(pending).Preferences==""&&cap.Current(a)==null);
                for(int i=0;i<500;i++)cap.Data.AccountIgnoreBefore.Add((i+1000).ToString("x64"),now);
                before=cap.Data;
                check("账号删除标记容量满时拒绝清空并保留数据",!cap.ClearAccount(pending.AccountId,now+12*second)&&Object.ReferenceEquals(cap.Data,before)&&cap.Current(pending)!=null&&cap.Error!=null);
                foreach(var key in cap.Data.AccountIgnoreBefore.Keys.ToList())cap.Data.AccountIgnoreBefore[key]=now-31*TimeSpan.TicksPerDay;
                check("账号屏障超过三十天也不淘汰导致旧好友复活",!cap.ClearAccount(pending.AccountId,now+13*second)&&cap.Data.Friends.Count==500&&cap.Data.AccountIgnoreBefore.Count==500);
                string tracked=cap.Data.AccountIgnoreBefore.Keys.First();check("账号屏障达到容量后仍可更新已存在账号",cap.ClearAccount(tracked,now+13*second)&&cap.Error==null&&cap.Data.AccountIgnoreBefore.Count==500&&cap.Data.Friends.Count==500);
            } finally {Directory.Delete(root,true);}
        }
    }
}
