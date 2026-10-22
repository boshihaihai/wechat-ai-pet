using System;
using System.Collections.Generic;
namespace WeChatPet.Runtime {
    internal static class NativeEmojiDraftTests {
        internal static void Run(Action<string,bool> check) {
            var names=new HashSet<string>(new[]{"[色]","[调皮]","[捂脸]","[偷笑]"},StringComparer.Ordinal);
            var plan=NativeEmojiPlan.Create("好呀[调皮]\n第二段[色]。",names);
            check("原生计划只替换已确认名称，保留全部正文及段落",plan.Count==2&&plan.Expected=="好呀\uFFFC\n第二段\uFFFC。"&&plan.Text=="好呀[调皮]\n第二段[色]。");
            check("原生草稿必须同时满足两种控件的完整读回",plan.Matches(plan.Expected,plan.Expected)&&!plan.Matches(plan.Expected,"好呀")&&!plan.Matches(plan.Text,plan.Text));
            check("原生读回仅允许换行格式归一",plan.Matches("好呀\uFFFC\r\n第二段\uFFFC。",plan.Expected)&&!plan.Matches(" "+plan.Expected,plan.Expected));
            check("对象数量相同但位置错位不算成功",!plan.Matches("好呀\n\uFFFC第二段\uFFFC。","好呀\n\uFFFC第二段\uFFFC。"));
            check("未知表情和纯文字继续使用原方式",NativeEmojiPlan.Create("[不存在]",names)==null&&NativeEmojiPlan.Create("收到",names)==null);
            check("真实嵌入对象与代码不能混入原生粘贴计划",NativeEmojiPlan.Create("\uFFFC[色]",names)==null&&NativeEmojiPlan.Create("`[色]`",names)==null&&NativeEmojiPlan.Create("{\"emoji\":\"[色]\"}",names)==null);
            foreach(string literal in new[]{"请原样输入“[调皮]”","他说「[调皮]」","'[调皮]'","\"[调皮]\"","> 好呀[调皮]\n收到[捂脸]","你说‘[调皮]’这个名称，收到[偷笑]","代码 `x = [调皮]`，懂了[偷笑]","```\n[调皮]\n```\n好呀[偷笑]"})
                check("引用中的名称整条保留为普通文字："+literal,NativeEmojiPlan.Create(literal,names)==null);
            foreach(string literal in new[]{"https://a.test/[调皮]","链接 www.example.test/[调皮]","看这个 ftp://a.test/[调皮]，好的[捂脸]","[{\"value\":\"[调皮]\"}]"})
                check("链接与JSON数组中的名称不转换："+literal,NativeEmojiPlan.Create(literal,names)==null);
            foreach(string literal in new[]{"[调皮]表示开玩笑","[调皮]是什么意思？","这里的符号是[调皮]","请复制[调皮]","含义是[调皮]","[调皮]是一个表情","名称是[调皮]","请原样输入[调皮]","复制下面的文字：[调皮]","把[调皮]原样复制过去","这个表情叫[调皮]"})
                check("讲解名称时不转换原生对象："+literal,NativeEmojiPlan.Create(literal,names)==null);
            foreach(string ordinary in new[]{"好呀[调皮]","没别的意思[调皮]","我代表大家谢谢你[捂脸]","I'm happy[调皮]","[调皮]","[偷笑]是挺好笑的"})
                check("普通回复与纯表情继续支持原生计划："+ordinary,NativeEmojiPlan.Create(ordinary,names)!=null);
            foreach(string ordinary in new[]{"这表情剧情转得比电视剧还快[偷笑]","这符号挺有意思[偷笑]","代码终于跑通了[偷笑]","原文我看过了[偷笑]","我已经复制好了[偷笑]","这个emoji有点可爱[偷笑]","你说‘行吧’也太勉强了[偷笑]","你说\"行吧\"也太勉强了[偷笑]","代码 `done` 终于跑通了[偷笑]"}) {
                var ordinaryPlan=NativeEmojiPlan.Create(ordinary,names);
                check("普通话题和正文引用不阻止末尾表情："+ordinary,ordinaryPlan!=null&&ordinaryPlan.Count==1&&ordinaryPlan.Text==ordinary&&ordinaryPlan.Expected==ordinary.Replace("[偷笑]","\uFFFC"));
            }
            var mixed=NativeEmojiPlan.Create("[调皮][未知]",names);check("未知名称不会冒充原生对象",mixed.Count==1&&mixed.Expected=="\uFFFC[未知]");
            var large=NativeEmojiPlan.Create(new string('长',8000)+"[色]",names);check("长正文在表情转换中不截断",large.Expected.Length==8001&&large.Expected.EndsWith("\uFFFC"));
            check("粘贴菜单名称必须精确匹配",NativeEmojiPasteMenu.PasteName("粘贴")&&NativeEmojiPasteMenu.PasteName("Paste\tCtrl+V")&&!NativeEmojiPasteMenu.PasteName("粘贴并发送")&&!NativeEmojiPasteMenu.PasteName("复制")&&!NativeEmojiPasteMenu.PasteName(null));
            var before=Snapshot();var after=Snapshot();check("原生草稿验证接受原账号会话消息与输入框",NativeEmojiPlan.SameDraftContext(before,after));
            Action<Action<ChatSnapshot>,string> differs=delegate(Action<ChatSnapshot> change,string label){var s=Snapshot();change(s);check(label,!NativeEmojiPlan.SameDraftContext(before,s));};
            differs(s=>s.AccountId="other","换号后拒绝旧表情验证");differs(s=>s.FriendId="other","切换好友后拒绝旧表情验证");
            differs(s=>s.GroupId="other","切换群后拒绝旧表情验证");differs(s=>s.GroupChat=true,"群聊私聊不能混用表情验证");
            differs(s=>s.Session="other","会话变动后拒绝旧表情验证");differs(s=>s.Window=2,"窗口变动后拒绝旧表情验证");
            differs(s=>s.InputIdentity="other","输入框变动后拒绝旧表情验证");differs(s=>s.Tail="new","新来信后拒绝旧表情验证");
            differs(s=>s.ContentRevision="new","上下文变动后拒绝旧表情验证");differs(s=>s.Available=false,"读取失败不算表情填入通过");
        }
        static ChatSnapshot Snapshot(){return new ChatSnapshot{Available=true,Window=1,Session="s",InputIdentity="i",AccountId="a",FriendId="f",GroupId=null,Tail="t",ContentRevision="c"};}
    }
}
