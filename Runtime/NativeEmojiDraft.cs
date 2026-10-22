using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;

namespace WeChatPet.Runtime {
    // The native clipboard parser replaces verified WeChat names with embedded
    // objects. Preview images never enter the draft, clipboard or verification.
    internal sealed class NativeEmojiPlan {
        internal string Text, Expected;
        internal int Count;
        static readonly Regex token = new Regex(@"\[[^\[\]\r\n]{1,16}\]", RegexOptions.Compiled);
        static readonly HashSet<string> names = LoadNames();
        static HashSet<string> LoadNames() {
            try {
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","wechat-pet","emoji","native-names.json");
                if(!File.Exists(path)||new FileInfo(path).Length>32768)return new HashSet<string>();
                var values=new JavaScriptSerializer().Deserialize<string[]>(File.ReadAllText(path));
                if(values==null||values.Length>256||values.Any(n=>n==null||!Regex.IsMatch(n,@"^\[[^\[\]\r\n]{1,16}\]$")))return new HashSet<string>();
                return new HashSet<string>(values,StringComparer.Ordinal);
            }catch{return new HashSet<string>();}
        }
        internal static NativeEmojiPlan Create(string text) { return Create(text,names); }
        internal static NativeEmojiPlan Create(string text,ISet<string> known) {
            if(String.IsNullOrEmpty(text)||text.Contains("\uFFFC")||LiteralContext(text,known))return null;
            int count=0;
            string expected=token.Replace(text,m=>{if(!known.Contains(m.Value))return m.Value;count++;return "\uFFFC";});
            return count==0?null:new NativeEmojiPlan{Text=text,Expected=expected,Count=count};
        }
        static bool LiteralContext(string text,ISet<string> known) {
            // Native paste parses the whole string. If any part needs literal
            // spelling, keep the entire draft on the plain ValuePattern path.
            if(text.TrimStart().StartsWith("{")||
               Regex.IsMatch(text,@"\b[a-z][a-z0-9+.-]*://|\bwww\.|^[ \t]*>",RegexOptions.IgnoreCase|RegexOptions.Multiline))return true;
            if(text.TrimStart().StartsWith("[")) {
                try {var data=new JavaScriptSerializer().DeserializeObject(text);if(data is Array||data is Dictionary<string,object>)return true;}
                catch(ArgumentException) { }
                catch(InvalidOperationException) { }
            }
            foreach(Match m in token.Matches(text)) {
                if(!known.Contains(m.Value))continue;
                // Discussing an emoji or quoting other words is ordinary chat.
                // Preserve spelling only when this token is inside a literal.
                foreach(Match literal in Regex.Matches(text,@"(?s)(?<ticks>`+).*?\k<ticks>|""[^""\r\n]*""|“[^”]*”|‘[^’]*’|「[^」]*」|『[^』]*』|(?<![A-Za-z0-9])'[^'\r\n]*'(?![A-Za-z0-9])"))
                    if(m.Index>=literal.Index&&m.Index+m.Length<=literal.Index+literal.Length)return true;
                string before=text.Substring(Math.Max(0,m.Index-24),Math.Min(m.Index,24));
                string after=text.Substring(m.Index+m.Length,Math.Min(24,text.Length-m.Index-m.Length));
                if(Regex.IsMatch(after,@"^\s*(?:的)?(?:意思|含义|表示|代表|叫作|叫做|是指|(?:是)?什么(?:意思|含义)|是\s*(?:一个|一种|表情|符号|字符|名称|emoji))",RegexOptions.IgnoreCase)||
                   Regex.IsMatch(before,@"(?:意思|含义|名称|名字|表情|符号|字符|emoji)(?:是|为|叫|叫作|叫做|写作|：|:)\s*$|(?:表示|代表)(?:的)?(?:是)?\s*$",RegexOptions.IgnoreCase)||
                   Regex.IsMatch(before,@"(?:复制|输入|粘贴|键入|填写)(?:以下|下面|这段|这些)?的?(?:文字|文本|字符|名称|表情名称)?\s*[:：]?\s*$|(?:原样|逐字)\s*[:：]?\s*$")||
                   Regex.IsMatch(after,@"^\s*(?:原样|逐字)(?:复制|输入|粘贴|保留)"))return true;
            }
            return false;
        }
        internal bool Matches(string value,string document) {
            // Only line-ending normalization is permitted; no trimming, dropping
            // content, counting objects alone, or accepting a literal fallback.
            return Normalize(value)==Normalize(Expected)&&Normalize(document)==Normalize(Expected);
        }
        internal static bool SameDraftContext(ChatSnapshot before,ChatSnapshot after) {
            return before!=null&&after!=null&&before.Available&&after.Available&&!String.IsNullOrEmpty(before.Session)&&!String.IsNullOrEmpty(before.InputIdentity)&&after.Session==before.Session&&after.Window==before.Window&&after.AccountId==before.AccountId&&after.FriendId==before.FriendId&&after.GroupId==before.GroupId&&after.GroupChat==before.GroupChat&&after.InputIdentity==before.InputIdentity&&after.Tail==before.Tail&&after.ContentRevision==before.ContentRevision;
        }
        static string Normalize(string value) {return value==null?null:value.Replace("\r\n","\n");}
    }

