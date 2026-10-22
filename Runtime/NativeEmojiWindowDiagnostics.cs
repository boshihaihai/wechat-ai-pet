using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WeChatPet.Runtime {
    // Structural, read-only diagnostics. No window title, input/chat text, clipboard, UIA,
    // process-memory access, menu opening, focus changes or window messages are used.
    public static class NativeEmojiWindowDiagnostics {
        const int MaximumWindows=256;
        const long MaximumMilliseconds=500;
        delegate bool EnumWindowCallback(IntPtr window,IntPtr state);

        public static object Inspect(long target) {
            var clock=Stopwatch.StartNew();
            var result=new Dictionary<string,object> {
                {"capturedAt",DateTime.UtcNow.ToString("o")},{"readOnly",true},{"readsWindowTitles",false},
                {"readsChatOrInputText",false},{"readsClipboard",false},{"usesUiAutomation",false},
                {"changesInput",false},{"target",target},{"scope","same_thread_visible_windows_and_descendants"}
            };
            try {
                IntPtr hwnd=new IntPtr(target);uint pid;
                uint thread=GetWindowThreadProcessId(hwnd,out pid);
                bool valid=target>0&&IsWindow(hwnd)&&pid!=0&&thread!=0;
                IntPtr foreground=GetForegroundWindow();
                result["targetValid"]=valid;result["targetPid"]=pid;result["targetTid"]=thread;
                result["foreground"]=foreground.ToInt64();
                if(!valid) {result["status"]="invalid_target";return result;}
                result["targetWindow"]=Describe(hwnd,hwnd,pid,thread,foreground);
                result["guiBefore"]=Gui(thread);
                var rows=new List<object>();var seen=new HashSet<long>();bool truncated=false;
                Func<IntPtr,bool> append=window=> {
                    if(clock.ElapsedMilliseconds>MaximumMilliseconds||rows.Count>=MaximumWindows) {truncated=true;return false;}
                    uint windowPid;uint windowThread=GetWindowThreadProcessId(window,out windowPid);
                    if(windowThread==thread&&windowPid==pid&&IsWindowVisible(window)&&seen.Add(window.ToInt64()))
                        rows.Add(Describe(window,hwnd,pid,thread,foreground));
                    return true;
                };
                bool enumerationReturned=EnumThreadWindows(thread,delegate(IntPtr window,IntPtr state) {
                    if(!append(window))return false;
                    EnumChildWindows(window,delegate(IntPtr child,IntPtr unused) {return append(child);},IntPtr.Zero);
                    return !truncated;
                },IntPtr.Zero);
                result["windows"]=rows;result["windowCount"]=rows.Count;result["truncated"]=truncated;
                result["enumerationReturned"]=enumerationReturned;
                result["guiAfter"]=Gui(thread);
                IntPtr foregroundAfter=GetForegroundWindow();uint pidAfter;
                uint threadAfter=GetWindowThreadProcessId(hwnd,out pidAfter);
                result["foregroundAfter"]=foregroundAfter.ToInt64();
                result["foregroundStable"]=foreground==foregroundAfter;
                result["targetBindingStable"]=IsWindow(hwnd)&&pidAfter==pid&&threadAfter==thread;
                result["status"]=truncated?"partial":"captured";
                result["isPasteAuthorization"]=false;
            } catch(Exception error) {
                // Exception messages are deliberately omitted: only structural diagnostics belong here.
                result["status"]="error";result["errorType"]=error.GetType().Name;
                result["errorHresult"]="0x"+unchecked((uint)error.HResult).ToString("X8");
            } finally {clock.Stop();result["elapsedMs"]=clock.ElapsedMilliseconds;}
            return result;
        }

        static object Describe(IntPtr window,IntPtr target,uint expectedPid,uint expectedThread,IntPtr foreground) {
            uint pid;uint thread=GetWindowThreadProcessId(window,out pid);
            IntPtr owner=GetWindow(window,4),root=GetAncestor(window,2),rootOwner=GetAncestor(window,3);
            bool visible=IsWindowVisible(window);
            // Exact current NativeEmojiPasteMenu.Owned predicate, not a newly relaxed policy.
            bool owned=window!=IntPtr.Zero&&window!=target&&visible&&thread==expectedThread&&pid==expectedPid
                &&(owner==target||rootOwner==target);
            var className=new StringBuilder(256);int classLength=GetClassName(window,className,className.Capacity);
            return new Dictionary<string,object> {
                {"handle",window.ToInt64()},{"className",classLength>0?className.ToString():null},
                {"classNameAvailable",classLength>0},{"owner",owner.ToInt64()},{"parent",GetParent(window).ToInt64()},
                {"root",root.ToInt64()},{"rootOwner",rootOwner.ToInt64()},{"foregroundMatch",window==foreground},
                {"tid",thread},{"pid",pid},{"isTarget",window==target},{"visible",visible},
                {"isTopLevel",root==window},{"passesCurrentOwned",owned}
            };
        }

        static object Gui(uint thread) {
            var info=new GuiInfo{Size=(uint)Marshal.SizeOf(typeof(GuiInfo))};
            bool ok=GetGUIThreadInfo(thread,ref info);int error=ok?0:Marshal.GetLastWin32Error();
            long layout=GetKeyboardLayout(thread).ToInt64();
            return new Dictionary<string,object> {
                {"success",ok},{"lastError",error},{"thread",thread},{"active",info.Active.ToInt64()},
                {"focus",info.Focus.ToInt64()},{"capture",info.Capture.ToInt64()},{"menuOwner",info.MenuOwner.ToInt64()},
                {"moveSize",info.MoveSize.ToInt64()},{"caret",info.Caret.ToInt64()},{"flags",info.Flags},
                {"layout",layout},{"layoutHex","0x"+unchecked((ulong)layout).ToString("x")}
            };
        }
        [StructLayout(LayoutKind.Sequential)] struct Rect {internal int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)] struct GuiInfo {
            internal uint Size,Flags;internal IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;internal Rect CaretRect;
        }
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread,EnumWindowCallback callback,IntPtr state);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumWindowCallback callback,IntPtr state);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int maximum);
        [DllImport("user32.dll",SetLastError=true)] static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    }
}
