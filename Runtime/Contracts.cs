using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WeChatPet.Runtime {
    public sealed class ChatImage { public string Mime {get;set;} public string Data {get;set;} }
    public sealed class ChatMessage {
        public string MemoryKey {get;set;}
        public ChatImage Image {get;set;}
        public string Key { get; set; }
        public string Role { get; set; }
        public string SenderId { get; set; }
        public string SenderName { get; set; }
        public string Text { get; set; }
        public uint Type { get; set; }
        public uint LocalId { get; set; }
        public long CreatedTicks { get; set; }
    }
    public sealed class ChatSnapshot {
        public string AccountId {get;set;}
        public string FriendId {get;set;}
        public string GroupId {get;set;}
        public bool Available { get; set; }
        public bool GroupChat { get; set; }
        public int GroupMessageCount,GroupMemberCount,GroupSelfMessages,GroupVerifiedMessages,GroupEmptySenderMessages;
        public string Reason { get; set; }
        public string Session { get; set; }
        public string InputIdentity { get; set; }
        public string ViewIdentity { get; set; }
        public string Contact { get; set; }
        public string Tail { get; set; }
        public string ContentRevision { get; set; }
        public string[] Keys { get; set; }
        public List<ChatMessage> Messages { get; set; }
        public bool AtEnd { get; set; }
        public bool DraftEmpty { get; set; }
        public bool DraftConsistent { get; set; }
        public bool Focused { get; set; }
        public bool Foreground { get; set; }
        public bool ImeAllowed { get; set; }
        public bool Composing { get; set; }
        public long Layout { get; set; }
        public long Activity { get; set; }
        public long Window { get; set; }
        public long CapturedTicks { get; set; }
        // UI Automation reports physical screen coordinates, never OCR positions.
        public double InputLeft, InputTop, InputWidth, InputHeight;
        public double WindowLeft, WindowTop, WindowWidth, WindowHeight;
    }
    public sealed class Recommendation {
        public const int MaxTextLength=16000;
        public static bool ValidText(string text){return !String.IsNullOrWhiteSpace(text)&&text.Length<=MaxTextLength;}
        public string Id, Session, Tail, Text, ContentRevision, InputIdentity;
        public string AccountId, GroupId;
        public bool GroupChat;
        public long Activity, CreatedTicks;
    }
    // Time is injected so cancellation and baseline behavior can be replayed without a desktop.
    public sealed class RecommendationController {
        public bool Paused, Connected;
        public ChatSnapshot Current { get; private set; }
        public Recommendation Ready { get; private set; }
        public string PendingId { get; private set; }
        public event Action<string, List<ChatMessage>> Generate;
        public event Action Invalidated;
        public event Action Changed;
        string session, tail, candidate, candidateInput, pendingSession, pendingTail, pendingContent, pendingInput;
        long candidateAt, candidateSeenAt, candidateActivity, pendingActivity, watchSince;
        bool candidateHasText;
        sealed class RecoveryContext {
            public string Session,Tail,Input,Account,Friend,Group,Origin;
            public bool GroupChat;
            public long Activity,Deadline;
        }
        RecoveryContext pendingContext,recovery;
        int recoveryCount;
        bool candidateIsRecovery,candidateNeedsStableRead;
        string candidateContent,candidateAccount,candidateFriend;
        string candidateOrigin, lastVisitedChat, lastVisitedView;
        bool emptyWatch;
        string view;
        long emptySince;
        uint highestLocalId;
        long lastAvailable;
        bool observingGroup;
        public string LastDecision { get; private set; }
        public string LastGroupBlockReason { get; private set; }
        public int EmptyChatObservations { get; private set; }
        public string LastTriggerOrigin { get; private set; }
        readonly HashSet<string> seen = new HashSet<string>();
        public int TriggerCount { get; private set; }
        string VisitIdentity(ChatSnapshot s) {
            if(s==null||!s.Available||s.Keys==null)return null;
            if(s.Keys.Length==0) {
                if(String.IsNullOrEmpty(s.ViewIdentity))return null;
                if(s.ViewIdentity==lastVisitedView&&lastVisitedChat!=null)return lastVisitedChat;
                return "empty:"+s.ViewIdentity;
            }
            if(String.IsNullOrEmpty(s.Session))return null;
            // Stable account/friend tags survive observer reconnects. Session is
            // the fallback when local memory identity cannot be established.
            if(!String.IsNullOrEmpty(s.AccountId)&&!String.IsNullOrEmpty(s.FriendId))
                return "friend:"+s.AccountId.Length+":"+s.AccountId+":"+s.FriendId;
            return "session:"+s.Session;
        }
        public void Reset() {
            // A read/model/observer reset is not the user opening another chat.
            // Keep the last visit so recovery cannot repeatedly spend model quota.
            session = tail = candidate = view = null; emptyWatch=false;highestLocalId=0;seen.Clear(); Current = null; lastAvailable=0;recoveryCount=0;observingGroup=false;LastDecision="reset";Invalidate();
        }
        public void Invalidate() {
            PendingId = null; Ready = null;
            pendingContext=recovery=null;
            if(candidateIsRecovery)candidate=null;
            candidateIsRecovery=candidateNeedsStableRead=false;
            if (Invalidated != null) Invalidated();
            if (Changed != null) Changed();
        }
        void RecoverPending(long deadline) {
            // One retry belongs to an already-triggered message, never to passive
            // history. External invalidation always destroys this ticket.
            var saved=PendingId!=null&&recoveryCount==0&&pendingContext!=null&&!pendingContext.GroupChat?pendingContext:null;
            Invalidate();
            if(saved!=null) {saved.Deadline=deadline;recovery=saved;recoveryCount++;}
        }
        static bool RecoveryMatches(RecoveryContext r,ChatSnapshot s,long now) {
            return r!=null&&now<=r.Deadline&&s.Session==r.Session&&s.Tail==r.Tail&&s.InputIdentity==r.Input&&s.Activity==r.Activity&&s.AccountId==r.Account&&s.FriendId==r.Friend&&s.DraftEmpty&&s.DraftConsistent&&!s.Composing;
        }
        public void SetPaused(bool value) { Paused = value; Reset(); }
        static List<ChatMessage> GroupContext(ChatSnapshot s) {string reason;return GroupContext(s,out reason);}
        public static string GroupBlockReason(ChatSnapshot s) {string reason;GroupContext(s,out reason);return reason;}
        static List<ChatMessage> GroupContext(ChatSnapshot s,out string reason) {
            reason="group_read_unavailable";
            if(s==null)return null;
            if(!s.Available){reason=s.Reason??reason;return null;}
            reason="group_identity_unavailable";
            if(!s.GroupChat||!MemoryIdentity.Valid(s.AccountId)||!MemoryIdentity.Valid(s.GroupId)||!String.IsNullOrEmpty(s.FriendId))return null;
            reason="group_composing";if(s.Composing)return null;
            reason="group_draft_inconsistent";if(!s.DraftConsistent)return null;
            reason="group_has_draft";if(!s.DraftEmpty)return null;
            reason="group_not_at_end";if(!s.AtEnd)return null;
            reason="group_context_incomplete";
            if(String.IsNullOrEmpty(s.Session)||String.IsNullOrEmpty(s.InputIdentity)||String.IsNullOrEmpty(s.ContentRevision))return null;
            reason="group_empty";if(s.Keys==null||s.Keys.Length==0||s.Messages==null||s.Messages.Count==0)return null;
            reason="group_tail_changed";if(s.Keys[s.Keys.Length-1]!=s.Tail)return null;
            var latest=s.Messages[s.Messages.Count-1];
            if(latest==null||latest.Key!=s.Tail)return null;
            reason="group_latest_self";if(latest.Role=="self")return null;
            reason="group_latest_sender_unavailable";if(latest.Role!="peer"||!MemoryIdentity.Valid(latest.SenderId))return null;
            reason="group_latest_unsupported";if(latest.Type!=1||latest.Image!=null)return null;
            reason="group_latest_text_unavailable";if(String.IsNullOrWhiteSpace(latest.Text))return null;
            reason="group_latest_text_too_long";if(latest.Text.Length>2000)return null;
            var context=new List<ChatMessage>();int bytes=0;
            for(int i=s.Messages.Count-1;i>=0&&context.Count<30;i--) {
                var m=s.Messages[i];
                reason="group_context_incomplete";
                if(m==null)return null;
                // Non-text rows remain explicit type markers. Unloaded text rows
                // are omitted instead of masquerading as an empty utterance.
                if(m.Type==1&&(String.IsNullOrWhiteSpace(m.Text)||m.Text.Length>2000))continue;
                int size=m.Type==1?m.Text.Length:"[非文字消息，未解析]".Length;
                if(bytes+size>8000)break;
                reason="group_history_unsupported";if(m.Type==0||m.Image!=null)return null;
                reason="group_history_sender_unavailable";if(!MemoryIdentity.Valid(m.SenderId)||(m.Role!="peer"&&m.Role!="self"))return null;
                reason="group_tail_changed";if(String.IsNullOrEmpty(m.Key)||!s.Keys.Contains(m.Key))return null;
                bytes+=size;
                context.Insert(0,m.Type==1?m:new ChatMessage{Key=m.Key,MemoryKey=m.MemoryKey,Text="",Type=m.Type,Role=m.Role,SenderId=m.SenderId,SenderName=m.SenderName,CreatedTicks=m.CreatedTicks,LocalId=m.LocalId});
            }
            if(context.Count==0||context[context.Count-1].Key!=s.Tail){reason="group_tail_changed";return null;}
            reason=null;return context;
        }
        bool PendingGroupMatches(ChatSnapshot s) {
            return pendingContext!=null&&pendingContext.GroupChat&&s.GroupChat&&s.AccountId==pendingContext.Account&&s.GroupId==pendingContext.Group&&
                s.Session==pendingSession&&s.Tail==pendingTail&&s.ContentRevision==pendingContent&&s.InputIdentity==pendingInput&&s.Activity==pendingActivity;
        }
        static bool ReadyGroupMatches(ChatSnapshot s,Recommendation r,long now) {
            return r.GroupChat&&s.GroupChat&&s.AccountId==r.AccountId&&s.GroupId==r.GroupId&&s.Session==r.Session&&s.Tail==r.Tail&&
                s.ContentRevision==r.ContentRevision&&s.InputIdentity==r.InputIdentity&&s.Activity==r.Activity&&now>=r.CreatedTicks&&now-r.CreatedTicks<=TimeSpan.TicksPerSecond*60;
        }
        void ObserveGroup(ChatSnapshot s,long now) {
            // Group reads never create or recover a request. Only a fresh click
            // authorizes using this group's visible context.
            session=tail=candidate=view=null;emptyWatch=false;highestLocalId=0;seen.Clear();lastAvailable=0;
            lastVisitedChat=lastVisitedView=null;recovery=null;candidateIsRecovery=candidateNeedsStableRead=false;
            bool usable=!Paused&&Connected&&GroupContext(s)!=null;
            if((Ready!=null&&(!usable||!ReadyGroupMatches(s,Ready,now)))||
               (PendingId!=null&&(!usable||!PendingGroupMatches(s)))||(!observingGroup&&(Ready!=null||PendingId!=null)))Invalidate();
            observingGroup=true;
            LastDecision=Paused?"paused":!Connected?"model_disconnected":!usable?"group_not_ready":PendingId!=null?"group_generating":Ready!=null?"group_ready":"group_waiting_for_click";
        }
        public bool RequestGroupRecommendation(long now) {
            var s=Current;string reason;var context=GroupContext(s,out reason);
            LastGroupBlockReason=Paused?"paused":!Connected?"model_disconnected":reason;
            if(LastGroupBlockReason==null&&(s.CapturedTicks<=0||now<s.CapturedTicks||now-s.CapturedTicks>TimeSpan.TicksPerSecond))LastGroupBlockReason="group_snapshot_stale";
            if(LastGroupBlockReason!=null) {
                LastDecision="group_not_ready";return false;
            }
            if(PendingId!=null){LastDecision="group_generating";LastGroupBlockReason="group_generating";return false;}
            Invalidate();candidate=null;observingGroup=true;
            PendingId=Guid.NewGuid().ToString("N");pendingSession=s.Session;pendingTail=s.Tail;pendingContent=s.ContentRevision;pendingInput=s.InputIdentity;pendingActivity=s.Activity;TriggerCount++;
            pendingContext=new RecoveryContext{Session=s.Session,Tail=s.Tail,Input=s.InputIdentity,Account=s.AccountId,Group=s.GroupId,GroupChat=true,Activity=s.Activity,Origin="group_click",Deadline=long.MaxValue};
            LastDecision="generation_started";LastTriggerOrigin="group_click";
            if(Generate!=null)Generate(PendingId,context);
            if(Changed!=null)Changed();return true;
        }
        public void Observe(ChatSnapshot s, long now) {
            Current = s;
            if(s!=null&&s.Available&&s.GroupChat){ObserveGroup(s,now);return;}
            if(observingGroup) {
                // Even a transient read gap cancels a manual group request;
                // recovery must require another click, never an automatic retry.
                if(Ready!=null||PendingId!=null||recovery!=null)Invalidate();
                observingGroup=false;session=tail=candidate=view=null;emptyWatch=false;highestLocalId=0;seen.Clear();lastAvailable=0;
            }
            // An explicitly observed group is another conversation, not a read
            // interruption. Returning from it is a new private-chat visit.
            if(s!=null&&!s.Available&&s.GroupChat&&s.Reason=="private_chat_required")lastVisitedChat=lastVisitedView=null;
            string visit=VisitIdentity(s);
            bool chatOpened=Connected&&visit!=null&&visit!=lastVisitedChat;
            if(Connected&&visit!=null){lastVisitedChat=visit;lastVisitedView=s.ViewIdentity;}
            bool transient=s!=null&&!s.Available&&(s.Reason=="controls_unavailable"||s.Reason=="read_unavailable"||s.Reason=="input_activity"||(s.Reason!=null&&s.Reason.StartsWith("native_")));
            // A single torn read during message insertion must not turn that message
            // into history. Longer loss, minimization and actual chat changes rebaseline.
            if(!Paused&&Connected&&transient&&session!=null&&now>=lastAvailable&&now-lastAvailable<=TimeSpan.TicksPerSecond) {
                LastDecision="transient_read_gap";
                if(s.Reason=="input_activity") {candidate=null;Invalidate();}
                else if(PendingId!=null)RecoverPending(lastAvailable+TimeSpan.TicksPerSecond);
                else if(Ready!=null)Invalidate();
                else if(candidateIsRecovery)candidateNeedsStableRead=true;
                return;
            }
            if (Paused || !Connected || s == null || !s.Available || !s.AtEnd || s.Keys == null || (s.Keys.Length == 0 && String.IsNullOrEmpty(s.ViewIdentity))) {
                LastDecision=Paused?"paused":!Connected?"model_disconnected":s==null?"stale_read":!s.Available?(s.Reason??"read_unavailable"):!s.AtEnd?"history_position":"empty_messages";
                session = tail = candidate = view = null;emptyWatch=false;highestLocalId=0;seen.Clear();
                if (Ready != null || PendingId != null || recovery!=null || candidateIsRecovery) Invalidate();
                return;
            }
            lastAvailable=now;
            // A verified empty model is a live chat, not a failed read. Keep only
            // its observation anchor; no recommendation may survive clearing.
            if(s.Keys.Length==0) {
                EmptyChatObservations++;
                if(Ready!=null||PendingId!=null||recovery!=null||candidateIsRecovery)Invalidate();candidate=null;
                if(!emptyWatch||view!=s.ViewIdentity) {
                    if(view!=s.ViewIdentity){session=null;seen.Clear();highestLocalId=0;}
                    emptySince=now;
                }
                view=s.ViewIdentity;tail=null;emptyWatch=true;LastDecision="empty_chat_watching";return;
            }
            bool afterEmpty=emptyWatch&&view==s.ViewIdentity&&(session==null||session==s.Session);
            emptyWatch=false;
            if(afterEmpty){session=s.Session;watchSince=Math.Max(watchSince,emptySince);}
            if (session != s.Session || (!afterEmpty&&(tail == null || !s.Keys.Contains(tail)))) {
                LastDecision="history_baseline";
                Invalidate(); session = s.Session;view=s.ViewIdentity;tail = s.Tail; candidate = null; watchSince = now;
                highestLocalId=s.Messages==null||s.Messages.Count==0?0:s.Messages.Max(m=>m.LocalId);
                seen.Clear(); foreach (string key in s.Keys) seen.Add(key);
                if(!chatOpened)return;
                ChatMessage last=s.Messages==null?null:s.Messages.LastOrDefault();
                // Opening a chat is explicit interest in its unanswered tail;
                // the tail need not have arrived after listening began.
                candidate=s.DraftEmpty&&s.DraftConsistent&&!s.Composing&&last!=null&&last.Key==s.Tail&&last.Role=="peer"&&last.Type==1&&last.LocalId>0?s.Tail:null;
                candidateHasText=last!=null&&!String.IsNullOrEmpty(last.Text);
                recoveryCount=0;candidateIsRecovery=false;
                candidateOrigin="chat_open";candidateAt=candidateSeenAt=now;candidateInput=s.InputIdentity;candidateActivity=s.Activity;
                LastDecision=candidate!=null?(candidateHasText?"chat_open_candidate":"chat_open_waiting_for_text"):!s.DraftEmpty||!s.DraftConsistent?"draft_not_empty":s.Composing?"composing":last==null?"missing_message":last.Role!="peer"?"outgoing_message":last.Type!=1?"non_text_message":"message_time_or_identity";
            }
            if ((Ready != null && (Ready.Tail != s.Tail || Ready.ContentRevision != s.ContentRevision || Ready.InputIdentity != s.InputIdentity || Ready.Activity != s.Activity || now - Ready.CreatedTicks > TimeSpan.TicksPerSecond * 60)) ||
                (PendingId != null && (pendingTail != s.Tail || pendingContent != s.ContentRevision || pendingInput != s.InputIdentity || pendingActivity != s.Activity)) || !s.DraftEmpty || !s.DraftConsistent || s.Composing) {
                bool revisedContext=PendingId!=null&&pendingContext!=null&&RecoveryMatches(pendingContext,s,now)&&pendingContent!=s.ContentRevision;
                if(revisedContext)RecoverPending(long.MaxValue);
                else if (Ready != null || PendingId != null) Invalidate();
            }
            if (s.Tail != tail) {
                bool unseen = !seen.Contains(s.Tail);
                int previousIndex = Array.IndexOf(s.Keys, tail);
                bool append = afterEmpty || previousIndex >= 0;
                for(int i=0;i<=previousIndex;i++)if(!seen.Contains(s.Keys[i]))append=false;
                for(int i=previousIndex+1;i<s.Keys.Length;i++)if(seen.Contains(s.Keys[i]))append=false;
                tail = s.Tail;
                foreach (string key in s.Keys) seen.Add(key);
                // Keep deduplication bounded to the current model window plus recent keys.
                if (seen.Count > 4096) { seen.Clear(); foreach (string key in s.Keys) seen.Add(key); }
                ChatMessage latest = s.Messages == null ? null : s.Messages.LastOrDefault();
                if(afterEmpty&&latest!=null&&latest.LocalId<=highestLocalId)append=false;
                candidate = append && unseen && s.DraftEmpty && !s.Composing && latest != null && latest.Key == s.Tail && latest.Role == "peer" && latest.Type == 1 && latest.LocalId > 0 && latest.CreatedTicks >= watchSince-TimeSpan.TicksPerSecond*2 && latest.CreatedTicks <= now+TimeSpan.TicksPerSecond*5 ? s.Tail : null;
                candidateHasText=latest!=null&&!String.IsNullOrEmpty(latest.Text);
                recoveryCount=0;candidateIsRecovery=false;
                LastDecision=candidate!=null?(candidateHasText?"candidate":"waiting_for_text"):!append?"not_append":!unseen?"seen_message":!s.DraftEmpty?"draft_not_empty":s.Composing?"composing":latest==null?"missing_message":latest.Role!="peer"?"outgoing_message":latest.Type!=1?"non_text_message":"message_time_or_identity";
                candidateAt = candidateSeenAt = now; candidateInput=s.InputIdentity; candidateActivity=s.Activity;
                candidateOrigin=afterEmpty?"after_empty_chat":"appended_message";
            }
            view=s.ViewIdentity;
            if(s.Messages!=null&&s.Messages.Count>0)highestLocalId=Math.Max(highestLocalId,s.Messages.Max(m=>m.LocalId));
            if(recovery!=null) {
                var ticket=recovery;recovery=null;
                var latest=s.Messages==null?null:s.Messages.LastOrDefault();
                if(RecoveryMatches(ticket,s,now)&&latest!=null&&latest.Key==s.Tail&&latest.Role=="peer"&&latest.Type==1&&latest.LocalId>0&&!String.IsNullOrEmpty(latest.Text)) {
                    candidate=s.Tail;candidateInput=s.InputIdentity;candidateActivity=s.Activity;
                    candidateAccount=s.AccountId;candidateFriend=s.FriendId;candidateContent=s.ContentRevision;
                    candidateAt=candidateSeenAt=now;candidateHasText=true;candidateIsRecovery=true;candidateNeedsStableRead=false;
                    candidateOrigin=ticket.Origin+"_recovery";LastDecision="recovery_candidate";
                }
            }
            if (!s.DraftEmpty || !s.DraftConsistent || s.Composing || candidateInput!=s.InputIdentity || candidateActivity!=s.Activity) candidate = null;
            if(candidateIsRecovery&&(candidateAccount!=s.AccountId||candidateFriend!=s.FriendId))candidate=null;
            if(candidate==null)return;
            if(candidateIsRecovery&&(candidateNeedsStableRead||candidateContent!=s.ContentRevision)) {
                candidateAt=now;candidateContent=s.ContentRevision;candidateNeedsStableRead=false;
            }
            ChatMessage candidateMessage=s.Messages==null?null:s.Messages.LastOrDefault();
            if(candidateMessage==null||candidateMessage.Key!=candidate||candidateMessage.Role!="peer"||candidateMessage.Type!=1||candidateMessage.LocalId==0) {candidate=null;return;}
            // Native identity can precede UIA text. Only an explicit chat-open or
            // newly observed arrival may wait; passive history recovery never does.
            if(!candidateHasText) {
                if(now-candidateSeenAt>TimeSpan.TicksPerSecond*2) {candidate=null;LastDecision="text_wait_expired";return;}
                if(String.IsNullOrEmpty(candidateMessage.Text)) {LastDecision="waiting_for_text";return;}
                candidateHasText=true;candidateAt=now;
            }
            if(String.IsNullOrEmpty(candidateMessage.Text)) {candidateHasText=false;return;}
            if (now - candidateAt < TimeSpan.TicksPerMillisecond * 600) return;
            candidate = null;
            var context = new List<ChatMessage>(); int bytes = 0;
            for (int i = s.Messages.Count - 1; i >= 0 && context.Count < 30; i--) {
                ChatMessage m = s.Messages[i];
                if (m.Type != 1 || String.IsNullOrEmpty(m.Text) || m.Text.Length > 2000) continue;
                int size = m.Text.Length;
                if (bytes + size > 8000) break;
                bytes += size; context.Insert(0, m);
            }
            if (context.Count == 0 || context[context.Count - 1].Key != s.Tail) return;
            PendingId = Guid.NewGuid().ToString("N"); pendingSession = s.Session; pendingTail = s.Tail; pendingContent = s.ContentRevision; pendingInput = s.InputIdentity; pendingActivity = s.Activity; TriggerCount++;
            LastDecision="generation_started";
            LastTriggerOrigin=candidateOrigin;
            pendingContext=new RecoveryContext {Session=s.Session,Tail=s.Tail,Input=s.InputIdentity,Account=s.AccountId,Friend=s.FriendId,Activity=s.Activity,Origin=candidateOrigin,Deadline=long.MaxValue};
            candidateIsRecovery=false;
            if (Generate != null) Generate(PendingId, context);
            if (Changed != null) Changed();
        }
        public bool Complete(string id, string text, long now) {
            ChatSnapshot s = Current;
            if (String.IsNullOrEmpty(id)||String.IsNullOrEmpty(PendingId)||id != PendingId || s == null || !s.Available || !s.AtEnd || !s.DraftEmpty || !s.DraftConsistent || s.Composing || s.Session != pendingSession || s.Tail != pendingTail || s.ContentRevision != pendingContent || s.InputIdentity != pendingInput || s.Activity != pendingActivity || Paused || !Connected) return false;
            if(s.GroupChat?(GroupContext(s)==null||!PendingGroupMatches(s)):pendingContext!=null&&pendingContext.GroupChat)return false;
            PendingId = null;
            if (!Recommendation.ValidText(text)) { Invalidate(); return false; }
            Ready = new Recommendation { Id = id, Session = s.Session, Tail = s.Tail, ContentRevision = s.ContentRevision, InputIdentity = s.InputIdentity, Activity = s.Activity, Text = text, CreatedTicks = now,AccountId=s.AccountId,GroupId=s.GroupId,GroupChat=s.GroupChat };
            if (Changed != null) Changed(); return true;
        }
    }
}
