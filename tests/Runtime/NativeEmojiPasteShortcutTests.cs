using System;
using System.Collections.Generic;
using System.Linq;

namespace WeChatPet.Runtime {
    internal static class NativeEmojiPasteShortcutTests {
        sealed class Fake : NativeEmojiPasteShortcut.IPlatform {
            internal long Time;
            ulong next=1000;
            internal NativeEmojiPasteShortcut.Engine Engine;
            internal readonly List<NativeEmojiPasteShortcut.Stroke[]> Batches=new List<NativeEmojiPasteShortcut.Stroke[]>();
            internal readonly List<NativeEmojiPasteShortcut.HookDisposition> Decisions=new List<NativeEmojiPasteShortcut.HookDisposition>();
            internal Func<NativeEmojiPasteShortcut.Stroke[],uint> Sender;
            internal Action<int,NativeEmojiPasteShortcut.Stroke> Before;
            internal Action OnNewMarker;
            internal bool GenericControl;
            internal Fake() {Engine=new NativeEmojiPasteShortcut.Engine(this);}
            public long Milliseconds {get{return Time;}}
            public ulong NewMarker() {if(OnNewMarker!=null)OnNewMarker();return ++next;}
            public void WaitOneMillisecond() {Time++;}
            public uint Send(NativeEmojiPasteShortcut.Stroke[] strokes) {
                Batches.Add(strokes);if(Sender!=null)return Sender(strokes);
                for(int i=0;i<strokes.Length;i++) {if(Before!=null)Before(i,strokes[i]);Deliver(strokes[i]);}
                return (uint)strokes.Length;
            }
            internal NativeEmojiPasteShortcut.HookDisposition Deliver(NativeEmojiPasteShortcut.Stroke stroke) {
                uint key=GenericControl&&stroke.Key==0xA2?0x11u:stroke.Key;
                var result=Engine.FilterHook(key,stroke.Down,stroke.Down?0x10u:0x90u,stroke.Marker);Decisions.Add(result);return result;
            }
        }
        internal static void Run(Action<string,bool> check) {
            var f=new Fake();var lease=f.Engine.Prepare(()=>true,500);
            check("Shortcut normal batch injected and hook-confirmed",lease.Prepared&&lease.Invoke());
            check("Shortcut normal batch has exactly four ordered input events",f.Batches.Count==1&&String.Join(",",f.Batches[0].Select(s=>s.Key+":"+s.Down))=="162:True,86:True,86:False,162:False");
            check("Shortcut all markers are nonzero and distinct",lease.Strokes.All(s=>s.Marker!=0)&&lease.Strokes.Select(s=>s.Marker).Distinct().Count()==6);
            check("Shortcut normal diagnostics describe injection only",lease.InvocationStarted&&lease.MainInserted==4&&lease.MainObserved==4&&lease.CtrlDownPassed&&lease.VDownPassed&&lease.KeysReleased&&lease.RecoveryInserted==0&&lease.Reason=="shortcut_injected");
            lease.Dispose();
            check("Shortcut replayed owned events stay blocked after disposal",lease.Strokes.Take(4).All(s=>f.Deliver(s)==NativeEmojiPasteShortcut.HookDisposition.BlockOwned));

            f=new Fake();int marked=0;lease=f.Engine.Prepare(()=>false,500);
            check("Shortcut final guard rejects before marking external invocation",!lease.Invoke(()=>{marked++;return true;})&&marked==0&&f.Batches.Count==0&&!lease.InvocationStarted);
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);
            check("Shortcut external invocation veto emits no keys",!lease.Invoke(()=>false)&&f.Batches.Count==0&&!lease.InvocationStarted&&lease.Reason=="shortcut_preinvoke_rejected");
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);
            check("Shortcut external invocation exception emits no keys",!lease.Invoke(()=>{throw new InvalidOperationException();})&&f.Batches.Count==0&&!lease.InvocationStarted&&lease.Reason=="shortcut_preinvoke_exception");
            f=new Fake();marked=0;lease=f.Engine.Prepare(()=>true,500);
            bool firstInvoke=lease.Invoke(()=>{marked++;return true;});
            check("Shortcut repeated invocation never republishes the external marker",firstInvoke&&!lease.Invoke(()=>{marked++;return true;})&&marked==1&&f.Batches.Count==1);

