using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace WeChatPet.Runtime {
    public static class MemoryRecoveryTests {
        static ChatSnapshot Chat(){return new ChatSnapshot{Available=true,AtEnd=true,AccountId=new string('a',64),FriendId=new string('b',64),Contact="合成好友",Messages=new List<ChatMessage>()};}
        public static void Run(Action<string,bool> check) {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"memory-recovery-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                long now=DateTime.UtcNow.Ticks;var chat=Chat();
                string capacity=Path.Combine(root,"capacity");Directory.CreateDirectory(capacity);
                var store=new MemoryStore(capacity);store.Observe(chat,now);var friend=store.Current(chat);store.SetPreferences(friend,"合成回复偏好");
                FriendMemory last=null;
                // Valid per-friend bounds can still exceed the global byte budget.
                // Keep the excess below one friend's seen list, so clearing just
                // that friend must recover without deleting everyone else.
                for(int i=0;i<229;i++) {
                    last=new FriendMemory{Id=i.ToString("x64"),AccountId=chat.AccountId,Name="合成好友"};
                    for(int j=0;j<1024;j++)last.Seen.Add(j.ToString("x64"));
                    store.Data.Friends.Add(last);
                }
                store.SetPreferences(friend,"合成更新偏好");
                check("合法好友与去重记录达到总容量时明确暂停",store.Error!=null&&store.Error.Contains("容量")&&store.Context(chat,now)=="");
                store.ClearFriend(last,now+1);
                check("容量错误后清理单个好友恢复保存且保留原有偏好",store.Error==null&&last.Seen.Count==0&&store.Data.Friends.Count==230&&friend.Preferences=="合成回复偏好");
                store.SetPreferences(friend,"合成更新偏好");
                var reopened=new MemoryStore(capacity);
                check("容量恢复后的删除在重启后仍然有效",reopened.Error==null&&reopened.Find(last.AccountId,last.Id).Seen.Count==0&&reopened.Current(chat).Preferences=="合成更新偏好");
                store.ClearAll(now+2);
                check("清空全部已经持久化才清除界面集合",store.Error==null&&store.Data.Friends.Count==0&&new MemoryStore(capacity).Data.Friends.Count==0);

                string rosterLimit=Path.Combine(root,"roster-limit");Directory.CreateDirectory(rosterLimit);
                store=new MemoryStore(rosterLimit);store.Observe(chat,now);friend=store.Current(chat);store.SetPreferences(friend,"已有好友设定");
                for(int i=0;i<499;i++)store.Data.Friends.Add(new FriendMemory{Id=i.ToString("x64"),AccountId=chat.AccountId,Name="合成好友"});
                var newFriend=Chat();newFriend.FriendId=new string('d',64);store.Observe(newFriend,now);
                check("好友列表满时明确提示且不产生第501条",store.Current(newFriend)==null&&store.Data.Friends.Count==500&&store.CapacityNotice(newFriend).Contains("好友记录已满"));
                check("列表容量提示不暂停已有好友推荐与编辑",store.Error==null&&store.CanEdit&&store.Profile(chat,now).ReplyPreferences=="已有好友设定"&&store.CapacityNotice(chat)==null);
                var otherAccount=Chat();otherAccount.AccountId=new string('e',64);
                check("本机容量跨账号达到上限时引导切回已有记录账号",store.CapacityNotice(otherAccount).Contains("切换到有记录的账号")&&store.Profile(otherAccount,now).ReplyPreferences=="");
                store.Data.Enabled=false;check("暂停记忆时不显示新增容量提示",store.CapacityNotice(newFriend)==null);store.Data.Enabled=true;
                check("身份不明确时不残留容量提示",store.CapacityNotice(null)==null&&store.CapacityNotice(new ChatSnapshot())==null);
                store.RemoveFriend(friend,now+1);
                check("移除已有好友后容量提示自动消退",store.Error==null&&store.CapacityNotice(newFriend)==null);
                store.Observe(newFriend,now+2);store.Save();
                check("腾出容量后新好友可正常保存并重启读取",store.Current(newFriend)!=null&&store.Data.Friends.Count==500&&new MemoryStore(rosterLimit).Current(newFriend)!=null);

                string groupLimit=Path.Combine(root,"group-limit");Directory.CreateDirectory(groupLimit);store=new MemoryStore(groupLimit);
                var groupChat=new ChatSnapshot{Available=true,GroupChat=true,AccountId=chat.AccountId,GroupId=new string('b',64),Contact="合成群"};
                store.ObserveGroup(groupChat,now);var groupMemory=store.CurrentGroup(groupChat);store.SetGroupPreferences(groupMemory,"已有群偏好");
                for(int i=0;i<499;i++)store.Data.Groups.Add(new GroupMemory{Id=i.ToString("x64"),AccountId=new string('c',64),Name="合成群"});
                var newGroup=new ChatSnapshot{Available=true,GroupChat=true,AccountId=chat.AccountId,GroupId=new string('d',64),Contact="新增合成群"};store.ObserveGroup(newGroup,now);
                check("群列表满时显示容量且已有群偏好继续使用",store.CurrentGroup(newGroup)==null&&store.Data.Groups.Count==500&&store.CapacityNotice(newGroup).Contains("群聊记录已满")&&store.Error==null&&store.GroupContext(groupChat).ReplyPreferences=="已有群偏好"&&store.CapacityNotice(groupChat)==null);
                store.ClearGroup(groupMemory);
                check("仅清空群内容不错误宣称已释放记录容量",store.Data.Groups.Count==500&&store.CapacityNotice(newGroup)!=null);
                store.ClearAccount(chat.AccountId,now+1);
                check("清理当前账号后群容量恢复且其他账号记录保留",store.CapacityNotice(newGroup)==null&&store.Data.Groups.Count==499&&store.Data.Groups.All(g=>g.AccountId==new string('c',64)));
                store.ObserveGroup(newGroup,now+2);
                check("群容量恢复后可记录新群且重启可用",store.Error==null&&store.CurrentGroup(newGroup)!=null&&new MemoryStore(groupLimit).CurrentGroup(newGroup)!=null);

                string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);
                store=new MemoryStore(blocked);store.Observe(chat,now);friend=store.Current(chat);store.Put(friend,null,"合成记忆",false,now);
                var item=friend.Items.Single();string path=Path.Combine(blocked,"memory.dpapi");byte[] original=File.ReadAllBytes(path);
                // Occupy only the isolated temporary destination. No live file
                // permissions, credential vaults or user memories are touched.
                Directory.CreateDirectory(path+".tmp");
                store.Remove(friend,item);
                check("删除写入失败保留原集合与原磁盘内容",store.Error!=null&&friend.Items.Contains(item)&&original.SequenceEqual(File.ReadAllBytes(path)));
                store.ClearFriend(friend,now+3);
                check("清空好友写入失败不伪装为空档案",store.Error!=null&&friend.Items.Contains(item)&&store.Current(chat)==friend&&original.SequenceEqual(File.ReadAllBytes(path)));
                store.ClearAll(now+4);
                check("清空全部写入失败不清掉可见好友",store.Error!=null&&store.Data.Friends.Count==1&&store.Current(chat)==friend&&original.SequenceEqual(File.ReadAllBytes(path)));
                Directory.Delete(path+".tmp");
                store.Remove(friend,item);
                check("存储恢复后可重试删除并解除错误",store.Error==null&&friend.Items.Count==0&&new MemoryStore(blocked).Current(chat).Items.Count==0);
                store.Put(friend,null,"第二条合成记忆",false,now+5);Directory.CreateDirectory(path+".tmp");store.ClearAll(now+6);Directory.Delete(path+".tmp");store.ClearAll(now+7);
                check("失败的清空全部可安全重试且重启不复活",store.Error==null&&store.Data.Friends.Count==0&&new MemoryStore(blocked).Data.Friends.Count==0);

                string editing=Path.Combine(root,"editing");Directory.CreateDirectory(editing);
                store=new MemoryStore(editing);store.Observe(chat,now);friend=store.Current(chat);store.SetPreferences(friend,"原回复偏好");store.SetExpressionExamples(friend,new[]{"原表达示例"});store.Put(friend,null,"我不吃香菜",true,now);
                item=friend.Items.Single();path=Path.Combine(editing,"memory.dpapi");original=File.ReadAllBytes(path);
                var originalHead=friend.PreferenceHeads["吃:香菜"];Directory.CreateDirectory(path+".tmp");
                store.Put(friend,item,"我现在吃香菜了",true,now+1);
                check("记忆编辑失败不改变正文或偏好冲突水位",store.Error!=null&&item.Text=="我不吃香菜"&&Object.ReferenceEquals(friend.Items.Single(),item)&&Object.ReferenceEquals(friend.PreferenceHeads["吃:香菜"],originalHead)&&original.SequenceEqual(File.ReadAllBytes(path)));
                store.Put(friend,null,"添加失败的备注",false,now+2);
                check("失败后继续添加不产生未落盘条目",friend.Items.Count==1&&friend.Items.All(i=>i.Text!="添加失败的备注"));
                store.SetPreferences(friend,"未保存的回复偏好");store.SetExpressionExamples(friend,new[]{"未保存的表达示例"});
                check("回复偏好和表达示例失败保留已保存值",friend.Preferences=="原回复偏好"&&friend.ExpressionExamples.Single()=="原表达示例"&&store.Context(chat,now)=="");
                Directory.Delete(path+".tmp");store.Put(friend,item,"我现在吃香菜了",true,now+3);
                check("存储恢复后原编辑可显式重试成功且保持条目引用",store.Error==null&&Object.ReferenceEquals(friend.Items.Single(),item)&&item.Text=="我现在吃香菜了"&&new MemoryStore(editing).Current(chat).Items.Single().Text==item.Text);
                Directory.CreateDirectory(path+".tmp");store.SetPreferences(friend,"重试保存的回复偏好");Directory.Delete(path+".tmp");store.SetPreferences(friend,"重试保存的回复偏好");
                check("回复偏好可从写入失败中重试恢复",store.Error==null&&friend.Preferences=="重试保存的回复偏好"&&new MemoryStore(editing).Current(chat).Preferences==friend.Preferences);
                Directory.CreateDirectory(path+".tmp");store.SetExpressionExamples(friend,new[]{"重试保存的表达示例"});Directory.Delete(path+".tmp");store.SetExpressionExamples(friend,new[]{"重试保存的表达示例"});
                check("表达示例可从写入失败中重试恢复",store.Error==null&&friend.ExpressionExamples.Single()=="重试保存的表达示例"&&new MemoryStore(editing).Current(chat).ExpressionExamples.SequenceEqual(friend.ExpressionExamples));

                string switches=Path.Combine(root,"switches");Directory.CreateDirectory(switches);
                store=new MemoryStore(switches);store.Observe(chat,now);store.Save();friend=store.Current(chat);path=Path.Combine(switches,"memory.dpapi");original=File.ReadAllBytes(path);
                long globalCutoff=store.Data.IgnoreBefore,friendCutoff=friend.IgnoreBefore;
                Directory.CreateDirectory(path+".tmp");store.SetEnabled(false,now+8);
                check("总开关保存失败保留已提交状态与遗忘边界",store.Error!=null&&store.Data.Enabled&&store.Data.IgnoreBefore==globalCutoff&&original.SequenceEqual(File.ReadAllBytes(path)));
                Directory.Delete(path+".tmp");store.SetEnabled(false,now+9);
                check("总开关可在存储恢复后直接重试并持久化",store.Error==null&&!store.Data.Enabled&&store.Data.IgnoreBefore==now+9&&!new MemoryStore(switches).Data.Enabled);
                store.SetEnabled(true,now+10);original=File.ReadAllBytes(path);Directory.CreateDirectory(path+".tmp");store.SetFriendEnabled(friend,false,now+11);
                check("好友开关保存失败保留已提交状态与遗忘边界",store.Error!=null&&friend.Enabled&&friend.IgnoreBefore==friendCutoff&&original.SequenceEqual(File.ReadAllBytes(path)));
                Directory.Delete(path+".tmp");store.SetFriendEnabled(friend,false,now+12);
                check("好友开关可直接重试且重启仍保持关闭",store.Error==null&&!friend.Enabled&&friend.IgnoreBefore==now+12&&!new MemoryStore(switches).Current(chat).Enabled);

                string broken=Path.Combine(root,"broken");Directory.CreateDirectory(broken);path=Path.Combine(broken,"memory.dpapi");
                File.WriteAllText(path,"synthetic-corrupt-memory");original=File.ReadAllBytes(path);store=new MemoryStore(broken);store.ClearAll(now);
                check("读取损坏不能借清理恢复路径覆盖原文件",store.Error!=null&&original.SequenceEqual(File.ReadAllBytes(path)));

                string malformed=Path.Combine(root,"malformed");Directory.CreateDirectory(malformed);path=Path.Combine(malformed,"memory.dpapi");
                var mutations=new Action<MemoryData>[] {
                    data=>data.IgnoreBefore=-1,
                    data=>data.Friends[0].IgnoreBefore=Int64.MaxValue,
                    data=>data.Friends[0].UpdatedTicks=Int64.MaxValue,
                    data=>data.Friends[0].Items[0].UpdatedTicks=Int64.MaxValue,
                    data=>data.Friends[0].Items[0].SourceOrder=-1,
                    data=>data.Friends.Add(new FriendMemory{AccountId=chat.AccountId,Id=chat.FriendId,Name="同身份的重复档案",Preferences="不应被悄悄选中"})
                };
                for(int index=0;index<mutations.Length;index++) {
                    var data=new MemoryData();var profile=new FriendMemory{AccountId=chat.AccountId,Id=chat.FriendId,Name="合成好友",Preferences="合成设定"};data.Friends.Add(profile);
                    profile.Items.Add(new MemoryItem{Id="manual-note",Text="用户确认的合成备注",Manual=true,SubjectRole="peer",SourceTicks=now,UpdatedTicks=now,Kind="note"});
                    mutations[index](data);LocalStore.Write(path,new JavaScriptSerializer().Serialize(data),true);original=File.ReadAllBytes(path);
                    store=new MemoryStore(malformed);
                    bool stopped=store.Error!=null&&!store.CanEdit&&store.Profile(chat,now).FriendNotes.Count==0&&store.Context(chat,now)=="";
                    store.ClearAll(now+1);
                    check("无效时间顺序或重复好友身份在载入时停止且不覆盖原文件："+index,stopped&&original.SequenceEqual(File.ReadAllBytes(path)));
                }
                var separate=new MemoryData();separate.Friends.Add(new FriendMemory{AccountId=chat.AccountId,Id=chat.FriendId,Name="同名",Preferences="甲账号设定"});
                separate.Friends.Add(new FriendMemory{AccountId=new string('c',64),Id=chat.FriendId,Name="同名",Preferences="乙账号设定"});
                LocalStore.Write(path,new JavaScriptSerializer().Serialize(separate),true);store=new MemoryStore(malformed);
                check("不同账号的同好友标识不是重复档案且仍独立读取",store.Error==null&&store.Data.Friends.Count==2&&store.Profile(chat,now).ReplyPreferences=="甲账号设定");
            } finally {Directory.Delete(root,true);}
        }
    }
}
