using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace WeChatPet.Runtime {
    public static class ExpressionExampleTests {
        static ChatSnapshot Chat(char account,char friend) {
            return new ChatSnapshot{Available=true,AtEnd=true,AccountId=new string(account,64),FriendId=new string(friend,64),Contact="同名好友",Messages=new List<ChatMessage>()};
        }
        public static void Run(Action<string,bool> check) {
            string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"expression-example-test-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                long now=DateTime.UtcNow.Ticks;
                var a=Chat('a','b');var b=Chat('a','c');var other=Chat('d','b');
                var store=new MemoryStore(dir);store.Observe(a,now);store.Observe(b,now);store.Observe(other,now);
                var fa=store.Current(a);var fb=store.Current(b);var fx=store.Current(other);
                store.SetExpressionExamples(fa,new[]{"  行啊，听你的  ","你可真会挑时候","行啊，听你的"," ",null});
                store.SetExpressionExamples(fb,new[]{"王老师，材料我再确认一下。"});
                store.SetExpressionExamples(fx,new[]{"队长，这事交给你定"});
                check("表达示例去空白去重但保留原有措辞",store.ExpressionExamples(a).SequenceEqual(new[]{"行啊，听你的","你可真会挑时候"}));
                check("同名好友示例独立，不串称呼",store.ExpressionExamples(b).Single().Contains("王老师")&&!String.Join("",store.ExpressionExamples(a)).Contains("王老师"));
                check("相同好友标识在不同账号隔离示例",store.ExpressionExamples(other).Single().Contains("队长")&&!String.Join("",store.ExpressionExamples(a)).Contains("队长"));
                var captured=store.ExpressionExamples(a);captured[0]="外部修改";
                check("请求示例快照不能修改存储",store.ExpressionExamples(a)[0]=="行啊，听你的");
                store.SetExpressionExamples(fa,new[]{"这是新写的示例"});
                check("编辑示例不改变已经捕获的请求",captured[1]=="你可真会挑时候"&&store.ExpressionExamples(a).Single()=="这是新写的示例");
                bool many=false,longText=false;
                try{store.SetExpressionExamples(fa,Enumerable.Range(0,6).Select(i=>"示例"+i));}catch(InvalidOperationException){many=true;}
                try{store.SetExpressionExamples(fa,new[]{new string('字',201)});}catch(InvalidOperationException){longText=true;}
                check("示例超出五条或单条二百字拒绝且保留原内容",many&&longText&&store.ExpressionExamples(a).Single()=="这是新写的示例");
                store.SetExpressionExamples(fa,new[]{new string('字',200)});
                check("表达示例二百字边界可保存",store.ExpressionExamples(a).Single().Length==200);
                store.SetExpressionExamples(fa,new[]{"不要自动加入聊天原话"});
                a.Messages.Add(new ChatMessage{Type=1,Role="self",Text="一条真实已发送消息",MemoryKey=new string('e',64),CreatedTicks=now});store.Observe(a,now);
                check("聊天原话不会自动混入手动表达示例",store.ExpressionExamples(a).Single()=="不要自动加入聊天原话");
                check("表达示例不当成事实记忆重复提交",!store.Context(a,now).Contains("不要自动加入聊天原话"));
                store.SetFriendEnabled(fa,false,now);check("关闭好友记忆停止提供表达示例",store.ExpressionExamples(a).Length==0);store.SetFriendEnabled(fa,true,now);
                store.SetEnabled(false,now);check("关闭总记忆停止提供表达示例",store.ExpressionExamples(a).Length==0);store.SetEnabled(true,now);
                var unknown=Chat('a','b');unknown.FriendId=null;check("好友身份不确定不提供示例",store.ExpressionExamples(unknown).Length==0);
                store=new MemoryStore(dir);fa=store.Current(a);fb=store.Current(b);
                check("重启恢复隔离的表达示例",store.Error==null&&store.Data.Version==4&&store.ExpressionExamples(a).Single()=="不要自动加入聊天原话"&&store.ExpressionExamples(b).Single().Contains("王老师"));
                check("示例仍用当前用户DPAPI加密",!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir,"memory.dpapi"))).Contains("王老师"));
                store.ClearFriend(fa,now);check("清空好友会删除其示例且不影响其他人",store.ExpressionExamples(a).Length==0&&store.ExpressionExamples(b).Length==1);
                var stale=fb;store.ClearAll(now);store.Observe(b,now);store.SetExpressionExamples(stale,new[]{"不能复活的旧编辑"});
                check("删除后旧编辑对象不能污染同标识新好友",store.ExpressionExamples(b).Length==0);

                string oldDir=Path.Combine(dir,"v2");Directory.CreateDirectory(oldDir);
                var old=new{Version=2,Enabled=true,LegacyPreferences="待归属旧偏好",Friends=new[]{new{Id=a.FriendId,AccountId=a.AccountId,Name="同名好友",Preferences="少用表情",Items=new MemoryItem[0],Seen=new string[0]}}};
                LocalStore.Write(Path.Combine(oldDir,"memory.dpapi"),new JavaScriptSerializer().Serialize(old),true);
                var migrated=new MemoryStore(oldDir);
                check("v2记忆无损迁移且示例默认为空",migrated.Error==null&&migrated.Data.Version==4&&migrated.Current(a).Preferences=="少用表情"&&migrated.Data.LegacyPreferences=="待归属旧偏好"&&migrated.ExpressionExamples(a).Length==0);
                check("迁移后的版本和好友偏好持久保存",new MemoryStore(oldDir).Data.Version==4&&new MemoryStore(oldDir).Current(a).Preferences=="少用表情");
                migrated.SetExpressionExamples(migrated.Current(a),new[]{"五条以下正常内容"});
                migrated.Current(a).ExpressionExamples.Add(new string('坏',201));migrated.SetPreferences(migrated.Current(a),"仍保留");
                var invalid=new MemoryStore(oldDir);check("非法示例文件暂停记忆且保留文件",invalid.Error!=null&&!invalid.Data.Enabled&&File.Exists(Path.Combine(oldDir,"memory.dpapi"))&&invalid.ExpressionExamples(a).Length==0);
            }finally{Directory.Delete(dir,true);}
        }
    }
}
