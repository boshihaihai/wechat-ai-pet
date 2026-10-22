using System;
using System.Collections.Generic;

namespace WeChatPet.Runtime {
    internal static class NativeEmojiPopupCompletionTests {
        internal static void Run(Action<string,bool> check) {
            bool popup=true,clipboardAvailable=true,consumed=false;
            var order=new List<string>();
            // A simulated target finishes its action only when its menu closes.
            // The real integration may verify only after Finish reports closure.
            order.Add("invoke-returned");
            var result=NativeEmojiPopupCompletion.Finish(true,()=>popup,()=>true,delegate {
                order.Add("close-owned-menu");consumed=clipboardAvailable;popup=false;return true;
            });
            if(result.ReadyForVerification)order.Add(consumed?"read-confirmed-draft":"read-empty-draft");
            clipboardAvailable=false;order.Add("clipboard-cleanup");
            check("Native popup closes before draft verification and clipboard cleanup",
                String.Join(",",order)=="invoke-returned,close-owned-menu,read-confirmed-draft,clipboard-cleanup");
            check("Native popup successful close reports both observed visibility states",
                result.ReadyForVerification&&result.CloseAttempted&&result.VisibleWhenInvokeReturned==true&&result.VisibleAfterClose==false&&result.Reason==null);

            int reads=0,closes=0;
            result=NativeEmojiPopupCompletion.Finish(false,()=>{reads++;return true;},()=>true,()=>{closes++;return true;});
            check("Native popup failed provider call never closes or pretends verification ready",
                !result.ReadyForVerification&&!result.CloseAttempted&&reads==0&&closes==0&&result.Reason=="native_invoke_unconfirmed");
            result=NativeEmojiPopupCompletion.Finish(true,()=>true,()=>false,()=>{closes++;return true;});
            check("Native popup changed ownership cannot be closed",!result.ReadyForVerification&&!result.CloseAttempted&&closes==0&&result.Reason=="native_menu_owner_changed_after_invoke");
            result=NativeEmojiPopupCompletion.Finish(true,()=>false,()=>{throw new Exception("Ownership read must not be needed for an absent menu");},()=>{closes++;return true;});
            check("Native popup already hidden needs no second close",result.ReadyForVerification&&!result.CloseAttempted&&closes==0&&result.VisibleWhenInvokeReturned==false&&result.VisibleAfterClose==false);

            result=NativeEmojiPopupCompletion.Finish(true,()=>true,()=>true,()=>false);
            check("Native popup close timeout cannot be reported ready",!result.ReadyForVerification&&result.CloseAttempted&&result.VisibleAfterClose==true&&result.Reason=="native_menu_close_unconfirmed");
            result=NativeEmojiPopupCompletion.Finish(true,()=>true,()=>true,()=>true);
            check("Native popup ignored close remains a specific failure",!result.ReadyForVerification&&result.CloseAttempted&&result.Reason=="native_menu_still_open_after_invoke");
            popup=true;
            result=NativeEmojiPopupCompletion.Finish(true,()=>popup,()=>true,delegate {popup=false;return false;});
            check("Native popup missing close acknowledgement is not guessed from disappearance",!result.ReadyForVerification&&result.VisibleAfterClose==false&&result.Reason=="native_menu_close_unconfirmed");
            result=NativeEmojiPopupCompletion.Finish(true,()=>{throw new InvalidOperationException();},()=>true,()=>{closes++;return true;});
            check("Native popup unreadable visibility rejects without close",!result.ReadyForVerification&&!result.CloseAttempted&&closes==0&&result.VisibleWhenInvokeReturned==null);
            result=NativeEmojiPopupCompletion.Finish(true,()=>true,()=>{throw new InvalidOperationException();},()=>{closes++;return true;});
            check("Native popup unreadable ownership rejects without close",!result.ReadyForVerification&&!result.CloseAttempted&&closes==0);
            result=NativeEmojiPopupCompletion.Finish(true,()=>true,()=>true,()=>{throw new InvalidOperationException();});
            check("Native popup close exception remains unconfirmed",!result.ReadyForVerification&&result.CloseAttempted&&result.VisibleAfterClose==null&&result.Reason=="native_menu_close_unconfirmed");
            int samples=0;
            result=NativeEmojiPopupCompletion.Finish(true,()=>{if(samples++==0)return true;throw new InvalidOperationException();},()=>true,()=>true);
            check("Native popup unreadable post-close state is not verification ready",!result.ReadyForVerification&&result.CloseAttempted&&result.VisibleAfterClose==null);
        }
#if OFFLINE_NATIVE_POPUP_TEST
        static int Main() {
            int passed=0,failed=0;
            Run((name,ok)=>{if(ok)passed++;else {failed++;Console.WriteLine("FAIL "+name);}});
            Console.WriteLine("Native popup completion: "+passed+" passed, "+failed+" failed. Pure simulated state only.");
            return failed==0?0:1;
        }
#endif
    }
#if OFFLINE_NATIVE_POPUP_TEST
    // Link-only stubs. The test exercises no native menu, input, UIA, or clipboard API.
    internal sealed class ChatSnapshot {
        internal bool Available,GroupChat;
        internal long Window;
        internal string Session,InputIdentity,AccountId,FriendId,GroupId,Tail,ContentRevision;
    }
    internal static class Win {
        internal static uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid) {throw new InvalidOperationException("Unexpected native call");}
        internal static IntPtr GetForegroundWindow() {throw new InvalidOperationException("Unexpected native call");}
    }
#endif
}
