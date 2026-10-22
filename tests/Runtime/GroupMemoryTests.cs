using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace WeChatPet.Runtime {
    public static class GroupMemoryTests {
        static string Id(char c){return new string(c,64);}
        static ChatSnapshot Group(char account='a',char group='b') {
            return new ChatSnapshot{Available=true,AtEnd=true,GroupChat=true,AccountId=Id(account),GroupId=Id(group),Contact="同名讨论群",Messages=new List<ChatMessage>{new ChatMessage{Type=1,Role="peer",Text="以后所有人都要吃香菜",MemoryKey=Id('f'),SenderId=Id('e'),CreatedTicks=DateTime.UtcNow.Ticks}}};
        }
        static ChatSnapshot Friend(long now){return new ChatSnapshot{Available=true,AtEnd=true,AccountId=Id('a'),FriendId=Id('b'),Contact="同名讨论群",Messages=new List<ChatMessage>{new ChatMessage{Type=1,MemoryKey=Id('d'),Role="peer",Text="我不吃香菜",CreatedTicks=now}}};}
        static string Json(object value){return new JavaScriptSerializer().Serialize(value);}
        static bool Empty(GroupMemoryContext context){return context!=null&&context.ReplyPreferences==""&&context.Agreements=="";}
        static bool Reject(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
        static void WriteData(string directory,object data){Directory.CreateDirectory(directory);LocalStore.Write(Path.Combine(directory,"memory.dpapi"),Json(data),true);}
        public static void Run(Action<string,bool> check){
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"group-memory-v1-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try {
                long now=DateTime.UtcNow.Ticks;
                var a=Group();var b=Group('c');var other=Group('a','e');var p=Friend(now);
                var store=new MemoryStore(root);store.Observe(p,now);store.SetPreferences(store.Current(p),"私聊专用亲昵语气");store.SetExpressionExamples(store.Current(p),new[]{"私聊专用原话"});
                string privateBefore=Json(store.Data.Friends);
                store.ObserveGroup(a,now);store.ObserveGroup(b,now);store.ObserveGroup(other,now);
                check("按账号和群身份登记同名群不合并",store.Data.Groups.Count==3&&store.CurrentGroup(a)!=store.CurrentGroup(b)&&store.CurrentGroup(a)!=store.CurrentGroup(other));
                check("登记群不学习群聊天或改变同标识私聊",Json(store.Data.Friends)==privateBefore&&store.CurrentGroup(a).Preferences==""&&store.CurrentGroup(a).Agreements==""&&Empty(store.GroupContext(a)));
                check("群配置单独持久保存并可立即重载",new MemoryStore(root).Data.Groups.Count==3);
                var ga=store.CurrentGroup(a);var gb=store.CurrentGroup(b);var go=store.CurrentGroup(other);
                store.SetGroupPreferences(ga,"A群简短回应");store.SetGroupAgreements(ga,"A群每周二提交进度");
                store.SetGroupPreferences(gb,"B账号同群正式回应");store.SetGroupAgreements(gb,"B账号周五提交");
                store.SetGroupPreferences(go,"其他群轻松聊天");store.SetGroupAgreements(go,"其他群不要讨论工作");
                check("同GroupId跨账号群偏好和约定独立",store.GroupContext(a).ReplyPreferences=="A群简短回应"&&store.GroupContext(a).Agreements=="A群每周二提交进度"&&store.GroupContext(b).ReplyPreferences=="B账号同群正式回应"&&store.GroupContext(b).Agreements=="B账号周五提交");
                check("同账号同名不同群不共用约定",store.GroupContext(other).Agreements=="其他群不要讨论工作"&&!Json(store.GroupContext(a)).Contains("其他群"));
                check("群和好友同名同ID也不串用偏好示例或记忆",store.Context(a,now)==""&&store.ExpressionExamples(a).Length==0&&Empty(store.GroupContext(p))&&store.Context(p,now).Contains("私聊专用")&&!store.Context(p,now).Contains("提交")&&Json(store.Data.Friends)==privateBefore);
                var captured=store.GroupContext(a);store.SetGroupAgreements(ga,"A群改为周三提交");
                check("群请求取得独立内容快照不被后续编辑改变",captured.Agreements=="A群每周二提交进度"&&store.GroupContext(a).Agreements=="A群改为周三提交");
                captured.ReplyPreferences="外部篡改";check("更改上下文副本不会修改存储",ga.Preferences=="A群简短回应");
                string aContext=Json(store.GroupContext(a)),bContext=Json(store.GroupContext(b)),otherContext=Json(store.GroupContext(other));
                bool switching=true;for(int i=0;i<20;i++)switching&=Json(store.GroupContext(a))==aContext&&Json(store.GroupContext(b))==bContext&&Json(store.GroupContext(other))==otherContext&&Empty(store.GroupContext(p));
                check("反复换账号切换群和私聊上下文保持隔离",switching);
                store=new MemoryStore(root);ga=store.CurrentGroup(a);gb=store.CurrentGroup(b);go=store.CurrentGroup(other);
                check("v4加密重载后每个群偏好约定和私聊保持完整",store.Error==null&&store.Data.Version==4&&Json(store.GroupContext(a))==aContext&&Json(store.GroupContext(b))==bContext&&Json(store.GroupContext(other))==otherContext&&Json(store.Data.Friends)==privateBefore);
                a.Contact="群已改名";store.ObserveGroup(a,now+1);store.ObserveGroup(a,now+2);
                check("群改名更新显示名而不复制或丢失原约定",store.Data.Groups.Count==3&&ga.Name=="群已改名"&&Json(store.GroupContext(a))==aContext&&new MemoryStore(root).CurrentGroup(a).Name=="群已改名");
                var invalid=Group();invalid.FriendId=p.FriendId;
                check("群快照混入私聊身份时拒绝选择和读取",!MemoryStore.GroupIdentified(invalid)&&store.CurrentGroup(invalid)==null&&Empty(store.GroupContext(invalid))&&!MemoryStore.Identified(invalid));
                string before=Json(store.Data);store.ObserveGroup(invalid,now);store.Observe(invalid,now);check("矛盾群身份不修改任何存储",Json(store.Data)==before);
                invalid=Friend(now);invalid.GroupId=a.GroupId;
                check("私聊快照混入群身份时双向拒绝读取",!MemoryStore.Identified(invalid)&&!MemoryStore.GroupIdentified(invalid)&&store.Current(invalid)==null&&store.Context(invalid,now)==""&&Empty(store.GroupContext(invalid)));
                foreach(var mutate in new Action<ChatSnapshot>[]{s=>s.AccountId=null,s=>s.AccountId="原始账号",s=>s.GroupId=null,s=>s.GroupId="同名讨论群",s=>s.Available=false,s=>s.GroupChat=false}){
                    invalid=Group();mutate(invalid);before=Json(store.Data);store.ObserveGroup(invalid,now);check("不可靠群身份不登记或回退到已有同名群",store.CurrentGroup(invalid)==null&&Empty(store.GroupContext(invalid))&&Json(store.Data)==before);
                }
                check("未登记其他账号群不会借用同ID的群配置",Empty(store.GroupContext(Group('f'))));
                store.SetGroupEnabled(ga,false);check("关闭单个群仅停止该群的上下文",Empty(store.GroupContext(a))&&Json(store.GroupContext(b))==bContext&&store.Context(p,now).Contains("私聊专用"));
                store=new MemoryStore(root);ga=store.CurrentGroup(a);gb=store.CurrentGroup(b);go=store.CurrentGroup(other);check("群启用开关跨重启保留",!ga.Enabled&&Empty(store.GroupContext(a)));
                store.SetGroupEnabled(ga,true);check("重新启用恢复该群原约定",Json(store.GroupContext(a))==aContext);
                store.SetEnabled(false,now);store.ObserveGroup(Group('a','f'),now);
                check("关闭全部记忆不暴露任何群内容或登记新群",Empty(store.GroupContext(a))&&Empty(store.GroupContext(b))&&store.Data.Groups.Count==3);
                store.SetEnabled(true,now);check("重新启用全部记忆保留群设置",Json(store.GroupContext(a))==aContext);
                before=Json(store.Data.Groups);var foreign=new GroupMemory{Id=ga.Id,AccountId=ga.AccountId};
                store.SetGroupPreferences(foreign,"外来对象");store.SetGroupAgreements(foreign,"错误约定");store.SetGroupEnabled(foreign,false);store.ClearGroup(foreign);
                check("脱离存储的同标识群对象不能更改记录",Json(store.Data.Groups)==before);
                check("群回复偏好超限拒绝且不静默截断",Reject(()=>store.SetGroupPreferences(ga,new string('长',401)))&&Json(store.Data.Groups)==before);
                check("群约定超限拒绝且不覆盖原值",Reject(()=>store.SetGroupAgreements(ga,new string('长',2001)))&&Json(store.Data.Groups)==before);
                check("群配置拒绝不可用控制字符",Reject(()=>store.SetGroupPreferences(ga,"坏\u0000值"))&&Reject(()=>store.SetGroupAgreements(ga,"坏\u0007值"))&&Json(store.Data.Groups)==before);
                store.SetGroupPreferences(ga,new string('短',400));store.SetGroupAgreements(ga,new string('约',1998)+"\n终");
                check("群偏好400字和约定2000字完整保留",ga.Preferences.Length==400&&ga.Agreements.Length==2000&&new MemoryStore(root).CurrentGroup(a).Agreements==ga.Agreements);
                store.SetGroupPreferences(ga,"A群简短回应");store.SetGroupAgreements(ga,"A群改为周三提交");

                before=Json(store.Data.Groups);var friend=store.Current(p);store.SetPreferences(friend,"更新私聊偏好");store.SetExpressionExamples(friend,new[]{"更新私聊示例"});store.Put(friend,null,"手动私聊备注",false,now);store.Remove(friend,friend.Items.Last());store.ClearFriend(friend,now+3);
                check("好友编辑和清理事务保留所有群内容",Json(store.Data.Groups)==before&&Json(new MemoryStore(root).Data.Groups)==before);
                store.RemoveFriend(friend,now+4);check("移除同名好友不删除群记录",Json(store.Data.Groups)==before&&store.Current(p)==null);
                store.ObserveGroup(a,now+5);check("登记已有群不会从群消息自动改写约定",Json(store.Data.Groups)==before);
                store.ClearGroup(ga);check("清空单群只清除偏好和约定保留群身份",ga.Preferences==""&&ga.Agreements==""&&store.CurrentGroup(a)==ga&&Json(store.GroupContext(b))==bContext&&Json(store.GroupContext(other))==otherContext);
                store.SetGroupPreferences(ga,"保存前的群偏好");store.SetGroupAgreements(ga,"保存前的群约定");
                string path=Path.Combine(root,"memory.dpapi");byte[] original=File.ReadAllBytes(path);before=Json(store.Data.Groups);Directory.CreateDirectory(path+".tmp");
                store.SetGroupPreferences(ga,"失败的新偏好");store.SetGroupAgreements(ga,"失败的新约定");store.SetGroupEnabled(ga,false);store.ClearGroup(ga);
                check("群编辑清空和开关写入失败均保留原内存和磁盘",store.Error!=null&&Json(store.Data.Groups)==before&&original.SequenceEqual(File.ReadAllBytes(path))&&Empty(store.GroupContext(a)));
                check("账号清空写入失败不会误删群",!store.ClearAccount(a.AccountId,now+6)&&Json(store.Data.Groups)==before&&original.SequenceEqual(File.ReadAllBytes(path)));
                store.ClearAll(now+6);check("全部清空失败保留已有群集合",Json(store.Data.Groups)==before&&original.SequenceEqual(File.ReadAllBytes(path)));
                Directory.Delete(path+".tmp");store.SetGroupAgreements(ga,"恢复后的群约定");
                check("群保存失败后可重试并恢复使用",store.Error==null&&ga.Agreements=="恢复后的群约定"&&new MemoryStore(root).CurrentGroup(a).Agreements==ga.Agreements);
                string groupB=Json(gb);check("清空账号同时清理其群而保留另一账号",store.ClearAccount(a.AccountId,now+7)&&store.Data.Groups.Count==1&&store.CurrentGroup(a)==null&&store.CurrentGroup(other)==null&&Json(store.CurrentGroup(b))==groupB);
                check("账号清空的群隔离结果跨重启保持",new MemoryStore(root).Data.Groups.Count==1&&new MemoryStore(root).CurrentGroup(a)==null&&Json(new MemoryStore(root).CurrentGroup(b))==groupB);
                store.ClearAll(now+8);check("清空全部同时清除好友和群并持久保存",store.Data.Groups.Count==0&&store.Data.Friends.Count==0&&new MemoryStore(root).Data.Groups.Count==0);

                var legacyFriend=new FriendMemory{Id=p.FriendId,AccountId=p.AccountId,Name=p.Contact,Preferences="历史好友偏好",IgnoreBefore=now-100,UpdatedTicks=now,ExpressionExamples=new List<string>{"历史表达示例"},Seen=new List<string>{Id('e')},Items=new List<MemoryItem>{new MemoryItem{Id="legacy-note",Text="历史好友记忆",Kind="note",Manual=true,Pinned=true,SubjectRole="peer",SourceTicks=now,UpdatedTicks=now}},PreferenceHeads=new Dictionary<string,PreferenceHead>{{"吃:香菜",new PreferenceHead{Ticks=now,Order=2}}}};
                string legacy=Path.Combine(root,"legacy");var old=new Dictionary<string,object>{{"Version",3},{"Enabled",true},{"LegacyPreferences","历史待归属偏好"},{"IgnoreBefore",now-200},{"AccountIgnoreBefore",new Dictionary<string,long>{{Id('a'),now-300}}},{"RemovedFriends",new[]{new RemovedFriendMemory{AccountId=Id('a'),Id=Id('f'),IgnoreBefore=now-400}}},{"Friends",new[]{legacyFriend}}};
                WriteData(legacy,old);var migrated=new MemoryStore(legacy);
                check("旧v3迁移v4时好友偏好示例来源和去重完整保留",migrated.Error==null&&migrated.Data.Version==4&&migrated.Data.Groups.Count==0&&Json(migrated.Current(p))==Json(legacyFriend));
                check("v3迁移保留待归属偏好及账号好友删除屏障",migrated.Data.LegacyPreferences=="历史待归属偏好"&&migrated.Data.IgnoreBefore==now-200&&migrated.Data.AccountIgnoreBefore[Id('a')]==now-300&&migrated.Data.RemovedFriends.Count==1&&migrated.Data.RemovedFriends[0].IgnoreBefore==now-400);
                var diskVersion=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(LocalStore.Read(Path.Combine(legacy,"memory.dpapi"),true));
                check("v3迁移立即加密持久化为v4阻止旧版丢弃群字段",Convert.ToInt32(diskVersion["Version"])==4&&new MemoryStore(legacy).Data.Version==4&&Json(new MemoryStore(legacy).Current(p))==Json(legacyFriend));
                migrated.ObserveGroup(a,now);migrated.SetGroupAgreements(migrated.CurrentGroup(a),"新群约定");migrated=new MemoryStore(legacy);
                check("旧v3升级新增群不将同名好友偏好复制给群",migrated.Error==null&&migrated.Data.Version==4&&migrated.CurrentGroup(a).Preferences==""&&migrated.CurrentGroup(a).Agreements=="新群约定"&&Json(migrated.Current(p))==Json(legacyFriend));
                string blockedMigration=Path.Combine(root,"blocked-migration");WriteData(blockedMigration,old);path=Path.Combine(blockedMigration,"memory.dpapi");original=File.ReadAllBytes(path);Directory.CreateDirectory(path+".tmp");var failedMigration=new MemoryStore(blockedMigration);failedMigration.ObserveGroup(a,now);
                check("v3迁移写入失败保留原文件资料并暂停使用",failedMigration.Error!=null&&original.SequenceEqual(File.ReadAllBytes(path))&&Json(failedMigration.Current(p))==Json(legacyFriend)&&failedMigration.Context(p,now)==""&&Empty(failedMigration.GroupContext(a))&&failedMigration.CurrentGroup(a)==null);
                Directory.Delete(path+".tmp");failedMigration.SetPreferences(failedMigration.Current(p),"历史好友偏好");
                check("v3迁移失败后显式保存可恢复并持久化v4",failedMigration.Error==null&&new MemoryStore(blockedMigration).Data.Version==4&&Json(new MemoryStore(blockedMigration).Current(p))==Json(legacyFriend));
                old["Version"]=1;old["Preferences"]="旧全局偏好";WriteData(Path.Combine(root,"v1"),old);migrated=new MemoryStore(Path.Combine(root,"v1"));migrated.ObserveGroup(a,now);
                check("旧版全局偏好仍待手动归属且不流入群",migrated.Error==null&&migrated.Data.LegacyPreferences=="旧全局偏好"&&Empty(migrated.GroupContext(a)));
                old["Version"]=3;old["Groups"]=new[]{new GroupMemory{Id=a.GroupId,AccountId=a.AccountId,Name=a.Contact,Preferences="第一条"},new GroupMemory{Id=a.GroupId,AccountId=a.AccountId,Name="另名",Preferences="第二条"}};
                string malformed=Path.Combine(root,"malformed");WriteData(malformed,old);var bad=new MemoryStore(malformed);path=Path.Combine(malformed,"memory.dpapi");original=File.ReadAllBytes(path);bad.ObserveGroup(a,now);bad.ClearAll(now);
                check("重复群身份的损坏存储不任选记录也不覆盖原文件",bad.Error!=null&&!bad.CanEdit&&Empty(bad.GroupContext(a))&&original.SequenceEqual(File.ReadAllBytes(path)));
                old["Groups"]=new[]{new GroupMemory{Id=a.GroupId,AccountId="原始账号",Name=a.Contact}};WriteData(malformed,old);bad=new MemoryStore(malformed);check("群存储非法账号身份读取失败且不暴露内容",bad.Error!=null&&!bad.CanEdit&&Empty(bad.GroupContext(a)));
                old["Groups"]=new[]{new GroupMemory{Id=a.GroupId,AccountId=a.AccountId,Name=a.Contact,Agreements=new string('长',2001)}};WriteData(malformed,old);bad=new MemoryStore(malformed);check("超限群约定文件读取失败而不截断后使用",bad.Error!=null&&!bad.CanEdit&&Empty(bad.GroupContext(a)));
                old["Version"]=5;old["Groups"]=new[]{new GroupMemory{Id=a.GroupId,AccountId=a.AccountId,Name=a.Contact,Preferences="未来版本群偏好",Agreements="未来版本群约定"}};WriteData(malformed,old);original=File.ReadAllBytes(Path.Combine(malformed,"memory.dpapi"));bad=new MemoryStore(malformed);bad.ObserveGroup(a,now);bad.ClearAll(now);bad.SetEnabled(true,now);
                check("未知未来版本拒绝使用且任何保存清理不覆盖原文件",bad.Error!=null&&!bad.CanEdit&&Empty(bad.GroupContext(a))&&bad.Context(p,now)==""&&original.SequenceEqual(File.ReadAllBytes(Path.Combine(malformed,"memory.dpapi"))));
                string blocked=Path.Combine(root,"register-blocked");Directory.CreateDirectory(blocked);Directory.CreateDirectory(Path.Combine(blocked,"memory.dpapi.tmp"));var failed=new MemoryStore(blocked);failed.ObserveGroup(a,now);
                check("群首次登记写入失败不暴露未落盘资料",failed.Error!=null&&failed.CurrentGroup(a)==null&&failed.Data.Groups.Count==0);
            }finally {
                string full=Path.GetFullPath(root),parent=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(full))Directory.Delete(full,true);
            }
        }
    }
}
