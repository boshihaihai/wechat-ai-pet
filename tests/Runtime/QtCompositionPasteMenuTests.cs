using System;
using System.Reflection;

namespace WeChatPet.Runtime {
    internal static class QtCompositionPasteMenuTests {
        internal static void Run(Action<string,bool> check) {
            Accept(check,"target foreground with target child focus",s=>{});
            Accept(check,"exact popup foreground and focus",s=>{s.Foreground=s.Popup;s.Focus=s.Popup;s.FocusRoot=s.Popup;s.Active=s.Popup;});
            Accept(check,"exact popup mouse capture",s=>{s.Capture=s.Popup;});
            Accept(check,"root-owner binding instead of direct owner",s=>{s.PopupOwner=333;s.PopupRootOwner=s.Target;});
            Accept(check,"direct-owner binding instead of root owner",s=>{s.PopupOwner=s.Target;s.PopupRootOwner=333;});
            foreach(uint flags in new uint[]{0,1,4,5,16,17,20,21}) {
                uint value=flags;Accept(check,"allowed GUI flags "+value,s=>{s.GuiFlags=value;});
            }
            Reject(check,"missing target",s=>s.Target=0);
            Reject(check,"invalid target",s=>s.TargetValid=false);
            Reject(check,"hidden target",s=>s.TargetVisible=false);
            Reject(check,"minimized target",s=>s.TargetIconic=true);
            Reject(check,"target is not its root",s=>s.TargetRoot=333);
            Reject(check,"missing target process",s=>s.TargetPid=0);
            Reject(check,"invalid target process",s=>s.TargetPid=UInt32.MaxValue);
            Reject(check,"missing target thread",s=>s.TargetTid=0);
            Reject(check,"missing popup",s=>s.Popup=0);
            Reject(check,"popup equals target",s=>s.Popup=s.Target);
            Reject(check,"popup is child instead of top-level",s=>s.PopupRoot=s.Target);
            Reject(check,"invalid popup",s=>s.PopupValid=false);
            Reject(check,"hidden popup",s=>s.PopupVisible=false);
            Reject(check,"minimized popup",s=>s.PopupIconic=true);
            Reject(check,"popup belongs to another process",s=>s.PopupPid++);
            Reject(check,"popup belongs to another thread",s=>s.PopupTid++);
            Reject(check,"popup ownership is unrelated",s=>{s.PopupOwner=333;s.PopupRootOwner=333;});
            Reject(check,"unrelated foreground",s=>s.Foreground=333);
            Reject(check,"foreground process changed",s=>s.ForegroundPid++);
            Reject(check,"foreground thread changed",s=>s.ForegroundTid++);
            Reject(check,"GUI information unavailable",s=>s.GuiOk=false);
            Reject(check,"no focus",s=>s.Focus=0);
            Reject(check,"unrelated focus root",s=>s.FocusRoot=333);
            Reject(check,"child of popup cannot replace exact popup focus",s=>s.FocusRoot=s.Popup);
            Reject(check,"focus process changed",s=>s.FocusPid++);
            Reject(check,"focus thread changed",s=>s.FocusTid++);
            Reject(check,"unrelated active window",s=>s.Active=333);
            Reject(check,"missing keyboard layout",s=>s.Layout=0);
            Reject(check,"move-size GUI flag",s=>s.GuiFlags|=2);
            Reject(check,"system-menu GUI flag",s=>s.GuiFlags|=8);
            Reject(check,"unknown GUI flag",s=>s.GuiFlags|=32);
            Reject(check,"unknown mouse capture",s=>s.Capture=333);
            Reject(check,"target mouse capture is not the observed menu",s=>s.Capture=s.Target);
            Reject(check,"move-size window",s=>s.MoveSize=s.Target);
            Reject(check,"unrelated menu owner",s=>s.MenuOwner=333);
            Reject(check,"menu mode without menu owner",s=>s.MenuOwner=0);
            Accept(check,"custom Qt menu without Win32 menu-mode flags",s=>{s.GuiFlags=0;s.MenuOwner=0;});
            string reason;check("Paste menu policy rejects null snapshot",!QtCompositionGate.ValidatePasteMenuWindow(null,out reason));

            var valid=Valid();
            check("Paste menu context accepts target window",QtCompositionGate.PasteMenuContextBound(valid,100,100,10,20,0));
            check("Paste menu context accepts target child",QtCompositionGate.PasteMenuContextBound(valid,101,100,10,20,0));
            check("Paste menu context accepts measured inactive cleared HWND",QtCompositionGate.PasteMenuContextBound(valid,0,0,0,0,0));
            check("Paste menu context rejects active cleared HWND",!QtCompositionGate.PasteMenuContextBound(valid,0,0,0,0,1));
            check("Paste menu context rejects missing inactive evidence",!QtCompositionGate.PasteMenuContextBound(valid,0,0,0,0,null));
            check("Paste menu context rejects unknown inactive flag",!QtCompositionGate.PasteMenuContextBound(valid,0,0,0,0,2));
            check("Paste menu context rejects inconsistent cleared HWND root",!QtCompositionGate.PasteMenuContextBound(valid,0,100,0,0,0));
            check("Paste menu context rejects inconsistent cleared HWND process",!QtCompositionGate.PasteMenuContextBound(valid,0,0,10,0,0));
            check("Paste menu context rejects inconsistent cleared HWND thread",!QtCompositionGate.PasteMenuContextBound(valid,0,0,0,20,0));
            check("Paste menu context rejects missing target HWND evidence",!QtCompositionGate.PasteMenuContextBound(valid,100,100,10,20,null));
            check("Paste menu context rejects unknown target HWND flag",!QtCompositionGate.PasteMenuContextBound(valid,100,100,10,20,255));
            check("Paste menu context can bind active target for reporting",QtCompositionGate.PasteMenuContextBound(valid,100,100,10,20,1));
            check("Paste menu context rejects negative context",!QtCompositionGate.PasteMenuContextBound(valid,-1,100,10,20,0));
            check("Paste menu context rejects popup context",!QtCompositionGate.PasteMenuContextBound(valid,200,200,10,20,0));
            check("Paste menu context rejects unrelated root",!QtCompositionGate.PasteMenuContextBound(valid,101,333,10,20,0));
            check("Paste menu context rejects another process",!QtCompositionGate.PasteMenuContextBound(valid,101,100,11,20,0));
            check("Paste menu context rejects another thread",!QtCompositionGate.PasteMenuContextBound(valid,101,100,10,21,0));
            check("Paste menu context rejects null snapshot",!QtCompositionGate.PasteMenuContextBound(null,101,100,10,20,0));
            check("Paste menu inactive context rejects null snapshot",!QtCompositionGate.PasteMenuContextBound(null,0,0,0,0,0));
            var chainType=typeof(QtCompositionGate).GetNestedType("Chain",BindingFlags.NonPublic);
            var chainSame=chainType.GetMethod("Same",BindingFlags.Public|BindingFlags.Instance);
            var firstChain=Activator.CreateInstance(chainType,true);var secondChain=Activator.CreateInstance(chainType,true);
            check("Composition chain permits stable inactive zero HWND",(bool)chainSame.Invoke(firstChain,new object[]{secondChain}));
            chainType.GetField("Window").SetValue(secondChain,100L);
            check("Composition chain rejects zero to target HWND change",!(bool)chainSame.Invoke(firstChain,new object[]{secondChain}));
            check("Composition chain rejects target to zero HWND change",!(bool)chainSame.Invoke(secondChain,new object[]{firstChain}));
            chainType.GetField("Window").SetValue(secondChain,0L);chainType.GetField("Flag").SetValue(secondChain,(byte)1);
            check("Composition chain rejects flag change while HWND zero",!(bool)chainSame.Invoke(firstChain,new object[]{secondChain}));
            check("Paste menu stability accepts identical evidence",valid.Same(Valid()));
            check("Paste menu stability rejects absent sample",!valid.Same(null));
            // Any captured binding changing between samples must invalidate, including flags and layout.
            foreach(var field in typeof(QtCompositionGate.PasteMenuWindowState).GetFields(BindingFlags.Instance|BindingFlags.NonPublic)) {
                var changed=Valid();object value=field.GetValue(changed);
                if(field.FieldType==typeof(long))field.SetValue(changed,(long)value+1);
                else if(field.FieldType==typeof(uint))field.SetValue(changed,(uint)value+1);
                else if(field.FieldType==typeof(bool))field.SetValue(changed,!(bool)value);
                else throw new InvalidOperationException("unhandled binding field "+field.Name);
                check("Paste menu stability checks "+field.Name,!valid.Same(changed));
            }
        }
        static void Accept(Action<string,bool> check,string name,Action<QtCompositionGate.PasteMenuWindowState> mutate) {Test(check,name,mutate,true);}
        static void Reject(Action<string,bool> check,string name,Action<QtCompositionGate.PasteMenuWindowState> mutate) {Test(check,name,mutate,false);}
        static void Test(Action<string,bool> check,string name,Action<QtCompositionGate.PasteMenuWindowState> mutate,bool expected) {
            var state=Valid();mutate(state);string reason;
            check("Paste menu: "+name,QtCompositionGate.ValidatePasteMenuWindow(state,out reason)==expected);
        }
        static QtCompositionGate.PasteMenuWindowState Valid() {
            return new QtCompositionGate.PasteMenuWindowState {
                Target=100,TargetRoot=100,Popup=200,PopupRoot=200,PopupOwner=100,PopupRootOwner=100,
                Foreground=100,Focus=101,FocusRoot=100,Active=100,Layout=0x08040804,MenuOwner=100,GuiFlags=20,
                TargetPid=10,TargetTid=20,PopupPid=10,PopupTid=20,ForegroundPid=10,ForegroundTid=20,FocusPid=10,FocusTid=20,
                TargetValid=true,TargetVisible=true,PopupValid=true,PopupVisible=true,GuiOk=true
            };
        }
#if OFFLINE_QT_MENU_TEST
        static int Main() {
            int passed=0,failed=0;
            Run((name,ok)=>{if(ok)passed++;else {failed++;Console.WriteLine("FAIL "+name);}});
            Console.WriteLine("Qt paste-menu pure policy checks: "+passed+" passed, "+failed+" failed. No UI, process, or clipboard access.");
            return failed==0?0:1;
        }
#endif
    }
#if OFFLINE_QT_MENU_TEST
    // Linking sentinel: these offline tests must never reach native module/disk verification.
    public static class BuildIdentity { public static void VerifyFile(string path) {throw new InvalidOperationException("Offline test attempted build verification");} }
#endif
}
