using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WeChatPet.Runtime {
    // Pure contract checks: no process reads, keyboard hooks, UI Automation or draft writes.
    internal static class GroupReaderTests {
        static readonly MethodInfo sameContext=typeof(ObserverProgram).GetMethod("SameContext",BindingFlags.Static|BindingFlags.NonPublic);
        static readonly MethodInfo sameNativeRow=typeof(NativeMessageReader).GetMethod("Same",BindingFlags.Static|BindingFlags.NonPublic);
        const string room="123456789@chatroom",account="wxid_test_self",member="wxid_test_peer";
        internal static void Run(Action<string,bool> check) {
            check("普通群标识及长度上限可识别",NativeMessageReader.IsGroupSession(room)&&NativeMessageReader.IsGroupSession(new String('9',32)+"@chatroom"));
            string[] invalidRooms={null,"","wxid_peer","@chatroom","abc@chatroom",new String('9',33)+"@chatroom","123@chatroom.extra","123@chatroom\n","123@chatroom\r\n","123@stranger"," 123@chatroom"};
            check("群标识拒绝其他会话类型与尾随控制字符",Array.TrueForAll(invalidRooms,x=>!NativeMessageReader.IsGroupSession(x)));
            check("群来信可确认本账号和实际发送成员",NativeMessageReader.ValidGroupEndpoints(room,account,room,member));
            check("群内自己的已发送消息可确认方向",NativeMessageReader.ValidGroupEndpoints(account,room,room,account));
            check("来信成员不能冒充本账号",!NativeMessageReader.ValidGroupEndpoints(room,account,room,account));
            check("自己发出的群消息不能冒充其他成员",!NativeMessageReader.ValidGroupEndpoints(account,room,room,member));
            check("群端点必须包含当前群且双方不同",!NativeMessageReader.ValidGroupEndpoints("999@chatroom",account,room,member)&&!NativeMessageReader.ValidGroupEndpoints(account,member,room,member)&&!NativeMessageReader.ValidGroupEndpoints(room,room,room,member));
            check("群账号和发送成员不能是群或其他特殊会话",!NativeMessageReader.ValidGroupEndpoints(room,"999@chatroom",room,member)&&!NativeMessageReader.ValidGroupEndpoints(room,account,room,"999@chatroom")&&!NativeMessageReader.ValidGroupEndpoints(room,account,room,"user@stranger"));
            string[] invalidPeople={null,""," ","\t","\r\n","wxid_test\n"," wxid_test"};
            check("群账号拒绝缺失空白或控制字符",Array.TrueForAll(invalidPeople,x=>!NativeMessageReader.ValidGroupEndpoints(room,x,room,member)));
            check("群成员拒绝缺失空白或控制字符",Array.TrueForAll(invalidPeople,x=>!NativeMessageReader.ValidGroupEndpoints(room,account,room,x)));
            GroupHistoryChecks(check);

            var s=Snapshot(true);var r=Reply(s);
            check("同账号同群且空草稿可保留手动生成结果",Same(s,r)&&InlineSuggestionWindow.Eligible(true,s,r,DateTime.UtcNow.Ticks));
            Reject(check,"群账号切换后拒绝旧推荐及草稿",x=>x.AccountId=new String('b',64));
            Reject(check,"群切换后拒绝旧推荐及草稿",x=>x.GroupId=new String('d',64));
            Reject(check,"群返回私聊后拒绝旧推荐及草稿",x=>{x.GroupChat=false;x.FriendId=new String('e',64);x.GroupId=null;});
            Reject(check,"群账号未确认时不接管草稿",x=>x.AccountId=null);
            Reject(check,"群标识未确认时不接管草稿",x=>x.GroupId=null);
            Reject(check,"群账号或群标识损坏时拒绝草稿",x=>{x.AccountId="invalid-account";x.GroupId="invalid-group";});
            Reject(check,"群聊天会话改变撤销推荐",x=>x.Session="another-session");
            Reject(check,"群中新消息改变尾部撤销旧推荐",x=>x.Tail="message-2");
            Reject(check,"群内容变化撤销旧推荐",x=>x.ContentRevision="revision-2");
            Reject(check,"群输入控件改变撤销旧推荐",x=>x.InputIdentity="input-2");
            Reject(check,"群输入活动变化撤销旧推荐",x=>x.Activity++);
            Reject(check,"群已有草稿不覆盖",x=>x.DraftEmpty=false);
            Reject(check,"群草稿读取不一致不接管Tab",x=>x.DraftConsistent=false);
            Reject(check,"群中文组词时不接管Tab",x=>x.Composing=true);
            Reject(check,"群历史滚动位置不填入",x=>x.AtEnd=false);
            Reject(check,"群读取不可用不填入",x=>x.Available=false);

            s=Snapshot(true);r=Reply(s);r.CreatedTicks=DateTime.UtcNow.AddSeconds(-61).Ticks;
            check("群推荐过期同时退出草稿候选和灰字",!Same(s,r)&&!InlineSuggestionWindow.Eligible(true,s,r,DateTime.UtcNow.Ticks));
            r=Reply(s);r.CreatedTicks=DateTime.UtcNow.AddMinutes(1).Ticks;
            check("未来时间的群推荐不能填入",!Same(s,r)&&!InlineSuggestionWindow.Eligible(true,s,r,DateTime.UtcNow.Ticks));
            r=Reply(s);r.GroupId=new String('d',64);
            check("推荐自身携带错误群身份时拒绝",!Same(s,r)&&!InlineSuggestionWindow.Eligible(true,s,r,DateTime.UtcNow.Ticks));
            r=Reply(s);r.AccountId=new String('b',64);
            check("推荐自身携带错误账号时拒绝",!Same(s,r)&&!InlineSuggestionWindow.Eligible(true,s,r,DateTime.UtcNow.Ticks));

            s=Snapshot(false);r=Reply(s);
            check("原私聊的正常填入条件继续有效",Same(s,r)&&InlineSuggestionWindow.Eligible(true,s,r,DateTime.UtcNow.Ticks));
            s.GroupChat=true;s.GroupId=new String('c',64);
            check("私聊转群不复用私聊推荐",!Same(s,r)&&!InlineSuggestionWindow.Eligible(true,s,r,DateTime.UtcNow.Ticks));
            s=Snapshot(false);r=Reply(s);s.DraftConsistent=false;
            check("私聊同样保留草稿一致性保护",!Same(s,r)&&!InlineSuggestionWindow.Eligible(true,s,r,DateTime.UtcNow.Ticks));
        }
        static void GroupHistoryChecks(Action<string,bool> check) {
            check("群端点独立确认账号和群但不替代成员确认",NativeMessageReader.ValidGroupScope(room,account,room)&&NativeMessageReader.ValidGroupScope(account,room,room)&&!NativeMessageReader.ValidGroupEndpoints(room,account,room,""));
            check("群端点仍拒绝空账号特殊账号和不同群",!NativeMessageReader.ValidGroupScope(room,"",room)&&!NativeMessageReader.ValidGroupScope(room,"other@chatroom",room)&&!NativeMessageReader.ValidGroupScope("999@chatroom",account,room)&&!NativeMessageReader.ValidGroupScope(account,member,room));
            var frame=GroupFrame();string revision;
            var texts=frame.Model.ToDictionary(r=>r.KeyTag,r=>"合成文字"+r.ModelIndex);
            check("有效群保留自己和多位已确认成员",WeChatReader.GroupFrameReason(frame)==null&&WeChatReader.ProjectMessages(frame,texts,true,out revision).Select(m=>m.SenderId).Distinct().Count()==3);
            MissingSender(frame.Model[1]);
            var originalKeys=frame.Model.Select(r=>r.KeyTag).ToArray();string tail=frame.Model.Last().KeyTag;
            var messages=WeChatReader.ProjectMessages(frame,texts,true,out revision);
            check("仅同账号同群的历史空成员被省略",WeChatReader.GroupFrameReason(frame)==null&&messages.Count==3&&messages.All(m=>MemoryIdentity.Valid(m.SenderId))&&!messages.Any(m=>m.Key==frame.Model[1].KeyTag));
            check("省略历史内容不删除原生键和真实尾部",frame.Model.Select(r=>r.KeyTag).SequenceEqual(originalKeys)&&frame.Model.Last().KeyTag==tail&&messages.Last().Key==tail);
            string changed;var alteredText=new Dictionary<string,string>(texts);alteredText[frame.Model[1].KeyTag]="历史缺成员行已编辑";
            WeChatReader.ProjectMessages(frame,alteredText,true,out changed);
            check("省略行文字变化仍撤销旧正文版本",revision!=changed);
            frame.Model[1].KeyTag="changed-missing-key";WeChatReader.ProjectMessages(frame,texts,true,out changed);
            check("省略行稳定键变化仍撤销旧正文版本",revision!=changed);
            frame=GroupFrame();MissingSender(frame.Model[0]);
            messages=WeChatReader.ProjectMessages(frame,texts,true,out revision);
            check("自己方向的空成员历史仅省略不推断自己",messages.Count==3&&messages.All(m=>m.Role=="peer")&&frame.Model[0].SenderId==null&&!frame.Model[0].GroupSenderKnown&&!frame.Model[0].DirectionKnown);
            frame=GroupFrame();MissingSender(frame.Model[1]);frame.Model[1].Type=10000;
            messages=WeChatReader.ProjectMessages(frame,texts,true,out revision);
            check("同作用域历史空成员非文字不进入模型",messages.Count==3&&messages.All(m=>m.Type!=10000));
            frame=GroupFrame();MissingSender(frame.Model[1]);MissingSender(frame.Model[2]);
            check("多个历史空成员不影响严格确认的最后来信",WeChatReader.ProjectMessages(frame,texts,true,out revision).Count==2);

            RejectFrame(check,"省略行属于其他账号",f=>{MissingSender(f.Model[1]);f.Model[1].AccountId=new String('f',64);},"group_scope_unavailable");
            RejectFrame(check,"省略行属于其他群",f=>{MissingSender(f.Model[1]);f.Model[1].GroupId=new String('f',64);},"group_scope_unavailable");
            RejectFrame(check,"省略行会话不一致",f=>{MissingSender(f.Model[1]);f.Model[1].SessionTag="other-scope";},"group_scope_unavailable");
            RejectFrame(check,"历史端点未确认不能继承尾消息身份",f=>{MissingSender(f.Model[1]);f.Model[1].GroupScopeKnown=false;},"group_scope_unavailable");
            RejectFrame(check,"已知成员历史也不能来自外群",f=>f.Model[0].GroupId=new String('f',64),"group_scope_unavailable");
            RejectFrame(check,"群历史不得混入私聊好友身份",f=>f.Model[0].FriendId=new String('f',64),"group_scope_unavailable");
            RejectFrame(check,"群历史不得混入私聊记忆键",f=>f.Model[0].MemoryKey="private-message","group_scope_unavailable");
            RejectFrame(check,"尾消息缺成员不能回退前一条",f=>MissingSender(f.Model.Last()),"group_latest_sender_unavailable");
            RejectFrame(check,"自己的尾消息缺成员也不推断发送者",f=>{var row=f.Model.Last();MissingSender(row);row.Peer=false;},"group_latest_sender_unavailable");
            RejectFrame(check,"真实尾部未支持不能回退前一条",f=>f.Count++,"group_latest_unsupported");
            RejectFrame(check,"最新账号端点未确认仍拒绝",f=>f.Model.Last().GroupScopeKnown=false,"group_scope_unavailable");
            RejectFrame(check,"历史非空非法成员不能按缺失省略",f=>{MissingSender(f.Model[1]);f.Model[1].SenderEvidence="member";},"group_history_sender_unavailable");
            RejectFrame(check,"历史冲突账号成员不能按缺失省略",f=>{MissingSender(f.Model[1]);f.Model[1].SenderEvidence="account";},"group_history_sender_unavailable");
            RejectFrame(check,"历史群或其他作用域成员不能省略",f=>{MissingSender(f.Model[1]);f.Model[1].SenderEvidence="other_scope";},"group_history_sender_unavailable");
            RejectFrame(check,"成员缺失时不得附带推断成员标识",f=>{MissingSender(f.Model[1]);f.Model[1].SenderId=new String('d',64);},"group_history_sender_unavailable");
            RejectFrame(check,"成员缺失时不得附带已确认方向",f=>{MissingSender(f.Model[1]);f.Model[1].DirectionKnown=true;},"group_history_sender_unavailable");
            RejectFrame(check,"有成员但角色证据冲突仍拒绝",f=>f.Model[0].SenderEvidence="member","group_history_sender_unavailable");
            RejectFrame(check,"历史索引次序异常仍拒绝",f=>f.Model[1].ModelIndex=0,"group_scope_unavailable");

            check("原生稳定双读接受一致群字段",SameRow(GroupRow(0),GroupRow(0)));
            var mutations=new Dictionary<string,Action<NativeMessageReader.Row>> {
                {"端点确认标记",r=>r.GroupScopeKnown=false},{"成员确认标记",r=>r.GroupSenderKnown=false},
                {"成员来源",r=>r.SenderEvidence="empty"},{"账号",r=>r.AccountId=new String('f',64)},
                {"群",r=>r.GroupId=new String('f',64)},{"成员",r=>r.SenderId=new String('f',64)},
                {"会话",r=>r.SessionTag="other"},{"候选成员摘要",r=>r.SenderTag="other"}
            };
            foreach(var change in mutations){var a=GroupRow(0);var b=GroupRow(0);change.Value(b);check("原生双读变化拒绝："+change.Key,!SameRow(a,b));}
        }
        static void RejectFrame(Action<string,bool> check,string label,Action<NativeMessageReader.Frame> change,string reason) {
            var frame=GroupFrame();change(frame);check("群历史兼容拒绝："+label,WeChatReader.GroupFrameReason(frame)==reason);
        }
        static bool SameRow(NativeMessageReader.Row a,NativeMessageReader.Row b){return (bool)sameNativeRow.Invoke(null,new object[]{a,b});}
        static void MissingSender(NativeMessageReader.Row row){row.GroupSenderKnown=false;row.DirectionKnown=false;row.SenderId=null;row.SenderEvidence="empty";row.SenderTag="empty-tag";}
        static NativeMessageReader.Frame GroupFrame(){return new NativeMessageReader.Frame{Valid=true,AtEnd=true,Count=4,Model=new List<NativeMessageReader.Row>{GroupRow(0),GroupRow(1),GroupRow(2),GroupRow(3)}};}
        static NativeMessageReader.Row GroupRow(int index) {
            bool self=index==0;char sender=self?'b':index==2?'e':'d';
            return new NativeMessageReader.Row{Supported=true,GroupChat=true,GroupScopeKnown=true,GroupSenderKnown=true,DirectionKnown=true,Peer=!self,
                AccountId=new String('a',64),GroupId=new String('c',64),SenderId=new String(sender,64),SessionTag="synthetic-group-scope",KeyTag="group-key-"+index,
                SenderTag="member-tag-"+sender,SenderEvidence=self?"account":"member",Type=1,Created=1,ModelIndex=index,LocalId=(uint)(index+1)};
        }
        static void Reject(Action<string,bool> check,string name,Action<ChatSnapshot> change) {
            var s=Snapshot(true);var r=Reply(s);change(s);
            check(name,!Same(s,r)&&!InlineSuggestionWindow.Eligible(true,s,r,DateTime.UtcNow.Ticks));
        }
        static bool Same(ChatSnapshot s,Recommendation r){return (bool)sameContext.Invoke(null,new object[]{s,r});}
        static ChatSnapshot Snapshot(bool group) {
            return new ChatSnapshot{Available=true,AtEnd=true,DraftEmpty=true,DraftConsistent=true,Foreground=true,Focused=true,ImeAllowed=true,
                GroupChat=group,AccountId=new String('a',64),GroupId=group?new String('c',64):null,FriendId=group?null:new String('e',64),
                Session="session-1",Tail="message-1",ContentRevision="revision-1",InputIdentity="input-1",Activity=10,CapturedTicks=DateTime.UtcNow.Ticks,
                Window=123,WindowLeft=0,WindowTop=0,WindowWidth=900,WindowHeight=600,InputLeft=20,InputTop=400,InputWidth=600,InputHeight=120};
        }
        static Recommendation Reply(ChatSnapshot s) {
            return new Recommendation{Id="synthetic-reply",Text="好，那就这么安排。",GroupChat=s.GroupChat,AccountId=s.AccountId,GroupId=s.GroupId,
                Session=s.Session,Tail=s.Tail,ContentRevision=s.ContentRevision,InputIdentity=s.InputIdentity,Activity=s.Activity,CreatedTicks=DateTime.UtcNow.Ticks};
        }
    }
}
