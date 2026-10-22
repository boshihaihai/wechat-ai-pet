using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WeChatPet.Runtime {
    public static class MemoryUnicodeTests {
        public static void Run(Action<string,bool> check) {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"memory-unicode-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try {
                string emoji=Char.ConvertFromUtf32(0x1f600),boundary=new string('长',399)+emoji;
                foreach(int limit in new[]{1,80,180,400,2000}) {
                    string text=new string('字',limit-1)+emoji+"尾";
                    check("记忆截断不拆分补充字符："+limit,MemoryStore.Clip(text,limit)==new string('字',limit-1));
                    check("完整落在记忆预算内的补充字符保留："+limit,MemoryStore.Clip(text,limit+1)==new string('字',limit-1)+emoji);
                }
                check("空预算和空值裁剪保持原行为",MemoryStore.Clip(emoji,0)==""&&MemoryStore.Clip(null,400)=="");
                check("正常Unicode仍规范化去重",PreferenceRules.Normalize(" ＡＢＣ　"+emoji+"！")=="abc"+emoji);
                foreach(string invalid in new[]{"\ud83d","\ude00","\ud83dA","\ufffe","\uffff","\ufdd0",Char.ConvertFromUtf32(0x1ffff)})
                    check("旧异常字符不使记忆比较崩溃："+((int)invalid[0]).ToString("X4"),PreferenceRules.Normalize("前"+invalid+"后").StartsWith("前\ufffd",StringComparison.Ordinal));

                long now=DateTime.UtcNow.Ticks;
                var snapshot=new ChatSnapshot{Available=true,AtEnd=true,AccountId=new string('a',64),FriendId=new string('b',64),Contact=new string('名',79)+emoji,Messages=new List<ChatMessage>{new ChatMessage{Role="peer",Type=1,Text=boundary,MemoryKey=new string('c',64),CreatedTicks=now,LocalId=1}}};
                var store=new MemoryStore(root);store.Observe(snapshot,now);var friend=store.Current(snapshot);
                check("399字加emoji来信可记录且原来源完整",friend.Items.Single().Text==new string('长',399)&&friend.Items.Single().SourceText==boundary);
                check("联系人名称截断保持完整字符",friend.Name==new string('名',79));
                store.Put(friend,null,new string('手',399)+emoji,true,now+1);store.SetPreferences(friend,boundary);
                check("手动记忆和回复偏好同样不拆分emoji",friend.Items.Single(i=>i.Manual).Text==new string('手',399)&&friend.Preferences==new string('长',399));
                check("包含边界emoji的记忆可用于上下文",store.Context(snapshot,now+1).Length>0&&store.Error==null);
                store.Save();var reopened=new MemoryStore(root);
                check("边界emoji记忆可重启恢复且来源不丢失",reopened.Error==null&&reopened.Current(snapshot).Items.Any(i=>i.SourceText==boundary)&&reopened.Context(snapshot,now+1).Length>0);
                var another=new ChatSnapshot{Available=true,AtEnd=true,AccountId=new string('d',64),FriendId=snapshot.FriendId,Contact=snapshot.Contact,Messages=new List<ChatMessage>()};
                check("Unicode修复不改变账号隔离",store.Context(another,now)=="");

                // A synthetic older JSON file deliberately contains an escaped
                // lone surrogate. Never read or modify the user's memory file.
                string oldRoot=Path.Combine(root,"old");Directory.CreateDirectory(oldRoot);string path=Path.Combine(oldRoot,"memory.dpapi");
                string legacy="{\"Version\":4,\"Friends\":[{\"Id\":\""+snapshot.FriendId+"\",\"AccountId\":\""+snapshot.AccountId+"\",\"Name\":\"synthetic\",\"Items\":[{\"Id\":\"legacy\",\"Text\":\"legacy\\ud83d\",\"SourceText\":\"original source\",\"Kind\":\"note\",\"SubjectRole\":\"peer\",\"Manual\":true,\"SourceTicks\":"+now+",\"UpdatedTicks\":"+now+"}]}]}";
                LocalStore.Write(path,legacy,true);byte[] original=File.ReadAllBytes(path);var old=new MemoryStore(oldRoot);var oldFriend=old.Current(snapshot);
                check("含孤立surrogate的旧记录仍可加载",old.Error==null&&oldFriend!=null&&oldFriend.Items.Count==1);
                // Some Framework JSON readers already replace malformed escapes.
                // Also exercise the exact in-memory value made by the old clip.
                oldFriend.Items.Single().Text="legacy\ud83d";
                string context=old.Context(snapshot,now);old.Refresh(oldFriend,now);
                check("旧异常记录比较不崩溃且不清空正文或来源",context.Contains("legacy")&&oldFriend.Items.Single().Text=="legacy\ud83d"&&oldFriend.Items.Single().SourceText=="original source");
                check("比较旧异常记录不直接重写原文件",original.SequenceEqual(File.ReadAllBytes(path)));
            } finally {Directory.Delete(root,true);}
        }
    }
}
