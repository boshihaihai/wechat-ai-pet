using System;
using System.Collections.Generic;
using System.Linq;

namespace WeChatPet.Runtime {
    public static class VisibleMessageTests {
        static readonly long Epoch=new DateTime(2026,10,21,5,0,0,DateTimeKind.Utc).Ticks;
        const string QuotedUiText="合成引用回复：这段引用拼接文字不能进入推荐上下文";
        static long T(int ms){return Epoch+TimeSpan.TicksPerMillisecond*ms;}
        static NativeMessageReader.Row Row(int id,string key,uint type){return new NativeMessageReader.Row{AccessibleId=id,KeyTag=key,Type=type};}
        static bool RejectedWithoutMutation(int expectedId,string expectedText,int currentId,string currentText,NativeMessageReader.Row row) {
            var map=new Dictionary<string,string>{{"existing","已验证的合成正文"}};
            return !WeChatReader.TryMapVisibleText(expectedId,expectedText,currentId,currentText,row,map)&&map.Count==1&&map["existing"]=="已验证的合成正文";
        }
        static ChatSnapshot Snapshot(bool appended) {
            var messages=new List<ChatMessage> {
                new ChatMessage{Key="plain",Type=1,Role="self",LocalId=10,Text="合成普通正文",CreatedTicks=T(-2000)},
                // Deliberately retain UI text here to verify the controller's independent type boundary.
                new ChatMessage{Key="quote",Type=49,Role="peer",LocalId=11,Text=QuotedUiText,CreatedTicks=T(-1000)}
            };
            if(appended)messages.Add(new ChatMessage{Key="incoming",Type=1,Role="peer",LocalId=12,Text="合成新的普通来信",CreatedTicks=T(1000)});
            string tail=messages[messages.Count-1].Key;
            return new ChatSnapshot {
                Available=true,Reason="ok",AtEnd=true,DraftEmpty=true,DraftConsistent=true,
                AccountId=new string('a',64),FriendId=new string('b',64),Session="synthetic-session",Contact="合成好友",
                InputIdentity="synthetic-input",ViewIdentity="synthetic-view",ContentRevision=tail,
                Tail=tail,Keys=messages.Select(m=>m.Key).ToArray(),Messages=messages
            };
        }
        public static void Run(Action<string,bool> check) {
            var frame=new NativeMessageReader.Frame {Valid=true,Count=2};
            frame.Model.Add(new NativeMessageReader.Row {Supported=true,ModelIndex=0,KeyTag="older-peer",DirectionKnown=true,Peer=true,Type=1});
            check("私聊真实尾行无法解析时不把前一条来信当作最新消息",!WeChatReader.PrivateTailKnown(frame));
            frame.Model.Add(new NativeMessageReader.Row {Supported=true,ModelIndex=1,KeyTag="actual-tail",DirectionKnown=true,Peer=false,Type=49});
            check("私聊已识别非文字尾行仍保留其身份而不回退旧文字",WeChatReader.PrivateTailKnown(frame));
            frame.Model[1].Type=1;frame.Model[1].Peer=true;
            check("私聊已识别普通来信尾部保持可读",WeChatReader.PrivateTailKnown(frame));
            frame.Model[1].ModelIndex=2;
            check("私聊尾部原生索引越界不能被当作当前消息",!WeChatReader.PrivateTailKnown(frame));
            frame.Model.RemoveAt(0);frame.Model[0].ModelIndex=1;
            check("省略不支持的历史行不妨碍已确认的私聊真实尾部",WeChatReader.PrivateTailKnown(frame));
            var map=new Dictionary<string,string>();var plain=Row(101,"plain",1);var quote=Row(102,"quote",49);
            bool plainMapped=WeChatReader.TryMapVisibleText(101,"合成普通正文",101,"合成普通正文",plain,map);
            bool quoteMapped=WeChatReader.TryMapVisibleText(102,QuotedUiText,102,QuotedUiText,quote,map);
            check("可见普通文字与引用行共存时验证通过且只映射普通正文",plainMapped&&quoteMapped&&map.Count==1&&map["plain"]=="合成普通正文"&&!map.ContainsKey("quote")&&quote.KeyTag=="quote"&&quote.Type==49);

            map=new Dictionary<string,string>();string limit=new string('字',2000),tooLong=new string('字',2001);
            bool atLimit=WeChatReader.TryMapVisibleText(103,limit,103,limit,Row(103,"limit",1),map);
            bool aboveLimit=WeChatReader.TryMapVisibleText(104,tooLong,104,tooLong,Row(104,"too-long",1),map);
            check("正文长度边界保留2000字并将超长正文留为空标记",atLimit&&aboveLimit&&map.Count==1&&map["limit"]==limit&&!map.ContainsKey("too-long"));
            map=new Dictionary<string,string>();
            check("正文尚未出现时保留已验证消息而不伪造文本",WeChatReader.TryMapVisibleText(105,null,105,null,Row(105,"missing-text",1),map)&&map.Count==0);
            check("缺失原生可见行被拒绝且不污染已验证文本",RejectedWithoutMutation(106,"正文",106,"正文",null));
            bool missingKeys=true;
            foreach(string key in new string[]{null,""})missingKeys&=RejectedWithoutMutation(106,"正文",106,"正文",Row(106,key,1));
            check("缺失消息标识被拒绝且不污染已验证文本",missingKeys);
            check("UI运行标识变化被拒绝且不污染已验证文本",RejectedWithoutMutation(107,"新正文",108,"新正文",Row(107,"existing",1)));
            check("原生可见行标识不符被拒绝且不污染已验证文本",RejectedWithoutMutation(107,"新正文",107,"新正文",Row(108,"existing",1)));
            check("两次UI名称变化被拒绝且不污染已验证文本",RejectedWithoutMutation(107,"旧正文",107,"新正文",Row(107,"existing",1)));

            var controller=new RecommendationController{Connected=true};List<ChatMessage> context=null;
            controller.Generate+=(id,messages)=>context=messages;
            controller.Observe(Snapshot(false),T(0));controller.Observe(Snapshot(false),T(610));
            check("引用消息作为聊天尾部标记时不触发普通文字推荐",controller.TriggerCount==0&&controller.PendingId==null&&controller.Current.Tail=="quote");
            controller.Observe(Snapshot(true),T(1000));controller.Observe(Snapshot(true),T(1610));
            check("引用标记之后的新普通来信仍正常触发一次推荐",controller.TriggerCount==1&&controller.PendingId!=null&&controller.LastTriggerOrigin=="appended_message");
            check("推荐上下文保留普通文字并排除引用行及其拼接文本",context!=null&&context.Count==2&&context[0].Key=="plain"&&context[1].Key=="incoming"&&context.All(m=>m.Type==1&&m.Text!=QuotedUiText));
            controller.Observe(Snapshot(true),T(2000));controller.Observe(Snapshot(true),T(3000));
            check("引用行后的同一来信持续采样不重复生成",controller.TriggerCount==1);
        }
    }
}
