using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WeChatPet.Runtime {
    public static class AutomaticPreferenceTests {
        static ChatSnapshot Chat(char friend){return new ChatSnapshot{Available=true,AtEnd=true,AccountId=new string('a',64),FriendId=new string(friend,64),Contact="合成好友",Messages=new List<ChatMessage>()};}
        static ChatMessage Message(string text,long ticks,uint order,string role="peer"){return new ChatMessage{Type=1,Role=role,Text=text,CreatedTicks=ticks,LocalId=order,MemoryKey=order.ToString("x64")};}
        static string[] FriendNotes(MemoryStore store,ChatSnapshot snapshot,long now){return (string[])((Dictionary<string,object>)RecommendationMemory.Fields(store,snapshot,now)["friendProfile"])["friendNotes"];}
        public static void Run(Action<string,bool> check) {
            foreach(var item in new[]{
                new[]{"我很喜欢咖啡","喜欢:咖啡"},new[]{"我特别不喜欢咖啡","喜欢:咖啡"},
                new[]{"我比较喜欢跑步","喜欢:跑步"},new[]{"我现在特别不喜欢跑步了","喜欢:跑步"},
                new[]{"我不喜欢吃香菜","吃:香菜"},new[]{"我特别喜欢吃香菜","吃:香菜"},
                new[]{"我很不喜欢吃香菜","吃:香菜"},new[]{"我最近比较喜欢吃香菜","吃:香菜"},
                new[]{"我比较喜欢喝咖啡","喝:咖啡"},new[]{"我不喜欢喝咖啡","喝:咖啡"},
                new[]{"我不喝咖啡","喝:咖啡"},new[]{"我现在可以喝咖啡了","喝:咖啡"}})
                check("明确自然表达归到原主题："+item[0],PreferenceRules.Key(item[0],false)==item[1]);
            foreach(string text in new[]{"我很喜欢香菜吗？","我可能特别喜欢吃香菜","我今天特别喜欢吃香菜","我这次比较喜欢喝咖啡","他说我特别喜欢吃香菜","“我特别不喜欢香菜”","我特别喜欢吃香菜哈哈","我比较喜欢吃香菜，但不爱吃葱","我特别喜欢吃香菜和葱","我很喜欢吃香菜\n我也喜欢葱","我不太喜欢吃香菜","我不特别喜欢吃香菜","我是一名老师","我在公司做产品经理","我很喜欢你","我喜欢吃","我不喜欢喝"})
                check("不推断不确定或越界表达："+text,PreferenceRules.Key(text,false)==null);
            check("普通喜欢与吃喝维度保持区别",PreferenceRules.Key("我喜欢咖啡",false)=="喜欢:咖啡"&&PreferenceRules.Key("我吃咖啡",false)=="吃:咖啡"&&PreferenceRules.Key("我喝咖啡",false)=="喝:咖啡");
            foreach(string text in new[]{"我喜欢什么","我喝什么","我习惯怎么安排","以后叫我什么","我吃饱了","我现在吃撑了","我喝醉了","我已经喝完咖啡了","我吃过香菜","我喝着咖啡"})
                check("无问号提问和当下吃喝状态不学习为长期偏好："+text,PreferenceRules.Key(text,false)==null);

            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"automatic-preference-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try {
                long now=DateTime.UtcNow.Ticks;var store=new MemoryStore(directory);var chat=Chat('b');
                var initial=Message("我不吃香菜",now-500,1);chat.Messages.Add(initial);store.Observe(chat,now);
                var changed=Message("我特别喜欢吃香菜",now-400,2);chat.Messages.Add(changed);store.Observe(chat,now);
                var friend=store.Current(chat);var active=friend.Items.Single(i=>!i.Superseded&&i.Kind=="stated");
                check("新自然说法替代同主题旧饮食偏好",active.Text==changed.Text&&!friend.Items.Any(i=>!i.Superseded&&i.Text==initial.Text));
                check("自动偏好完整保留对方来源而非改写事实",!active.Manual&&active.SourceRole=="peer"&&active.SourceText==changed.Text&&active.SourceKey==changed.MemoryKey&&active.SourceTicks==changed.CreatedTicks&&active.SourceOrder==changed.LocalId);
                var fields=RecommendationMemory.Fields(store,chat,now);var notes=(string[])((Dictionary<string,object>)fields["friendProfile"])["friendNotes"];
                check("实际推荐字段使用最新自动偏好",notes.Length==1&&notes[0].Contains(changed.Text)&&!notes[0].Contains(initial.Text)&&notes[0].StartsWith("好友自述，未核实",StringComparison.Ordinal));
                check("自动偏好不会混入手动回复风格或重复旧记忆",(string)((Dictionary<string,object>)fields["friendProfile"])["replyPreferences"]==""&&!((string)fields["memory"]).Contains(initial.Text));
                var opposite=Message("我特别不喜欢吃香菜",now-300,3);chat.Messages.Add(opposite);store.Observe(chat,now);
                check("带强度的否定改口立即替代肯定偏好",FriendNotes(store,chat,now).Single().Contains(opposite.Text)&&!friend.Items.Any(i=>!i.Superseded&&i.Text==changed.Text));
                chat.Messages.Add(Message("我很喜欢吃香菜",now-200,4,"self"));store.Observe(chat,now);
                check("本人相反口味不覆盖好友的最新偏好",FriendNotes(store,chat,now).Single().Contains(opposite.Text)&&friend.Items.Single(i=>i.SourceRole=="self").Kind=="recent");
                chat.Messages.Add(Message("我喜欢吃香菜",now-450,5));store.Observe(chat,now);
                check("迟到的旧同义自述不能恢复旧口味",FriendNotes(store,chat,now).Length==1&&FriendNotes(store,chat,now)[0].Contains(opposite.Text));

                var drink=Chat('c');drink.Messages.Add(Message("我不喝咖啡",now-300,6));store.Observe(drink,now);
                drink.Messages.Add(Message("我现在可以喝咖啡了",now-200,7));store.Observe(drink,now);
                check("原有咖啡禁忌改口链继续生效",FriendNotes(store,drink,now).Single().Contains("现在可以喝咖啡")&&!FriendNotes(store,drink,now).Single().Contains("不喝咖啡"));
                drink.Messages.Add(Message("我比较不喜欢喝咖啡",now-100,8));store.Observe(drink,now);
                check("喜欢喝的自然说法复用饮品更新主题",FriendNotes(store,drink,now).Single().Contains("比较不喜欢喝咖啡")&&!FriendNotes(store,drink,now).Single().Contains("可以喝咖啡"));
                check("不同好友的自动偏好不串到推荐请求",!String.Join("",FriendNotes(store,chat,now)).Contains("咖啡")&&!String.Join("",FriendNotes(store,drink,now)).Contains("香菜"));
                var otherAccount=Chat('b');otherAccount.AccountId=new string('d',64);
                check("换账号后不复用同标识好友的自动偏好",FriendNotes(store,otherAccount,now).Length==0);

                var dimensions=Chat('e');dimensions.Messages.Add(Message("我很喜欢香菜",now-200,9));dimensions.Messages.Add(Message("我不吃香菜",now-100,10));store.Observe(dimensions,now);
                check("喜欢事物不擅自改写为能吃或不能吃",FriendNotes(store,dimensions,now).Length==2&&FriendNotes(store,dimensions,now).Any(x=>x.Contains("很喜欢香菜"))&&FriendNotes(store,dimensions,now).Any(x=>x.Contains("不吃香菜")));
                var uncertain=Chat('f');uint order=20;
                foreach(string text in new[]{"我今天特别喜欢吃香菜","我特别喜欢吃香菜吗？","我特别喜欢吃香菜哈哈","他说我很喜欢吃香菜","我很喜欢吃香菜，但是不能吃","我是一名老师"})uncertain.Messages.Add(Message(text,now-50,order++));
                store.Observe(uncertain,now);
                check("拒绝的表述只留近期原话而不生成好友事实",store.Current(uncertain).Items.All(i=>i.Kind=="recent")&&FriendNotes(store,uncertain,now).Length==0);
                foreach(string text in new[]{"我喜欢什么","我吃饱了","我喝醉了","我吃过香菜"})uncertain.Messages.Add(Message(text,now-20,order++));
                store.Observe(uncertain,now);
                check("日常状态和省略问号的问句只保留原话摘录",store.Current(uncertain).Items.All(i=>i.Kind=="recent")&&FriendNotes(store,uncertain,now).Length==0);
                // Older versions may already have classified these statements.
                var mistaken=store.Current(uncertain).Items.Single(i=>i.Text=="我吃饱了");mistaken.Kind="stated";
                check("旧版本误学的即时状态在读取时退回近期原话",FriendNotes(store,uncertain,now).Length==0&&mistaken.Kind=="recent");
                store.Save();var reopened=new MemoryStore(directory);var restored=reopened.Current(chat).Items.Single(i=>i.Kind=="stated");
                check("重启保留自动改口结果与来源",reopened.Error==null&&FriendNotes(reopened,chat,now).Single().Contains(opposite.Text)&&restored.SourceText==opposite.Text&&restored.SourceRole=="peer"&&restored.SourceTicks==opposite.CreatedTicks);
                check("自然说法的自动偏好仍按原期限过期",FriendNotes(reopened,chat,now+TimeSpan.TicksPerDay*31).Length==0);
            } finally {Directory.Delete(directory,true);}
        }
    }
}
