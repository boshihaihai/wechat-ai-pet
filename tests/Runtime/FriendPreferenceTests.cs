using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace WeChatPet.Runtime {
    public static class FriendPreferenceTests {
        static ChatSnapshot Chat(string account,string friend) {
            return new ChatSnapshot{Available=true,AtEnd=true,AccountId=account,FriendId=friend,Contact="同名好友",Messages=new List<ChatMessage>()};
        }
        public static void Run(Action<string,bool> check) {
            string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"friend-preference-test-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                long now=DateTime.UtcNow.Ticks;string account=new string('a',64),friendId=new string('b',64);
                var a=Chat(account,friendId);var b=Chat(account,new string('c',64));var otherAccount=Chat(new string('d',64),friendId);
                var store=new MemoryStore(dir);store.Observe(a,now);store.Observe(b,now);store.Observe(otherAccount,now);
                var fa=store.Current(a);var fb=store.Current(b);var fx=store.Current(otherAccount);
                store.SetPreferences(fa,"称呼对方为小祖宗，语气亲昵");store.SetPreferences(fb,"称呼对方为王老师，简洁正式");store.SetPreferences(fx,"称呼对方为队长");
                check("回复偏好仅提供给指定好友",store.Context(a,now).Contains("小祖宗")&&!store.Context(b,now).Contains("小祖宗")&&!store.Context(otherAccount,now).Contains("小祖宗"));
                check("同名好友各自使用自己的回复语气",store.Context(b,now).Contains("王老师")&&!store.Context(a,now).Contains("王老师"));
                check("相同好友标识在不同微信账号隔离回复偏好",store.Context(otherAccount,now).Contains("队长")&&!store.Context(a,now).Contains("队长"));
                string toA=store.Context(a,now),toB=store.Context(b,now);
                check("快速切换好友不缓存或复用上一位的偏好",store.Context(a,now)==toA&&store.Context(b,now)==toB&&store.Context(a,now)==toA);
                check("好友设定明确标记为用户填写而非好友自述",toA.Contains("用户填写的当前好友设定（关系、背景与回复方式；不是好友自述）"));
                store=new MemoryStore(dir);fa=store.Current(a);fb=store.Current(b);fx=store.Current(otherAccount);
                check("重启保持三份回复偏好独立",fa.Preferences.Contains("小祖宗")&&fb.Preferences.Contains("王老师")&&fx.Preferences.Contains("队长"));
                store.SetPreferences(fa,"  改称小美，少用表情  ");
                check("改偏好只更新当前好友且去掉首尾空白",fa.Preferences=="改称小美，少用表情"&&store.Context(a,now).Contains("改称小美")&&!store.Context(a,now).Contains("小祖宗")&&fb.Preferences.Contains("王老师"));
                store.SetPreferences(fa,"");
                check("清空个人回复偏好后不退回其他人的设置",!store.Context(a,now).Contains("措辞偏好")&&fb.Preferences.Contains("王老师"));
                store.SetPreferences(fa,new string('长',450));check("每位好友回复偏好仍限制四百字符",fa.Preferences.Length==400&&store.Context(a,now).Length<=1500);
                store.SetFriendEnabled(fa,false,now);check("关闭好友记忆同时停止使用其回复偏好",store.Context(a,now)=="");store.SetFriendEnabled(fa,true,now);
                store.ClearFriend(fa,now);check("清空此好友同时清除其回复偏好而不影响其他人",fa.Preferences==""&&fb.Preferences.Contains("王老师"));
                var outsider=new FriendMemory{Id=fa.Id,AccountId=fa.AccountId,Preferences="外部对象"};store.SetPreferences(outsider,"不应保存");
                check("同标识的脱离存储对象不能修改回复偏好",fa.Preferences==""&&outsider.Preferences=="外部对象");
                store.ClearAll(now);store.SetPreferences(fb,"已删除好友不应复活");store.Observe(b,now);
                check("删除全部后旧编辑对象不能写入重新出现的好友",store.Current(b).Preferences==""&&store.Data.Friends.Count==1);

                string legacyDir=Path.Combine(dir,"v1");Directory.CreateDirectory(legacyDir);
                string path=Path.Combine(legacyDir,"memory.dpapi");
                var legacy=new {Version=1,Enabled=true,Preferences="旧版共用的专属称呼",Friends=new[]{new {Id=friendId,AccountId=account,Name="同名好友",Items=new MemoryItem[0],Seen=new string[0]}}};
                LocalStore.Write(path,new JavaScriptSerializer().Serialize(legacy),true);
                var migrated=new MemoryStore(legacyDir);migrated.Observe(b,now);migrated.Save();
                check("旧版全局偏好迁移为待归属存档并保留好友",migrated.Error==null&&migrated.Data.Version==4&&migrated.Data.LegacyPreferences==legacy.Preferences&&migrated.Data.Preferences==""&&migrated.Data.Friends.Count==2);
                check("旧版共用称呼不自动进入任何好友的模型上下文",migrated.Current(a).Preferences==""&&migrated.Current(b).Preferences==""&&!migrated.Context(a,now).Contains("旧版")&&!migrated.Context(b,now).Contains("旧版"));
                migrated=new MemoryStore(legacyDir);check("重启后待归属偏好仍保留且不会自动复制",migrated.Data.LegacyPreferences==legacy.Preferences&&migrated.Current(a).Preferences==""&&migrated.Current(b).Preferences=="");
                migrated.UseLegacyPreferences(migrated.Current(a));
                check("明确归属只将旧偏好应用于选中的一位好友",migrated.Context(a,now).Contains(legacy.Preferences)&&!migrated.Context(b,now).Contains(legacy.Preferences)&&migrated.Data.LegacyPreferences=="");
                migrated.UseLegacyPreferences(migrated.Current(b));migrated=new MemoryStore(legacyDir);
                check("旧偏好归属保存后不能再次传播给其他好友",migrated.Current(a).Preferences==legacy.Preferences&&migrated.Current(b).Preferences==""&&migrated.Data.LegacyPreferences=="");
                check("迁移与好友偏好仍加密存储",!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("专属称呼"));
                migrated.Data.LegacyPreferences="待归属";migrated.ClearAll(now);
                check("清空全部同时清除待归属偏好",new MemoryStore(legacyDir).Data.LegacyPreferences=="");
            } finally {Directory.Delete(dir,true);}
        }
    }
}