    // UIA Invoke acknowledges the provider action, not that the popup has closed
    // or the clipboard has been consumed. Finish that menu lifetime before the
    // caller reads the draft or decides whether to restore/isolate the clipboard.
    internal sealed class NativeEmojiPopupCompletion {
        internal bool ReadyForVerification,CloseAttempted;
        internal bool? VisibleWhenInvokeReturned,VisibleAfterClose;
        internal string Reason;
        internal static NativeEmojiPopupCompletion Finish(bool providerReturned,Func<bool> visible,Func<bool> owned,Func<bool> close) {
            var result=new NativeEmojiPopupCompletion {Reason="native_invoke_unconfirmed"};
            if(!providerReturned)return result;
            result.Reason="native_menu_close_unconfirmed";
            try {
                result.VisibleWhenInvokeReturned=visible();
                if(!result.VisibleWhenInvokeReturned.Value) {
                    result.VisibleAfterClose=false;result.ReadyForVerification=true;result.Reason=null;return result;
                }
                if(!owned()) {result.Reason="native_menu_owner_changed_after_invoke";return result;}
                result.CloseAttempted=true;
                bool closeReturned=close();
                result.VisibleAfterClose=visible();
                if(!closeReturned)return result;
                if(result.VisibleAfterClose.Value) {result.Reason="native_menu_still_open_after_invoke";return result;}
                result.ReadyForVerification=true;result.Reason=null;
            } catch {result.Reason="native_menu_close_unconfirmed";}
            return result;
        }
    }