            f=new Fake{GenericControl=true};lease=f.Engine.Prepare(()=>true,500);
            check("Shortcut own generic VK_CONTROL hook representation is supported",lease.Invoke()&&lease.KeysReleased);

            f=new Fake();bool eligible=true;lease=f.Engine.Prepare(()=>eligible,500);
            f.Before=(i,s)=>{if(i==1)eligible=false;};
            check("Shortcut foreground change between Ctrl and V blocks V",!lease.Invoke()&&lease.CtrlDownPassed&&!lease.VDownPassed&&lease.KeysReleased);
            check("Shortcut cancellation permits only owned Ctrl keyup",f.Decisions.SequenceEqual(new[]{NativeEmojiPasteShortcut.HookDisposition.AllowOwned,NativeEmojiPasteShortcut.HookDisposition.BlockOwned,NativeEmojiPasteShortcut.HookDisposition.BlockOwned,NativeEmojiPasteShortcut.HookDisposition.AllowOwned}));
            check("Shortcut cancelled full batch does not retry paste",f.Batches.Count==1);

            f=new Fake();eligible=true;lease=f.Engine.Prepare(()=>eligible,500);
            var physical=NativeEmojiPasteShortcut.HookDisposition.BlockOwned;
            f.Before=(i,s)=>{if(i==1){physical=f.Engine.FilterHook(0x41,true,0,lease.Strokes[1].Marker);eligible=false;}};
            check("Shortcut real user input is never swallowed or exempted",!lease.Invoke()&&physical==NativeEmojiPasteShortcut.HookDisposition.Unrelated&&!lease.VDownPassed&&lease.KeysReleased);

            f=new Fake();lease=f.Engine.Prepare(()=>true,500);
            check("Shortcut foreign injected event has no exemption",f.Engine.FilterHook(0x56,true,0x10,999999)==NativeEmojiPasteShortcut.HookDisposition.Unrelated);
            check("Shortcut physical Ctrl with matching marker remains unrelated",f.Engine.FilterHook(0xA2,true,0,lease.Strokes[0].Marker)==NativeEmojiPasteShortcut.HookDisposition.Unrelated);
            check("Shortcut forged marker on another key gets ordinary processing",f.Engine.FilterHook(0x41,true,0x10,lease.Strokes[0].Marker)==NativeEmojiPasteShortcut.HookDisposition.Unrelated);
            check("Shortcut malformed marker use cancels later injection",!lease.Invoke()&&f.Batches.Count==0);

