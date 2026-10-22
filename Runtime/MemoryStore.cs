using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WeChatPet.Runtime {
    public sealed class MemoryItem {
        public string Id,Text,Kind,SourceKey,SourceRole,SourceText,SubjectRole;
        public long SourceTicks,UpdatedTicks,SourceOrder;
        public bool Pinned,Manual,Superseded;
    }
    public sealed class PreferenceHead { public long Ticks,Order; public bool Ambiguous; public string Fingerprint; }
    public sealed class RemovedFriendMemory { public string AccountId,Id; public long IgnoreBefore; }
    public sealed class FriendMemory {
        public string Id,AccountId,Name;
        public string Preferences="";
        public List<string> ExpressionExamples=new List<string>();
        public bool Enabled=true;
        public long IgnoreBefore,UpdatedTicks;
        public List<MemoryItem> Items=new List<MemoryItem>();
        public List<string> Seen=new List<string>();
        public Dictionary<string,PreferenceHead> PreferenceHeads=new Dictionary<string,PreferenceHead>();
        public string DisplayName {get;set;}
        public override string ToString(){return DisplayName??Name;}
    }
    public sealed class GroupMemory {
        public string Id,AccountId,Name;
        public string Preferences="",Agreements="";
        public bool Enabled=true;
        public long UpdatedTicks;
        public string DisplayName {get;set;}
        public override string ToString(){return DisplayName??Name;}
    }
    public sealed class GroupMemoryContext {
        public string ReplyPreferences="",Agreements="";
    }
    public sealed class FriendProfileContext {
        public string ReplyPreferences="";
        public readonly List<string> FriendNotes=new List<string>(),SelfNotes=new List<string>();
    }
    public sealed class MemoryData {
        public int Version=4;
        public bool Enabled=true;
        // Only retained to deserialize v1. Never used in recommendation context.
        public string Preferences="";
        public string LegacyPreferences="";
        public long IgnoreBefore;
        public List<FriendMemory> Friends=new List<FriendMemory>();
        // Added in v4 so older binaries refuse this file instead of discarding
        // group fields during a save. Never infer a group from a friend's name.
        public List<GroupMemory> Groups=new List<GroupMemory>();
        // Content-free barriers survive removal and restart. Existing v3 files
        // deserialize with empty collections; no account identity is inferred.
        public Dictionary<string,long> AccountIgnoreBefore=new Dictionary<string,long>();
        public List<RemovedFriendMemory> RemovedFriends=new List<RemovedFriendMemory>();
    }
    // Bounded, extractive memory. A quote is evidence of what was said, never a verified fact.
    public sealed class MemoryStore {
        readonly string path;
        public MemoryData Data {get;private set;}
        public string Error {get;private set;}
        public bool CanEdit {get{return !loadFailed;}}
        bool dirty,loadFailed;
        public MemoryStore(string directory) {
            path=Path.Combine(directory,"memory.dpapi");Data=new MemoryData();
            try {
                if(File.Exists(path)) {
                    if(new FileInfo(path).Length>16*1024*1024)throw new IOException();
                    Data=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Deserialize<MemoryData>(LocalStore.Read(path,true));
                    if(Data==null||(Data.Version!=1&&Data.Version!=2&&Data.Version!=3&&Data.Version!=4)||!ValidTicks(Data.IgnoreBefore)||Data.Friends==null||Data.Friends.Count>500||Data.Friends.Any(f=>f==null||!MemoryIdentity.Valid(f.Id)||!MemoryIdentity.Valid(f.AccountId)||!ValidTicks(f.IgnoreBefore)||!ValidTicks(f.UpdatedTicks)||f.Items==null||f.Seen==null||f.Items.Any(i=>i==null||i.Text==null||!ValidTicks(i.SourceTicks)||!ValidTicks(i.UpdatedTicks)||i.SourceOrder<0)))throw new IOException();
                    // One profile per account/contact. A duplicate must not make
                    // settings edit one record while recommendation reads another.
                    if(Data.Friends.Select(f=>f.AccountId+":"+f.Id).Distinct().Count()!=Data.Friends.Count)throw new IOException();
                    if(Data.Friends.Any(f=>f.PreferenceHeads!=null&&(f.PreferenceHeads.Count>256||f.PreferenceHeads.Any(p=>p.Key.Length>160||p.Value==null||p.Value.Ticks<0||p.Value.Ticks>DateTime.MaxValue.Ticks||p.Value.Order<0||(p.Value.Fingerprint!=null&&!MemoryIdentity.Valid(p.Value.Fingerprint))))))throw new IOException();
                    if(Data.AccountIgnoreBefore==null)Data.AccountIgnoreBefore=new Dictionary<string,long>();
                    if(Data.RemovedFriends==null)Data.RemovedFriends=new List<RemovedFriendMemory>();
                    if(Data.Groups==null)Data.Groups=new List<GroupMemory>();
                    if(Data.Groups.Count>500||Data.Groups.Any(g=>g==null||!MemoryIdentity.Valid(g.Id)||!MemoryIdentity.Valid(g.AccountId)||!ValidTicks(g.UpdatedTicks)||!ValidGroupText(g.Name??"",80)||!ValidGroupText(g.Preferences??"",400)||!ValidGroupText(g.Agreements??"",2000))||Data.Groups.Select(g=>g.AccountId+":"+g.Id).Distinct().Count()!=Data.Groups.Count)throw new IOException();
                    foreach(var g in Data.Groups){g.Name=g.Name??"";g.Preferences=g.Preferences??"";g.Agreements=g.Agreements??"";}
                    if(Data.AccountIgnoreBefore.Count>500||Data.AccountIgnoreBefore.Any(p=>!MemoryIdentity.Valid(p.Key)||!ValidTicks(p.Value)))throw new IOException();
                    if(Data.RemovedFriends.Count>5000||Data.RemovedFriends.Any(f=>f==null||!MemoryIdentity.Valid(f.AccountId)||!MemoryIdentity.Valid(f.Id)||!ValidTicks(f.IgnoreBefore))||Data.RemovedFriends.Select(f=>f.AccountId+":"+f.Id).Distinct().Count()!=Data.RemovedFriends.Count)throw new IOException();
                    foreach(var f in Data.Friends) {
                        if(f.ExpressionExamples==null)f.ExpressionExamples=new List<string>();
                        if(f.ExpressionExamples.Count>5||f.ExpressionExamples.Any(x=>String.IsNullOrWhiteSpace(x)||x.Length>200||Regex.IsMatch(x,@"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")))throw new IOException();
                    }
                    if(Data.Version==1) {
                        // There is no trustworthy recipient on a v1 global preference.
                        // Keep it for explicit assignment; copying it to every friend
                        // would preserve the original cross-contact bug.
                        Data.LegacyPreferences=Data.Preferences??"";Data.Preferences="";
                    }
                    if(Data.Version<4){Data.Version=4;dirty=true;Save();}
                }
            }catch {loadFailed=true;Error="记忆文件未能读取，已暂停记忆；原文件保留";Data=new MemoryData{Enabled=false};}
        }
        public static bool Identified(ChatSnapshot s){return s!=null&&s.Available&&!s.GroupChat&&MemoryIdentity.Valid(s.AccountId)&&MemoryIdentity.Valid(s.FriendId)&&String.IsNullOrEmpty(s.GroupId);}
        public FriendMemory Find(string account,string friend){return Data.Friends.FirstOrDefault(f=>f.AccountId==account&&f.Id==friend);}
        public FriendMemory Current(ChatSnapshot s){return Identified(s)?Find(s.AccountId,s.FriendId):null;}
        public static bool GroupIdentified(ChatSnapshot s){return s!=null&&s.Available&&s.GroupChat&&MemoryIdentity.Valid(s.AccountId)&&MemoryIdentity.Valid(s.GroupId)&&String.IsNullOrEmpty(s.FriendId);}
        public GroupMemory FindGroup(string account,string group){return Data.Groups.FirstOrDefault(g=>g.AccountId==account&&g.Id==group);}
        public GroupMemory CurrentGroup(ChatSnapshot s){return GroupIdentified(s)?FindGroup(s.AccountId,s.GroupId):null;}
        public string CapacityNotice(ChatSnapshot snapshot) {
            if(Error!=null||!Data.Enabled)return null;
            bool group=GroupIdentified(snapshot);
            if(group?(CurrentGroup(snapshot)!=null||Data.Groups.Count<500):(!Identified(snapshot)||Current(snapshot)!=null||Data.Friends.Count<500))return null;
            bool ownRecords=group?Data.Groups.Any(g=>g.AccountId==snapshot.AccountId):Data.Friends.Any(f=>f.AccountId==snapshot.AccountId);
            return "本机"+(group?"群聊":"好友")+"记录已满（500），"+(ownRecords?"清理已有记录后可新增。":"请切换到有记录的账号清理。");
        }
        static bool ValidTicks(long ticks){return ticks>=0&&ticks<=DateTime.MaxValue.Ticks;}
        static bool ValidGroupText(string value,int limit){return value!=null&&value.Length<=limit&&!Regex.IsMatch(value,@"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]");}
        static string GroupText(string value,int limit,string label){
            value=(value??"").Trim();
            if(value.Length>limit)throw new InvalidOperationException(label+"最多"+limit+"字");
            if(!ValidGroupText(value,limit))throw new InvalidOperationException(label+"包含不可用字符，请删除后保存");
            return value;
        }
        bool OwnsGroup(GroupMemory group){return group!=null&&Data.Groups.Any(g=>Object.ReferenceEquals(g,group));}
        static GroupMemory CloneGroup(GroupMemory g){return new GroupMemory{Id=g.Id,AccountId=g.AccountId,Name=g.Name,DisplayName=g.DisplayName,Preferences=g.Preferences,Agreements=g.Agreements,Enabled=g.Enabled,UpdatedTicks=g.UpdatedTicks};}
        static void ApplyGroup(GroupMemory target,GroupMemory candidate){target.Name=candidate.Name;target.Preferences=candidate.Preferences;target.Agreements=candidate.Agreements;target.Enabled=candidate.Enabled;target.UpdatedTicks=candidate.UpdatedTicks;}
        void CommitGroup(GroupMemory target,GroupMemory replacement){
            var candidate=RemovalCandidate();candidate.Groups=candidate.Groups.Select(g=>Object.ReferenceEquals(g,target)?replacement:g).ToList();
            if(Persist(candidate))ApplyGroup(target,replacement);
        }
        public void ObserveGroup(ChatSnapshot snapshot,long now){
            if(Error!=null||!Data.Enabled||!GroupIdentified(snapshot)||!ValidTicks(now))return;
            string name=Clip(snapshot.Contact,80);if(!ValidGroupText(name,80))return;
            var group=CurrentGroup(snapshot);
            if(group!=null){if(group.Name!=name){var renamed=CloneGroup(group);renamed.Name=name;renamed.UpdatedTicks=now;CommitGroup(group,renamed);}return;}
            if(Data.Groups.Count>=500)return;
            // Registration stores no messages or inferred member preferences.
            // Persist before exposing a new profile to settings.
            var candidate=RemovalCandidate();candidate.Groups.Add(new GroupMemory{Id=snapshot.GroupId,AccountId=snapshot.AccountId,Name=name,UpdatedTicks=now});
            if(Persist(candidate))Data=candidate;
        }
        public void SetGroupPreferences(GroupMemory group,string value){
            if(loadFailed||!OwnsGroup(group))return;
            var replacement=CloneGroup(group);replacement.Preferences=GroupText(value,400,"回复偏好");replacement.UpdatedTicks=DateTime.UtcNow.Ticks;CommitGroup(group,replacement);
        }
        public void SetGroupAgreements(GroupMemory group,string value){
            if(loadFailed||!OwnsGroup(group))return;
            var replacement=CloneGroup(group);replacement.Agreements=GroupText(value,2000,"群内约定");replacement.UpdatedTicks=DateTime.UtcNow.Ticks;CommitGroup(group,replacement);
        }
        public void SetGroupEnabled(GroupMemory group,bool value){
            if(loadFailed||!OwnsGroup(group))return;
            var replacement=CloneGroup(group);replacement.Enabled=value;replacement.UpdatedTicks=DateTime.UtcNow.Ticks;CommitGroup(group,replacement);
        }
        public void ClearGroup(GroupMemory group){
            if(loadFailed||!OwnsGroup(group))return;
            var replacement=CloneGroup(group);replacement.Preferences="";replacement.Agreements="";replacement.UpdatedTicks=DateTime.UtcNow.Ticks;CommitGroup(group,replacement);
        }
        public GroupMemoryContext GroupContext(ChatSnapshot snapshot){
            var empty=new GroupMemoryContext();
            if(Error!=null||!Data.Enabled||!GroupIdentified(snapshot))return empty;
            var group=CurrentGroup(snapshot);if(group==null||!group.Enabled)return empty;
            // Independent values are captured for this account and group only.
            return new GroupMemoryContext{ReplyPreferences=group.Preferences??"",Agreements=group.Agreements??""};
        }
        long AccountCutoff(string account){long cutoff;return Data.AccountIgnoreBefore.TryGetValue(account,out cutoff)?cutoff:0;}
        static bool UsableMessage(ChatMessage m,long cutoff,long now) {
            return m!=null&&m.Type==1&&MemoryIdentity.Valid(m.MemoryKey)&&!String.IsNullOrWhiteSpace(m.Text)&&m.Text.Length<=2000&&
                (m.Role=="peer"||m.Role=="self")&&m.CreatedTicks>cutoff&&m.CreatedTicks<=now+TimeSpan.TicksPerSecond*5&&m.CreatedTicks>=now-TimeSpan.TicksPerDay*30;
        }
        public void Observe(ChatSnapshot s,long now) {
            if(Error!=null||!Data.Enabled||!Identified(s)||!s.AtEnd||s.Messages==null)return;
            var friend=Current(s);
            if(friend==null) {
                if(Data.Friends.Count>=500)return;
                var removed=Data.RemovedFriends.FirstOrDefault(f=>f.AccountId==s.AccountId&&f.Id==s.FriendId);
                long cutoff=Math.Max(AccountCutoff(s.AccountId),removed==null?0:removed.IgnoreBefore);
                // Reopening an old chat is not consent to relearn removed data.
                // Recreate only after a definite later incoming text, and retain
                // the cutoff so older rows in that same snapshot stay forgotten.
                if(cutoff>0&&!s.Messages.Any(m=>UsableMessage(m,Math.Max(cutoff,Data.IgnoreBefore),now)&&m.Role=="peer"))return;
                friend=new FriendMemory{Id=s.FriendId,AccountId=s.AccountId,Name=Clip(s.Contact,80),IgnoreBefore=cutoff};Data.Friends.Add(friend);
                if(removed!=null)Data.RemovedFriends.Remove(removed);dirty=true;
            }
            if(friend.Name!=Clip(s.Contact,80)){friend.Name=Clip(s.Contact,80);dirty=true;}
            if(!friend.Enabled)return;
            foreach(var m in s.Messages) {
                if(!UsableMessage(m,Math.Max(Math.Max(friend.IgnoreBefore,Data.IgnoreBefore),AccountCutoff(s.AccountId)),now))continue;
                if(friend.Seen.Contains(m.MemoryKey))continue;
                friend.Seen.Add(m.MemoryKey);if(friend.Seen.Count>1024)friend.Seen.RemoveAt(0);
                var entry=new MemoryItem{Id=Guid.NewGuid().ToString("N"),Text=Clip(m.Text,400),SourceText=Clip(m.Text,2000),SourceRole=m.Role,SourceKey=m.MemoryKey,SourceTicks=m.CreatedTicks,SourceOrder=m.LocalId,UpdatedTicks=now,Kind="recent"};
                // Only short, explicit first-person preferences/naming claims; questions,
                // hypotheticals, quoted speech and jokes stay ordinary recent excerpts.
                if(m.Role=="peer"&&PreferenceRules.Key(m.Text,false)!=null)entry.Kind="stated";
                friend.Items.Add(entry);friend.UpdatedTicks=now;dirty=true;
            }
            Prune(friend,now);
        }
        void Prune(FriendMemory f,long now) {
            Reconcile(f,now);
            int before=f.Items.Count;
            f.Items.RemoveAll(i=>!i.Manual&&!i.Pinned&&(i.Superseded||i.SourceTicks<now-TimeSpan.TicksPerDay*(i.Kind=="stated"?30:7)));
            foreach(string kind in new[]{"recent","stated"}) {
                var excess=f.Items.Where(i=>i.Kind==kind&&!i.Manual&&!i.Pinned).OrderByDescending(i=>i.SourceTicks).Skip(kind=="recent"?30:20).ToList();
                foreach(var i in excess)f.Items.Remove(i);
            }
            if(before!=f.Items.Count)dirty=true;
        }
        static long EffectiveTime(MemoryItem i){return i.Manual?i.UpdatedTicks:i.SourceTicks;}
        static long EffectiveOrder(MemoryItem i){return i.Manual?Int64.MaxValue:i.SourceOrder;}
        static string PreferenceFingerprint(string text){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(PreferenceRules.Normalize(text)))).Replace("-","").ToLowerInvariant();}
        public static string Subject(MemoryItem item) {
            if(item==null)return null;
            // A real message's sender is immutable evidence. Explicit subjects
            // are only needed for newly created manual notes.
            if(item.SourceRole=="self"||item.SourceRole=="peer")return item.SourceRole;
            return item.SubjectRole=="self"||item.SubjectRole=="peer"?item.SubjectRole:null;
        }
        static int Compare(long time,long order,PreferenceHead h){int c=time.CompareTo(h.Ticks);return c!=0?c:order.CompareTo(h.Order);}
        void Supersede(MemoryItem i){if(!i.Superseded){i.Superseded=true;dirty=true;}}
        void Reconcile(FriendMemory f,long now) {
            if(f.PreferenceHeads==null){f.PreferenceHeads=new Dictionary<string,PreferenceHead>();dirty=true;}
            foreach(var i in f.Items.Where(i=>!i.Manual&&i.Kind=="stated"&&PreferenceRules.Key(i.Text,false)==null)){i.Kind="recent";dirty=true;}
            foreach(var key in f.PreferenceHeads.Where(p=>p.Value.Ticks<now-TimeSpan.TicksPerDay*30).Select(p=>p.Key).ToList()){f.PreferenceHeads.Remove(key);dirty=true;}
            // Older versions treated edited self excerpts as friend facts. Drop
            // only watermarks demonstrably written by such an edit; never infer
            // a subject for old notes that have no sender evidence.
            foreach(var own in f.Items.Where(i=>i.Manual&&Subject(i)=="self")) {
                string key=PreferenceRules.Key(own.Text,true);PreferenceHead bad;
                if(key!=null&&f.PreferenceHeads.TryGetValue(key,out bad)&&bad.Ticks==own.UpdatedTicks&&bad.Order==Int64.MaxValue){f.PreferenceHeads.Remove(key);dirty=true;}
            }
            var keyed=f.Items.Where(i=>!i.Superseded&&Subject(i)=="peer")
                .Select(i=>new{Item=i,Key=PreferenceRules.Key(i.Text,i.Manual)}).Where(x=>x.Key!=null).GroupBy(x=>x.Key);
            foreach(var group in keyed) {
                var newest=group.OrderByDescending(x=>EffectiveTime(x.Item)).ThenByDescending(x=>EffectiveOrder(x.Item)).First().Item;
                PreferenceHead head;
                if(!f.PreferenceHeads.TryGetValue(group.Key,out head)) {
                    if(f.PreferenceHeads.Count>=256){foreach(var x in group)Supersede(x.Item);continue;}
                    head=new PreferenceHead();f.PreferenceHeads[group.Key]=head;dirty=true;
                }
                if(Compare(EffectiveTime(newest),EffectiveOrder(newest),head)>0){head.Ticks=EffectiveTime(newest);head.Order=EffectiveOrder(newest);head.Ambiguous=false;head.Fingerprint=null;dirty=true;}
                var latest=group.Where(x=>Compare(EffectiveTime(x.Item),EffectiveOrder(x.Item),head)==0).ToList();
                // A confirming excerpt may already have been deduplicated into
                // a retained note. Its fingerprint still represents evidence
                // at this exact time/order, including after a restart.
                var latestFingerprints=latest.Select(x=>PreferenceFingerprint(x.Item.Text)).Distinct().ToList();
                bool ambiguous=head.Ambiguous||latestFingerprints.Count>1||head.Fingerprint!=null&&latestFingerprints.Any(value=>value!=head.Fingerprint);
                if(ambiguous&&!head.Ambiguous){head.Ambiguous=true;dirty=true;}
                if(!ambiguous&&latest.Count>0) {
                    string fingerprint=PreferenceFingerprint(latest[0].Item.Text);
                    if(head.Fingerprint!=fingerprint){head.Fingerprint=fingerprint;dirty=true;}
                }
                foreach(var x in group) {
                    // Repeating the same statement confirms a retained note;
                    // it must not turn that note into an expiring auto excerpt.
                    bool confirmed=(x.Item.Manual||x.Item.Pinned)&&head.Fingerprint!=null&&PreferenceFingerprint(x.Item.Text)==head.Fingerprint;
                    if(ambiguous||Compare(EffectiveTime(x.Item),EffectiveOrder(x.Item),head)<0&&!confirmed)Supersede(x.Item);
                }
            }
            // Repeated excerpts must not consume the prompt budget or outweigh newer facts.
            foreach(var group in f.Items.Where(i=>!i.Superseded).GroupBy(i=>(Subject(i)??"unknown")+"\n"+PreferenceRules.Normalize(i.Text)))
                foreach(var old in group.OrderByDescending(i=>i.Manual||i.Pinned).ThenByDescending(EffectiveTime).ThenByDescending(EffectiveOrder).Skip(1))Supersede(old);
        }
        public void Refresh(FriendMemory f,long now){if(f!=null)Prune(f,now);}
        public string Context(ChatSnapshot s,long now) {return Context(s,now,true);}
        // Keep the legacy text form for older callers; current requests carry
        // preferences separately so chat excerpts cannot crowd them out.
        public string Context(ChatSnapshot s,long now,bool includeProfile) {
            if(Error!=null||!Data.Enabled||!Identified(s))return "";
            var f=Current(s);if(f==null||!f.Enabled)return "";
            Prune(f,now);
            var result=new StringBuilder();
            if(includeProfile&&!String.IsNullOrWhiteSpace(f.Preferences))Append(result,"用户填写的当前好友设定（关系、背景与回复方式；不是好友自述）："+Clip(f.Preferences,400));
            Append(result,"当前好友显示名："+Clip(s.Contact,80));
            foreach(var i in f.Items.Where(i=>includeProfile&&!i.Superseded&&(i.Manual||i.Pinned||i.Kind=="stated")).OrderByDescending(EffectiveTime).ThenByDescending(i=>i.Pinned)) {
                string subject=Subject(i);if(subject==null)continue;
                Append(result,(i.Manual?(subject=="self"?"关于本人（self）的信息，由用户确认":"关于当前好友（peer）的信息，由用户确认"):subject=="self"?"本人（self）曾实际发出的原话":"当前好友（peer）曾自述（未核实，可能变化）")+" ["+Date(EffectiveTime(i))+"]："+i.Text);
            }
            var keys=new HashSet<string>((s.Messages??new List<ChatMessage>()).Where(m=>!String.IsNullOrEmpty(m.Text)).Select(m=>m.MemoryKey));
            foreach(var i in f.Items.Where(i=>!i.Superseded&&!i.Manual&&!i.Pinned&&i.Kind=="recent"&&!keys.Contains(i.SourceKey)).OrderByDescending(i=>i.SourceTicks).Take(8))
                Append(result,"近期话题摘录 · "+(i.SourceRole=="self"?"我实际发出":"对方")+" ["+Date(i.SourceTicks)+"]："+Clip(i.Text,180));
            return result.ToString();
        }
        public FriendProfileContext Profile(ChatSnapshot s,long now) {
            var result=new FriendProfileContext();
            if(Error!=null||!Data.Enabled||!Identified(s))return result;
            var friend=Current(s);if(friend==null||!friend.Enabled)return result;
            Prune(friend,now);result.ReplyPreferences=Clip(friend.Preferences,400).Trim();
            int remaining=1000;
            foreach(var item in friend.Items.Where(i=>!i.Superseded&&(i.Manual||i.Pinned||i.Kind=="stated"))
                .OrderByDescending(EffectiveTime).ThenByDescending(i=>i.Pinned).ThenByDescending(i=>i.Manual)) {
                string subject=Subject(item);if(subject==null)continue;
                var target=subject=="peer"?result.FriendNotes:result.SelfNotes;
                string evidence=item.Manual?"用户确认的描述":subject=="peer"?"好友自述，未核实":"本人实际发出的原话";
                string line=evidence+" ["+Date(EffectiveTime(item))+"]："+Clip(item.Text,400);
                // Preserve complete notes and their provenance; never cut a
                // remembered statement into a different meaning to fit a budget.
                if(target.Count>=30||line.Length>remaining)continue;
                target.Add(line);remaining-=line.Length;
            }
            return result;
        }
        static string Date(long ticks){return ticks>0?new DateTime(ticks,DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"手动";}
        static void Append(StringBuilder b,string line){if(b.Length+line.Length+Environment.NewLine.Length<=1500)b.AppendLine(line);}
        public static string Clip(string text,int limit){
            text=text??"";if(text.Length<=limit)return text;
            // The limits are UTF-16 budgets, but a boundary must not split an
            // emoji/supplementary character and poison later normalization.
            if(limit>0&&Char.IsHighSurrogate(text[limit-1])&&Char.IsLowSurrogate(text[limit]))limit--;
            return text.Substring(0,limit);
        }
        public void SetEnabled(bool value,long now){
            if(loadFailed||!ValidTicks(now))return;
            var candidate=RemovalCandidate();candidate.Enabled=value;candidate.IgnoreBefore=now;
            if(Persist(candidate)){Data.Enabled=value;Data.IgnoreBefore=now;}
        }
        public void SetFriendEnabled(FriendMemory f,bool value,long now){
            if(loadFailed||!Owns(f)||!ValidTicks(now))return;
            var candidate=CloneFriend(f);candidate.Enabled=value;candidate.IgnoreBefore=now;
            if(Persist(WithFriend(f,candidate))){f.Enabled=value;f.IgnoreBefore=now;}
        }
        bool Owns(FriendMemory friend){return friend!=null&&Data.Friends.Any(f=>Object.ReferenceEquals(f,friend));}
        public void SetPreferences(FriendMemory friend,string value){if(loadFailed||!Owns(friend))return;var candidate=CloneFriend(friend);candidate.Preferences=Clip(value,400).Trim();CommitFriend(friend,candidate);}
        public void SetExpressionExamples(FriendMemory friend,IEnumerable<string> values) {
            if(loadFailed||!Owns(friend))return;
            var examples=(values??new string[0]).Where(x=>!String.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if(examples.Count>5)throw new InvalidOperationException("最多保存5条表达示例");
            if(examples.Any(x=>x.Length>200))throw new InvalidOperationException("每条表达示例最多200字");
            if(examples.Any(x=>Regex.IsMatch(x,@"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")))throw new InvalidOperationException("表达示例包含不可用字符，请删除后保存");
            var candidate=CloneFriend(friend);candidate.ExpressionExamples=examples;CommitFriend(friend,candidate);
        }
        public string[] ExpressionExamples(ChatSnapshot snapshot) {
            if(Error!=null||!Data.Enabled||!Identified(snapshot))return new string[0];
            var friend=Current(snapshot);
            if(friend==null||!friend.Enabled||friend.ExpressionExamples==null)return new string[0];
            // Return an independent value snapshot. Switching contacts or editing
            // examples cannot change the data of an already dispatched request.
            return friend.ExpressionExamples.ToArray();
        }
        public void UseLegacyPreferences(FriendMemory friend){
            if(loadFailed||!Owns(friend)||String.IsNullOrWhiteSpace(Data.LegacyPreferences))return;
            var candidate=CloneFriend(friend);candidate.Preferences=Clip(Data.LegacyPreferences,400).Trim();
            var data=WithFriend(friend,candidate);data.LegacyPreferences="";data.Preferences="";
            if(Persist(data)){ApplyFriend(friend,candidate);Data.LegacyPreferences="";Data.Preferences="";}
        }
        public void Put(FriendMemory f,MemoryItem item,string text,bool pin,long now) {
            if(loadFailed||!Owns(f)||String.IsNullOrWhiteSpace(text)||(item!=null&&!f.Items.Contains(item)))return;
            if((item==null||!item.Manual&&!item.Pinned)&&f.Items.Count(i=>i.Manual||i.Pinned)>=30)throw new InvalidOperationException("最多保留30条手动或固定记忆");
            var candidate=CloneFriend(f);
            var edited=item==null?new MemoryItem{Id=Guid.NewGuid().ToString("N"),SourceTicks=now,Kind="note",SubjectRole="peer"}:candidate.Items[f.Items.IndexOf(item)];
            if(item==null)candidate.Items.Add(edited);
            // Editing an unassigned legacy note is an explicit confirmation in
            // the "about this friend" editor. Editing self text stays self.
            edited.SubjectRole=Subject(edited)??"peer";
            edited.Text=Clip(text.Trim(),400);edited.Manual=true;edited.Pinned=pin;edited.Superseded=false;edited.UpdatedTicks=now;Prune(candidate,now);CommitFriend(f,candidate);
        }
        static MemoryItem CloneItem(MemoryItem i){return new MemoryItem{Id=i.Id,Text=i.Text,Kind=i.Kind,SourceKey=i.SourceKey,SourceRole=i.SourceRole,SourceText=i.SourceText,SubjectRole=i.SubjectRole,SourceTicks=i.SourceTicks,UpdatedTicks=i.UpdatedTicks,SourceOrder=i.SourceOrder,Pinned=i.Pinned,Manual=i.Manual,Superseded=i.Superseded};}
        static FriendMemory CloneFriend(FriendMemory f){return new FriendMemory{Id=f.Id,AccountId=f.AccountId,Name=f.Name,DisplayName=f.DisplayName,Preferences=f.Preferences,ExpressionExamples=new List<string>(f.ExpressionExamples),Enabled=f.Enabled,IgnoreBefore=f.IgnoreBefore,UpdatedTicks=f.UpdatedTicks,Items=f.Items.Select(CloneItem).ToList(),Seen=new List<string>(f.Seen),PreferenceHeads=(f.PreferenceHeads??new Dictionary<string,PreferenceHead>()).ToDictionary(p=>p.Key,p=>new PreferenceHead{Ticks=p.Value.Ticks,Order=p.Value.Order,Ambiguous=p.Value.Ambiguous,Fingerprint=p.Value.Fingerprint})};}
        static void ApplyFriend(FriendMemory target,FriendMemory candidate) {
            var items=new List<MemoryItem>();
            foreach(var copy in candidate.Items) {
                var old=target.Items.FirstOrDefault(i=>i.Id==copy.Id);
                if(old==null){items.Add(copy);continue;}
                old.Text=copy.Text;old.Kind=copy.Kind;old.SubjectRole=copy.SubjectRole;old.Pinned=copy.Pinned;old.Manual=copy.Manual;old.Superseded=copy.Superseded;old.UpdatedTicks=copy.UpdatedTicks;items.Add(old);
            }
            target.Preferences=candidate.Preferences;target.ExpressionExamples=candidate.ExpressionExamples;target.Items=items;target.PreferenceHeads=candidate.PreferenceHeads;
        }
        void CommitFriend(FriendMemory target,FriendMemory candidate){if(Persist(WithFriend(target,candidate)))ApplyFriend(target,candidate);}
        MemoryData WithFriend(FriendMemory original,FriendMemory replacement) {
            return new MemoryData{Version=Data.Version,Enabled=Data.Enabled,Preferences=Data.Preferences,LegacyPreferences=Data.LegacyPreferences,IgnoreBefore=Data.IgnoreBefore,
                Friends=Data.Friends.Select(f=>Object.ReferenceEquals(f,original)?replacement:f).ToList(),Groups=Data.Groups,AccountIgnoreBefore=Data.AccountIgnoreBefore,RemovedFriends=Data.RemovedFriends};
        }
        public void Remove(FriendMemory f,MemoryItem item) {
            if(loadFailed||!Owns(f)||!f.Items.Contains(item))return;
            long now=DateTime.UtcNow.Ticks;
            var replacement=new FriendMemory{Id=f.Id,AccountId=f.AccountId,Name=f.Name,DisplayName=f.DisplayName,Enabled=f.Enabled,UpdatedTicks=f.UpdatedTicks,IgnoreBefore=now,
                Preferences=f.Preferences,ExpressionExamples=f.ExpressionExamples,Items=f.Items.Where(i=>!Object.ReferenceEquals(i,item)).ToList(),Seen=f.Seen,PreferenceHeads=f.PreferenceHeads};
            // Deletion is committed before changing the visible collection. A
            // failed write must not look like successful removal until restart.
            if(Persist(WithFriend(f,replacement))){f.Items.Remove(item);f.IgnoreBefore=now;}
        }
        public void ClearFriend(FriendMemory f,long now) {
            if(loadFailed||!Owns(f))return;
            var replacement=new FriendMemory{Id=f.Id,AccountId=f.AccountId,Name=f.Name,DisplayName=f.DisplayName,Enabled=f.Enabled,UpdatedTicks=f.UpdatedTicks,IgnoreBefore=now};
            if(!Persist(WithFriend(f,replacement)))return;
            f.Preferences="";f.ExpressionExamples.Clear();f.Items.Clear();f.Seen.Clear();if(f.PreferenceHeads!=null)f.PreferenceHeads.Clear();f.IgnoreBefore=now;
        }
        MemoryData RemovalCandidate() {
            // Keep content-free barriers even after message retention expires:
            // old chats must not silently reappear as empty friend profiles.
            return new MemoryData{Version=Data.Version,Enabled=Data.Enabled,Preferences=Data.Preferences,LegacyPreferences=Data.LegacyPreferences,IgnoreBefore=Data.IgnoreBefore,
                Friends=new List<FriendMemory>(Data.Friends),Groups=new List<GroupMemory>(Data.Groups),AccountIgnoreBefore=new Dictionary<string,long>(Data.AccountIgnoreBefore),
                RemovedFriends=Data.RemovedFriends.Select(f=>new RemovedFriendMemory{AccountId=f.AccountId,Id=f.Id,IgnoreBefore=f.IgnoreBefore}).ToList()};
        }
        public bool RemoveFriend(FriendMemory friend,long now) {
            if(loadFailed||!Owns(friend)||!ValidTicks(now))return false;
            var candidate=RemovalCandidate();
            candidate.Friends.Remove(friend);
            var marker=candidate.RemovedFriends.FirstOrDefault(f=>f.AccountId==friend.AccountId&&f.Id==friend.Id);
            if(marker==null) {
                if(candidate.RemovedFriends.Count>=5000){Error="移除记录容量已满，请清空当前账号的记忆后重试";return false;}
                marker=new RemovedFriendMemory{AccountId=friend.AccountId,Id=friend.Id};candidate.RemovedFriends.Add(marker);
            }
            marker.IgnoreBefore=Math.Max(Math.Max(marker.IgnoreBefore,friend.IgnoreBefore),now);
            if(!Persist(candidate))return false;
            Data=candidate;return true;
        }
        public bool ClearAccount(string account,long now) {
            if(loadFailed||!MemoryIdentity.Valid(account)||!ValidTicks(now))return false;
            var candidate=RemovalCandidate();long prior;
            if(!candidate.AccountIgnoreBefore.TryGetValue(account,out prior)&&candidate.AccountIgnoreBefore.Count>=500){Error="账号清理记录容量已满，请稍后再试";return false;}
            candidate.AccountIgnoreBefore[account]=Math.Max(prior,now);
            candidate.Friends.RemoveAll(f=>f.AccountId==account);
            candidate.Groups.RemoveAll(g=>g.AccountId==account);
            candidate.RemovedFriends.RemoveAll(f=>f.AccountId==account);
            if(!Persist(candidate))return false;
            Data=candidate;return true;
        }
        public void ClearAll(long now) {
            if(loadFailed)return;
            var replacement=new MemoryData{Version=Data.Version,Enabled=Data.Enabled,IgnoreBefore=now};
            if(Persist(replacement))Data=replacement;
        }
        public void Save() {
            if(!dirty||Error!=null||loadFailed)return;
            Persist(Data);
        }
        bool Persist(MemoryData candidate) {
            if(loadFailed)return false;
            try{
                string json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(candidate);
                if(Encoding.UTF8.GetByteCount(json)>15*1024*1024){Error="记忆容量已满，请清理部分记忆后重试";return false;}
                LocalStore.Write(path,json,true);dirty=false;Error=null;return true;
            }
            catch{Error="记忆更改尚未保存，已暂停使用；请清理或检查存储后重试";return false;}
        }
    }
}