    internal sealed class NativeEmojiPasteMenu : IDisposable {
        delegate bool EnumWindowsCallback(IntPtr hwnd,IntPtr state);
        [DllImport("user32.dll")]static extern bool EnumThreadWindows(uint thread,EnumWindowsCallback callback,IntPtr state);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr hwnd,uint command);
        [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
        [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam,uint flags,uint timeout,out IntPtr result);
        readonly IntPtr target;
        readonly int pid;
        readonly uint thread;
        IntPtr popup;
        AutomationElement item;
        int[] itemId;
        internal string Reason {get;private set;}
        internal long Popup {get{return popup.ToInt64();}}
        internal bool? PopupVisibleWhenInvokeReturned {get;private set;}
        internal bool? PopupVisibleAfterClose {get;private set;}
        internal bool PopupCloseAttempted {get;private set;}
        internal string PopupCloseReason {get;private set;}
        internal NativeEmojiPasteMenu(IntPtr window) {target=window;uint p;thread=Win.GetWindowThreadProcessId(window,out p);pid=(int)p;}
        bool Owned(IntPtr hwnd) {
            uint p;return hwnd!=IntPtr.Zero&&hwnd!=target&&IsWindowVisible(hwnd)&&Win.GetWindowThreadProcessId(hwnd,out p)==thread&&p==pid&&(GetWindow(hwnd,4)==target||GetAncestor(hwnd,3)==target);
        }
        List<IntPtr> Windows() {
            var list=new List<IntPtr>();EnumThreadWindows(thread,delegate(IntPtr hwnd,IntPtr state){if(Owned(hwnd)&&list.Count<32)list.Add(hwnd);return true;},IntPtr.Zero);return list;
        }
        internal static bool PasteName(string value) {
            if(value==null)return false;string name=value.Split('\t')[0].Trim();return name=="粘贴"||name=="Paste"||name=="粘贴(&P)"||name=="&Paste";
        }
        internal bool Open(AutomationElement field,Func<bool> stillValid) {
            Reason="native_menu_unavailable";
            var previous=new HashSet<IntPtr>(Windows());
            if(previous.Count!=0||!stillValid()||!field.Current.HasKeyboardFocus)return false;
            Rect box=field.Current.BoundingRectangle;
            if(box.IsEmpty||box.Width<20||box.Height<20)return false;
            IntPtr ignored;
            // Qt Widgets routes a keyboard-reason context event to the focused
            // input. A coordinate (mouse-reason) WM_CONTEXTMENU can be ignored.
            // -1 requests that menu only: no key injection, paste, or send.
            SendMessageTimeout(target,0x007B,target,new IntPtr(-1),0x0002,500,out ignored);
            var clock=Stopwatch.StartNew();
            while(clock.ElapsedMilliseconds<900) {
                var candidates=Windows().Where(h=>!previous.Contains(h)).ToArray();
                if(candidates.Length>1)return false;
                if(candidates.Length==1) {
                    popup=candidates[0];
                    if(!stillValid()){Reason="native_context_changed";return false;}
                    var root=AutomationElement.FromHandle(popup);
                    if(root.Current.ProcessId!=pid)return false;
                    var matches=new List<AutomationElement>();
                    var queue=new Queue<AutomationElement>();queue.Enqueue(root);int count=0;
                    while(queue.Count>0&&count++<80) {
                        var e=queue.Dequeue();var c=e.Current;
                        if(c.ProcessId==pid&&c.ControlType==ControlType.MenuItem&&c.IsEnabled&&!c.IsOffscreen&&PasteName(c.Name))matches.Add(e);
                        for(var child=TreeWalker.RawViewWalker.GetFirstChild(e);child!=null;child=TreeWalker.RawViewWalker.GetNextSibling(child))queue.Enqueue(child);
                    }
                    if(queue.Count!=0||matches.Count>1)return false;
                    object pattern;
                    if(matches.Count==1&&matches[0].TryGetCurrentPattern(InvokePattern.Pattern,out pattern)) {
                        item=matches[0];itemId=item.GetRuntimeId();Reason=null;return true;
                    }
                }
                else if(!stillValid()){Reason="native_context_changed";return false;}
                Thread.Sleep(30);
            }
            return false;
        }
        internal bool Invoke(Func<bool> stillValid,Func<bool> markStarted) {
            Reason="native_menu_changed";
            if(item==null||!Owned(popup)||!stillValid())return false;
            var c=item.Current;object pattern;
            if(c.ProcessId!=pid||c.ControlType!=ControlType.MenuItem||!c.IsEnabled||c.IsOffscreen||!PasteName(c.Name)||!item.GetRuntimeId().SequenceEqual(itemId)||!item.TryGetCurrentPattern(InvokePattern.Pattern,out pattern))return false;
            if(!stillValid()){Reason="native_context_changed";return false;}
            // Only the provider call runs separately. Reader.Field remains on
            // the observation worker; cleanup must run even if UIA never returns.
            var invocation=Task.Factory.StartNew(delegate {
                if(!markStarted())return false;
                try {((InvokePattern)pattern).Invoke();return true;}catch{return false;}
            });
            if(!invocation.Wait(1000)){Reason="native_invoke_timeout";return false;}
            // Qt's accessibility pressAction triggers QAction but does not hide
            // its menu. Do not wait until Dispose (after clipboard cleanup) to
            // close this exact owned popup. This still does not confirm paste.
            var completion=NativeEmojiPopupCompletion.Finish(invocation.Result,
                () => IsWindowVisible(popup),() => Owned(popup),delegate {
                    // Recheck ownership immediately before the sole close command.
                    if(!Owned(popup))return false;
                    IntPtr ignored;
                    return SendMessageTimeout(popup,0x0010,IntPtr.Zero,IntPtr.Zero,0x0002,200,out ignored)!=IntPtr.Zero;
                });
            PopupVisibleWhenInvokeReturned=completion.VisibleWhenInvokeReturned;
            PopupVisibleAfterClose=completion.VisibleAfterClose;PopupCloseAttempted=completion.CloseAttempted;
            PopupCloseReason=completion.Reason;Reason=completion.Reason;
            return completion.ReadyForVerification;
        }
        internal bool ForegroundBelongs() {
            IntPtr foreground=Win.GetForegroundWindow();return foreground==target||(foreground==popup&&Owned(popup));
        }
        public void Dispose() {
            // Only the newly observed, owned popup may be dismissed. Never close
            // the chat window or issue Escape/Enter to whichever app has focus.
            if(popup!=IntPtr.Zero&&Owned(popup)) {IntPtr ignored;SendMessageTimeout(popup,0x0010,IntPtr.Zero,IntPtr.Zero,0x0002,200,out ignored);}
            item=null;
        }
    }
}