            f=new Fake();lease=f.Engine.Prepare(()=>true,500);
            check("Shortcut unexpected lower-integrity own keydown stays owned and blocked",f.Engine.FilterHook(0xA2,true,0x12,lease.Strokes[0].Marker)==NativeEmojiPasteShortcut.HookDisposition.BlockOwned&&!lease.Invoke());
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);Fake altChanged=f;
            f.Sender=strokes=>{
                altChanged.Deliver(strokes[0]);
                altChanged.Decisions.Add(altChanged.Engine.FilterHook(0x56,true,0x30,strokes[1].Marker));
                altChanged.Deliver(strokes[2]);
                altChanged.Decisions.Add(altChanged.Engine.FilterHook(0xA2,false,0xB0,strokes[3].Marker));return 4;
            };
            check("Shortcut AltDown on owned V blocks paste and keeps Ctrl release owned",!lease.Invoke()&&lease.CtrlDownPassed&&!lease.VDownPassed&&lease.KeysReleased&&f.Decisions.SequenceEqual(new[]{NativeEmojiPasteShortcut.HookDisposition.AllowOwned,NativeEmojiPasteShortcut.HookDisposition.BlockOwned,NativeEmojiPasteShortcut.HookDisposition.BlockOwned,NativeEmojiPasteShortcut.HookDisposition.AllowOwned}));
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);Fake altFirst=f;
            f.Sender=strokes=>{
                altFirst.Decisions.Add(altFirst.Engine.FilterHook(0xA2,true,0x30,strokes[0].Marker));
                foreach(var s in strokes.Skip(1))altFirst.Deliver(s);return 4;
            };
            check("Shortcut AltDown on first own Ctrl vetoes the whole batch",!lease.Invoke()&&!lease.CtrlDownPassed&&!lease.VDownPassed&&lease.KeysReleased&&f.Decisions.All(d=>d==NativeEmojiPasteShortcut.HookDisposition.BlockOwned));
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);
            check("Shortcut mismatched key direction gets no exemption",f.Engine.FilterHook(0xA2,false,0x90,lease.Strokes[0].Marker)==NativeEmojiPasteShortcut.HookDisposition.Unrelated&&!lease.Invoke());

            f=new Fake();lease=f.Engine.Prepare(()=>true,500);
            f.Sender=strokes=>{f.Deliver(strokes[1]);f.Deliver(strokes[0]);f.Deliver(strokes[2]);f.Deliver(strokes[3]);return 4;};
            check("Shortcut reordered V cannot precede Ctrl",!lease.Invoke()&&!lease.CtrlDownPassed&&!lease.VDownPassed&&lease.KeysReleased);
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);
            f.Sender=strokes=>{f.Deliver(strokes[0]);f.Deliver(strokes[0]);f.Deliver(strokes[1]);f.Deliver(strokes[2]);f.Deliver(strokes[3]);return 4;};
            check("Shortcut duplicate Ctrl cancels V but preserves owned release",!lease.Invoke()&&lease.CtrlDownPassed&&!lease.VDownPassed&&lease.KeysReleased);

            f=new Fake();lease=f.Engine.Prepare(()=>true,50);f.Time=51;
            check("Shortcut expired before invocation emits nothing",!lease.Invoke()&&!lease.InvocationStarted&&f.Batches.Count==0);
            f=new Fake();lease=f.Engine.Prepare(()=>true,50);f.Before=(i,s)=>{if(i==1)f.Time=51;};
            check("Shortcut expiry after Ctrl vetoes V while releasing Ctrl",!lease.Invoke()&&lease.CtrlDownPassed&&!lease.VDownPassed&&lease.KeysReleased);
            f=new Fake();int guards=0;lease=f.Engine.Prepare(()=>{if(++guards==3)throw new InvalidOperationException();return true;},500);
            check("Shortcut fast guard exception fails closed and releases keys",!lease.Invoke()&&lease.CtrlDownPassed&&!lease.VDownPassed&&lease.KeysReleased);

            for(int count=1;count<=3;count++) {
                f=new Fake();lease=f.Engine.Prepare(()=>true,500);int inserted=count;Fake target=f;
                f.Sender=strokes=>{int n=target.Batches.Count==1?inserted:strokes.Length;for(int i=0;i<n;i++)target.Deliver(strokes[i]);return (uint)n;};
                check("Shortcut partial insertion "+count+" is not success",!lease.Invoke()&&lease.MainInserted==count);
                check("Shortcut partial insertion "+count+" releases owned keys only",lease.KeysReleased&&lease.RecoveryInserted==2&&lease.RecoveryObserved==2&&f.Batches.Count==2&&f.Batches[1].All(s=>!s.Down&&(s.Key==0x56||s.Key==0xA2)));
                check("Shortcut partial insertion "+count+" recovery leaves engine usable",!f.Engine.Blocked&&f.Engine.Prepare(()=>true,500).Prepared);
            }
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);f.Sender=strokes=>0;
            check("Shortcut zero inserted events need no key release",!lease.Invoke()&&lease.KeysReleased&&f.Batches.Count==1&&!f.Engine.Blocked);

            f=new Fake();lease=f.Engine.Prepare(()=>true,500);Fake failureTarget=f;
            f.Sender=strokes=>{if(failureTarget.Batches.Count==1){failureTarget.Deliver(strokes[0]);return 1;}return 0;};
            check("Shortcut failed key release is explicit and blocks future batches",!lease.Invoke()&&!lease.KeysReleased&&f.Engine.Blocked&&lease.Reason=="shortcut_key_release_unconfirmed"&&f.Engine.Prepare(()=>true,500).Reason=="shortcut_recovery_blocked");

            f=new Fake();lease=f.Engine.Prepare(()=>true,50);NativeEmojiPasteShortcut.Stroke[] late=null;Fake delayed=f;
            f.Sender=strokes=>{if(delayed.Batches.Count==1){late=strokes;return 4;}foreach(var s in strokes)delayed.Deliver(s);return (uint)strokes.Length;};
            check("Shortcut unobserved input times out instead of claiming success",!lease.Invoke()&&lease.MainObserved==0&&lease.RecoveryObserved==2&&lease.KeysReleased);
            lease.Dispose();
            check("Shortcut late original keydowns are blocked after timeout",late.All(s=>f.Deliver(s)==NativeEmojiPasteShortcut.HookDisposition.BlockOwned)&&!lease.CtrlDownPassed&&!lease.VDownPassed);

            f=new Fake();lease=f.Engine.Prepare(()=>true,500);var same=lease;f.Before=(i,s)=>{if(i==1)same.Dispose();};
            check("Shortcut disposal during injection preserves only cleanup keyups",!lease.Invoke()&&lease.CtrlDownPassed&&!lease.VDownPassed&&lease.KeysReleased);
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);
            check("Shortcut only one prepared lease can exist",f.Engine.Prepare(()=>true,500).Reason=="shortcut_busy");
            lease.Dispose();check("Shortcut disposed uninvoked lease releases its slot",f.Engine.Prepare(()=>true,500).Prepared);
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);lease.Invoke();int batches=f.Batches.Count;
            var guardField=typeof(NativeEmojiPasteShortcut.Lease).GetField("guard",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            check("Shortcut successful marker tombstones release the captured guard",guardField.GetValue(lease)==null);
            check("Shortcut same lease cannot invoke twice",!lease.Invoke()&&f.Batches.Count==batches);
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);lease.Dispose();
            check("Shortcut disposed marker tombstones release the captured guard",guardField.GetValue(lease)==null);
            check("Shortcut invalid guard and lifetime reject without injection",!f.Engine.Prepare(null,500).Prepared&&!f.Engine.Prepare(()=>true,49).Prepared&&!f.Engine.Prepare(()=>true,2001).Prepared);

            f=new Fake();lease=f.Engine.Prepare(()=>true,500);
            check("Shortcut stop cancels a prepared unused lease without input",f.Engine.TryStop()&&!lease.Invoke()&&lease.Reason=="shortcut_stopping"&&f.Batches.Count==0);
            check("Shortcut stopped engine refuses all future leases",f.Engine.TryStop()&&!f.Engine.Prepare(()=>true,500).Prepared&&f.Engine.Prepare(()=>true,500).Reason=="shortcut_stopping");
            f=new Fake();lease=f.Engine.Prepare(()=>true,500);bool stoppedWhileRunning=true;Fake stopping=f;
            f.Before=(i,s)=>{if(i==1)stoppedWhileRunning=stopping.Engine.TryStop();};
            check("Shortcut running stop keeps hook alive until keys are released",!lease.Invoke()&&!stoppedWhileRunning&&lease.CtrlDownPassed&&!lease.VDownPassed&&lease.KeysReleased&&f.Engine.TryStop());
            f=new Fake();Fake registration=f;bool stopDuringRegistration=false;
            f.OnNewMarker=()=>{stopDuringRegistration=registration.Engine.TryStop();};
            lease=f.Engine.Prepare(()=>true,500);
            check("Shortcut stop during registration cannot publish an active batch",stopDuringRegistration&&!lease.Prepared&&!lease.Invoke()&&lease.Reason=="shortcut_stopping"&&f.Batches.Count==0&&f.Engine.TryStop());
        }
#if OFFLINE_NATIVE_SHORTCUT_TEST
        static int Main() {
            int passed=0,failed=0;
            Run((name,ok)=>{if(ok)passed++;else {failed++;Console.WriteLine("FAIL "+name);}});
            Console.WriteLine("Native shortcut checks: "+passed+" passed, "+failed+" failed. No native input, UI, or clipboard calls.");
            return failed==0?0:1;
        }
#endif
    }
}
