using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace WeChatPet.Runtime {
    public sealed class WeChatReader {
        readonly int pid; readonly long birth;readonly IntPtr window;
        public AutomationElement Field { get; private set; }
        internal NativeMessageReader.Frame NativeFrame {get;private set;}
        static long Birth(int pid){using(var p=Process.GetProcessById(pid))return p.StartTime.ToUniversalTime().Ticks;}
        public WeChatReader(int pid,long hwnd):this(pid,Birth(pid),hwnd) { }
        public WeChatReader(int pid,long birth,long hwnd) { this.pid=pid;this.birth=birth;window=new IntPtr(hwnd); }
        public ChatSnapshot Read(long activity) {
            var s=new ChatSnapshot { Reason="controls_unavailable", Window=window.ToInt64(), Activity=activity, CapturedTicks=DateTime.UtcNow.Ticks };
            Field=null;NativeFrame=null;
            if(!TargetIdentity.IsCurrent(pid,birth,window.ToInt64())) {s.Reason="target_identity_changed";return s;}
            if(!Win.IsWindowVisible(window)||Win.IsIconic(window)) { s.Reason="wechat_hidden_or_minimized"; return s; }
            AutomationElement root=AutomationElement.FromHandle(window), title=null,input=null,list=null,groupCount=null;
            TreeWalker w=TreeWalker.RawViewWalker; var q=new Queue<AutomationElement>();q.Enqueue(root); int count=0;
            while(q.Count>0&&count++<1000) {
                var e=q.Dequeue(); var c=e.Current; string id=c.AutomationId; int dot=id.LastIndexOf('.'); if(dot>=0) id=id.Substring(dot+1);
                if(id=="current_chat_name_label"&&c.ClassName=="mmui::XTextView") { if(title!=null) return s; title=e; }
                if(id=="current_chat_count_label"&&c.ControlType==ControlType.Text) { if(groupCount!=null)return s;groupCount=e; }
                if(id=="chat_input_field"&&c.ClassName=="mmui::ChatInputField") { if(input!=null) return s; input=e; }
                if(id=="chat_message_list"&&c.ClassName=="mmui::RecyclerListView") { if(list!=null) return s; list=e;continue; }
                if(c.ClassName=="mmui::ChatSessionList"||c.ClassName=="mmui::ChatSessionCell")continue;
                for(var child=w.GetFirstChild(e);child!=null;child=w.GetNextSibling(child))q.Enqueue(child);
            }
            if(q.Count!=0||title==null||input==null||list==null)return s;
            string contact=title.Current.Name; int[] listId=list.GetRuntimeId();
            object vp,tp;
            if(listId.Length<4||!input.TryGetCurrentPattern(ValuePattern.Pattern,out vp)||!input.TryGetCurrentPattern(TextPattern.Pattern,out tp))return s;
            var value=(ValuePattern)vp; string draft=value.Current.Value; string text=((TextPattern)tp).DocumentRange.GetText(-1);
            string groupCountIdentity=null,groupCountText=null;
            if(groupCount!=null&&!groupCount.Current.IsOffscreen) {
                string members=groupCount.Current.Name;
                if(HasGroupCount(members)) {
                    string groupId=String.Join(".",groupCount.GetRuntimeId());
                    if(!String.IsNullOrEmpty(contact)&&contact==title.Current.Name&&contact==InputName(input)&&
                       String.Join(".",listId)==String.Join(".",list.GetRuntimeId())&&
                       groupId==String.Join(".",groupCount.GetRuntimeId())&&members==groupCount.Current.Name&&!groupCount.Current.IsOffscreen) {
                        s.GroupChat=true;groupCountIdentity=groupId;groupCountText=members;
                    }
                    if(!s.GroupChat)return s;
                }
            }
            var ids=new List<int>();var texts=new List<string>();var elements=new List<AutomationElement>(); int rows=0;
            for(var row=w.GetFirstChild(list);row!=null&&rows++<200;row=w.GetNextSibling(row)) {
                if(row.Current.ClassName!="mmui::ChatTextItemView")continue;
                int[] rid=row.GetRuntimeId();ids.Add(rid[rid.Length-1]);texts.Add(row.Current.Name);elements.Add(row);
            }
            if(rows>=200)return s;
            var nf=NativeMessageReader.Sample(pid,listId[listId.Length-1],ids.ToArray());
            NativeFrame=nf;
            if(!nf.Valid) { s.Reason="native_"+nf.Reason;return s; }
            bool empty=nf.Count==0&&nf.Model.Count==0&&ids.Count==0;
            if(nf.Model.Count==0&&!empty) { s.Reason="native_no_supported_messages";return s; }
            if(!s.GroupChat&&!empty&&!PrivateTailKnown(nf)) { s.Reason="private_latest_unsupported";return s; }
            string session=empty?null:nf.Model[nf.Model.Count-1].SessionTag;
            var identity=empty?null:nf.Model[nf.Model.Count-1];
            if(s.GroupChat) {
                s.GroupMessageCount=nf.Model.Count;s.GroupMemberCount=nf.Model.Where(r=>r.GroupSenderKnown).Select(r=>r.SenderTag).Distinct().Count();
                s.GroupSelfMessages=nf.Model.Count(r=>r.GroupSenderKnown&&!r.Peer);s.GroupVerifiedMessages=nf.Model.Count(r=>r.GroupSenderKnown);
                s.GroupEmptySenderMessages=nf.Model.Count(r=>r.SenderEvidence=="empty");
                string groupReason=GroupFrameReason(nf);
                if(groupReason!=null){s.Reason=groupReason;return s;}
                s.AccountId=identity.AccountId;s.GroupId=identity.GroupId;
            }
            else if(nf.Model.Any(r=>r.GroupChat)){s.Reason="chat_kind_changed";return s;}
            else if(identity!=null&&MemoryIdentity.Valid(identity.AccountId)&&MemoryIdentity.Valid(identity.FriendId)&&nf.Model.All(r=>r.AccountId==identity.AccountId&&r.FriendId==identity.FriendId)) {
                s.AccountId=identity.AccountId;s.FriendId=identity.FriendId;
            }
            if(!s.GroupChat&&nf.Model.Any(r=>r.SessionTag!=session||!r.DirectionKnown)) { s.Reason="private_chat_required";return s; }
            if(nf.Visible.Count!=ids.Count)return s;
            var visibleText=new Dictionary<string,string>();
            for(int i=0;i<ids.Count;i++) {
                int[] rid=elements[i].GetRuntimeId();var nr=nf.Visible[i];
                if(!TryMapVisibleText(ids[i],texts[i],rid[rid.Length-1],elements[i].Current.Name,nr,visibleText))return s;
            }
            if(String.Join(".",listId)!=String.Join(".",list.GetRuntimeId())||contact!=title.Current.Name||contact!=InputName(input))return s;
            if(s.GroupChat&&(groupCount==null||groupCount.Current.IsOffscreen||groupCountText!=groupCount.Current.Name||groupCountIdentity!=String.Join(".",groupCount.GetRuntimeId())))return s;
            string contentRevision;s.Messages=ProjectMessages(nf,visibleText,s.GroupChat,out contentRevision);
            s.Session=session;s.Contact=contact;s.Keys=nf.Model.Select(r=>r.KeyTag).ToArray();s.Tail=empty?null:s.Keys[s.Keys.Length-1];
            s.ContentRevision=contentRevision;
            s.InputIdentity=contact+"|"+String.Join(".",input.GetRuntimeId());
            s.ViewIdentity=s.InputIdentity+"|"+String.Join(".",listId);
            var inputBounds=input.Current.BoundingRectangle;Win.ScreenRect windowBounds;
            if(!inputBounds.IsEmpty&&Win.PhysicalBounds(window,out windowBounds)) {
                s.InputLeft=inputBounds.Left;s.InputTop=inputBounds.Top;s.InputWidth=inputBounds.Width;s.InputHeight=inputBounds.Height;
                s.WindowLeft=windowBounds.Left;s.WindowTop=windowBounds.Top;s.WindowWidth=windowBounds.Right-windowBounds.Left;s.WindowHeight=windowBounds.Bottom-windowBounds.Top;
            }
            s.AtEnd=nf.AtEnd;s.DraftConsistent=draft==text;s.DraftEmpty=draft.Length==0&&s.DraftConsistent&&!value.Current.IsReadOnly&&input.Current.IsEnabled&&!input.Current.IsOffscreen;
            s.Foreground=Win.GetForegroundWindow()==window;s.Focused=input.Current.HasKeyboardFocus;
            uint owner; s.Layout=Win.GetKeyboardLayout(Win.GetWindowThreadProcessId(window,out owner)).ToInt64();
            if(owner!=pid)return s;
            if(s.Foreground) {
                var clock=Stopwatch.StartNew(); var ime=QtCompositionGate.Sample(window.ToInt64());
                s.Composing=Convert.ToString(ime["compositionState"])=="active_reported";
                s.ImeAllowed=clock.ElapsedMilliseconds<=100&&ImeAllowed(ime,s.Layout);
            }
            s.Available=true;s.Reason=empty?"empty_chat":"ok";s.CapturedTicks=DateTime.UtcNow.Ticks;Field=input;return s;
        }
        static bool GroupScopeKnown(NativeMessageReader.Row row) {
            return row!=null&&row.Supported&&row.GroupChat&&row.GroupScopeKnown&&MemoryIdentity.Valid(row.AccountId)&&MemoryIdentity.Valid(row.GroupId)&&
                !String.IsNullOrEmpty(row.SessionTag)&&!String.IsNullOrEmpty(row.KeyTag)&&String.IsNullOrEmpty(row.FriendId)&&String.IsNullOrEmpty(row.MemoryKey);
        }
        internal static bool PrivateTailKnown(NativeMessageReader.Frame frame) {
            // The native adapter omits unknown row classes. An older readable
            // peer message must not become the tail when the actual last row
            // was omitted; its sender and type have not been established.
            if(frame==null||!frame.Valid||frame.Count<=0||frame.Model==null||frame.Model.Count==0)return false;
            var latest=frame.Model[frame.Model.Count-1];
            return latest!=null&&latest.Supported&&latest.ModelIndex==frame.Count-1;
        }
        static bool GroupSenderKnown(NativeMessageReader.Row row) {
            return row.GroupSenderKnown&&row.DirectionKnown&&MemoryIdentity.Valid(row.SenderId)&&row.SenderEvidence==(row.Peer?"member":"account");
        }
        internal static string GroupFrameReason(NativeMessageReader.Frame frame) {
            if(frame==null||!frame.Valid||frame.Model==null||frame.Model.Count==0)return "group_scope_unavailable";
            var latest=frame.Model[frame.Model.Count-1];
            if(latest==null||latest.ModelIndex!=frame.Count-1)return "group_latest_unsupported";
            if(!GroupScopeKnown(latest))return "group_scope_unavailable";
            // Never recover an older message as the tail when the actual last sender is unknown.
            if(!GroupSenderKnown(latest))return "group_latest_sender_unavailable";
            int previous=-1;
            foreach(var row in frame.Model) {
                if(!GroupScopeKnown(row)||row.AccountId!=latest.AccountId||row.GroupId!=latest.GroupId||row.SessionTag!=latest.SessionTag||
                   row.ModelIndex<=previous||row.ModelIndex>latest.ModelIndex)return "group_scope_unavailable";
                previous=row.ModelIndex;
                if(GroupSenderKnown(row))continue;
                // A missing member may be omitted only in older rows whose account/room is proved.
                // A nonempty conflicting/invalid member is never treated as a missing one.
                if(row.ModelIndex>=latest.ModelIndex||row.GroupSenderKnown||row.DirectionKnown||row.SenderEvidence!="empty"||!String.IsNullOrEmpty(row.SenderId))
                    return "group_history_sender_unavailable";
            }
            return null;
        }
        internal static List<ChatMessage> ProjectMessages(NativeMessageReader.Frame frame,IDictionary<string,string> visibleText,bool group,out string revision) {
            if(group){string reason=GroupFrameReason(frame);if(reason!=null)throw new InvalidOperationException(reason);}
            var all=new List<ChatMessage>();var included=new List<ChatMessage>();
            foreach(var row in frame.Model) {
                string body;visibleText.TryGetValue(row.KeyTag,out body);
                bool include=!group||GroupSenderKnown(row);
                var message=new ChatMessage {Key=row.KeyTag,MemoryKey=row.MemoryKey,SenderId=row.SenderId,Type=row.Type,LocalId=row.LocalId,
                    CreatedTicks=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(row.Created).Ticks,Role=include?(row.Peer?"peer":"self"):null,Text=body};
                all.Add(message);if(include)included.Add(message);
            }
            // Hash every row before omission. Removed/edited senderless history must still
            // invalidate an older recommendation; only verified rows reach the model.
            object evidence=group?(object)new {Messages=all,Rows=frame.Model.Select(r=>new {r.KeyTag,r.SessionTag,r.AccountId,r.GroupId,r.SenderTag,r.SenderEvidence,r.GroupScopeKnown,r.GroupSenderKnown,r.ModelIndex}).ToArray()}:all;
            using(var sha=SHA256.Create())revision=Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(evidence))));
            return included;
        }
        internal static bool HasGroupCount(string text) {
            if(String.IsNullOrEmpty(text)||!Regex.IsMatch(text,@"^\([0-9]{1,6}\)$"))return false;
            int members;return Int32.TryParse(text.Substring(1,text.Length-2),out members)&&members>=2;
        }
        internal static bool TryMapVisibleText(int expectedId,string expectedText,int currentId,string currentText,NativeMessageReader.Row row,Dictionary<string,string> visibleText) {
            if(row==null||currentId!=expectedId||row.AccessibleId!=expectedId||currentText!=expectedText||String.IsNullOrEmpty(row.KeyTag))return false;
            // Quoted and rich-content messages can share ChatTextItemView with plain text.
            // Keep their native identity/type, but never treat their UI label as message text.
            if(row.Type==1&&expectedText!=null&&expectedText.Length<=2000)visibleText[row.KeyTag]=expectedText;
            return true;
        }
        public static string InputName(AutomationElement input) { return Regex.Replace(input.Current.Name,@"\s*按住鼠标\s*语音输入文字\s*$",""); }
        public static bool ImeAllowed(Dictionary<string,object> ime,long layout) {
            object sampled;
            return Convert.ToBoolean(ime["valid"])&&!Convert.ToBoolean(ime["sampleStaleForTab"])&&Convert.ToString(ime["compositionState"])=="inactive_reported"&&ime.TryGetValue("keyboardLayoutValue",out sampled)&&Convert.ToInt64(sampled)==layout;
        }
    }
}
