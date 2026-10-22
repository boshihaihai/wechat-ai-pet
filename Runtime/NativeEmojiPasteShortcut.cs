using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace WeChatPet.Runtime {
    // One Ctrl+V batch, gated by the existing low-level hook. No focus changes,
    // mouse input, clipboard access, Enter, or send command is present here.
    // Invoke success means injected keys were observed, never that a draft matches.
    internal static class NativeEmojiPasteShortcut {
        internal enum HookDisposition { Unrelated, AllowOwned, BlockOwned }
        static readonly Engine production=new Engine(new NativePlatform());
        internal static Lease Prepare(Func<bool> stillValidFast,int lifetimeMilliseconds=500) {return production.Prepare(stillValidFast,lifetimeMilliseconds);}
        internal static HookDisposition FilterHook(uint virtualKey,bool down,uint flags,UIntPtr extraInfo) {return production.FilterHook(virtualKey,down,flags,extraInfo.ToUInt64());}
        // Keep the hook/message pump installed until true. This never waits on
        // the hook thread: an in-flight Invoke releases its own slot in finally.
        internal static bool TryStop() {return production.TryStop();}

        internal sealed class Stroke {
            internal ushort Key;
            internal bool Down;
            internal ulong Marker;
        }
        internal interface IPlatform {
            long Milliseconds {get;}
            ulong NewMarker();
            uint Send(Stroke[] strokes);
            void WaitOneMillisecond();
        }
        sealed class Token {internal Lease Lease;internal int Index;}
        internal sealed class Engine {
            readonly IPlatform platform;
            readonly ConcurrentDictionary<ulong,Token> tokens=new ConcurrentDictionary<ulong,Token>();
            Lease active;
            int issued,blocked,closing;
            internal Engine(IPlatform platform) {this.platform=platform;}
            internal bool Blocked {get{return Interlocked.CompareExchange(ref blocked,0,0)!=0;}}
            bool Closing {get{return Interlocked.CompareExchange(ref closing,0,0)!=0;}}
            internal bool TryStop() {
                Interlocked.Exchange(ref closing,1);
                var current=Interlocked.CompareExchange(ref active,null,null);
                if(current!=null)current.RequestStop();
                return Interlocked.CompareExchange(ref active,null,null)==null;
            }
            internal Lease Prepare(Func<bool> guard,int lifetime) {
                if(Closing)return Lease.Failed("shortcut_stopping");
                if(guard==null||lifetime<50||lifetime>2000)return Lease.Failed("shortcut_invalid_guard_or_deadline");
                if(Blocked)return Lease.Failed("shortcut_recovery_blocked");
                // Retain random markers for this process so delayed/replayed owned
                // keydowns remain blocked after cancellation. Bound their storage.
                if(Interlocked.Increment(ref issued)>4096)return Lease.Failed("shortcut_marker_capacity");
                var lease=new Lease(this,platform,guard,lifetime);
                if(Interlocked.CompareExchange(ref active,lease,null)!=null)return Lease.Failed("shortcut_busy");
                if(Closing) {lease.RequestStop();return lease;}
                try {
                    for(int i=0;i<lease.Strokes.Length;i++) {
                        bool registered=false;
                        for(int attempt=0;attempt<32&&!registered;attempt++) {
                            ulong marker=platform.NewMarker();
                            if(marker==0)continue;
                            if(tokens.TryAdd(marker,new Token {Lease=lease,Index=i})) {lease.Strokes[i].Marker=marker;registered=true;}
                        }
                        if(!registered)throw new InvalidOperationException();
                    }
                    if(Closing) {lease.RequestStop();return lease;}
                    lease.SetPrepared();return lease;
                } catch {lease.Cancel("shortcut_marker_unavailable");Release(lease);return lease;}
            }
            internal HookDisposition FilterHook(uint key,bool down,uint flags,ulong marker) {
                Token token;
                if((flags&0x10)==0||!tokens.TryGetValue(marker,out token))return HookDisposition.Unrelated;
                Stroke expected=token.Lease.Strokes[token.Index];
                bool keyMatches=key==expected.Key||(expected.Key==0xA2&&key==0x11);
                // A forged marker on an unrelated event grants no exemption from
                // the observer's ordinary activity path. Never swallow real keys.
                if(!keyMatches||down!=expected.Down) {
                    token.Lease.Cancel("shortcut_marker_event_mismatch");return HookDisposition.Unrelated;
                }
                // A known injected Ctrl/V remains ours even if Windows reports
                // an unexpected modifier/integrity flag. Never let that bypass
                // the keydown veto or lose track of a key we already passed.
                if(((flags&0x80)==0)!=down||(flags&~0x90u)!=0)
                    token.Lease.Cancel("shortcut_injected_flags_changed");
                return token.Lease.Filter(token.Index);
            }
            internal void Release(Lease lease) {Interlocked.CompareExchange(ref active,null,lease);}
            internal void Block() {Interlocked.Exchange(ref blocked,1);}
        }

        internal sealed class Lease : IDisposable {
            const int Cancelled=1,HeldCtrl=2,HeldV=4,PassedCtrl=8,PassedV=16,Invoked=32,RecoveryArmed=64;
            readonly Engine engine;
            readonly IPlatform platform;
            Func<bool> guard;
            readonly long deadline;
            internal readonly Stroke[] Strokes;
            int state,prepared,invokeCall,successful,mainInserted,recoveryInserted;
            string failure;
            internal Lease(Engine engine,IPlatform platform,Func<bool> guard,int lifetime) {
                this.engine=engine;this.platform=platform;this.guard=guard;deadline=platform.Milliseconds+lifetime;
                Strokes=new[]{new Stroke{Key=0xA2,Down=true},new Stroke{Key=0x56,Down=true},new Stroke{Key=0x56},new Stroke{Key=0xA2},new Stroke{Key=0x56},new Stroke{Key=0xA2}};
            }
            Lease(string reason) {failure=reason;}
            internal static Lease Failed(string reason) {return new Lease(reason);}
            internal void SetPrepared() {Interlocked.Exchange(ref prepared,1);}
            int State {get{return Interlocked.CompareExchange(ref state,0,0);}}
            internal bool Prepared {get{return Interlocked.CompareExchange(ref prepared,0,0)!=0;}}
            internal bool InvocationStarted {get{return (State&Invoked)!=0;}}
            internal string Reason {get{return Interlocked.CompareExchange(ref failure,null,null)??(Interlocked.CompareExchange(ref successful,0,0)!=0?"shortcut_injected":InvocationStarted?"shortcut_injection_pending":"shortcut_prepared");}}
            internal int MainInserted {get{return Interlocked.CompareExchange(ref mainInserted,0,0);}}
            internal int RecoveryInserted {get{return Interlocked.CompareExchange(ref recoveryInserted,0,0);}}
            internal int MainObserved {get{return Count((State>>8)&15);}}
            internal int RecoveryObserved {get{return Count((State>>12)&3);}}
            internal bool CtrlDownPassed {get{return (State&PassedCtrl)!=0;}}
            internal bool VDownPassed {get{return (State&PassedV)!=0;}}
            internal bool KeysReleased {get{return (State&(HeldCtrl|HeldV))==0;}}
            static int Count(int mask) {int n=0;while(mask!=0){n+=mask&1;mask>>=1;}return n;}
            void AddState(int bits) {int before;do{before=State;}while(Interlocked.CompareExchange(ref state,before|bits,before)!=before);}
            internal void Cancel(string reason) {Interlocked.CompareExchange(ref failure,reason,null);AddState(Cancelled);ClearGuardIfReleased();}
            void ClearGuardIfReleased() {
                if(KeysReleased&&((State&Cancelled)!=0||Interlocked.CompareExchange(ref successful,0,0)!=0))
                    Interlocked.Exchange(ref guard,null);
            }
            bool Guard() {try{var current=Interlocked.CompareExchange(ref guard,null,null);return current!=null&&platform.Milliseconds<=deadline&&current();}catch{return false;}}

            // Called only by the input hook; no waits, native calls, UIA or locks.
            internal HookDisposition Filter(int index) {
                int seenBit=1<<(8+index);
                while(true) {
                    int before=State;
                    if((before&seenBit)!=0) {Cancel("shortcut_duplicate_event");return HookDisposition.BlockOwned;}
                    bool recovery=index>=4;
                    bool order=recovery?((before&RecoveryArmed)!=0&&((before>>12)&3)==((1<<(index-4))-1)):
                        ((before&Invoked)!=0&&((before>>8)&15)==((1<<index)-1));
                    int after=before|seenBit;
                    if(!order) {after|=Cancelled;Interlocked.CompareExchange(ref failure,"shortcut_event_order_changed",null);}
                    bool down=Strokes[index].Down;
                    int held=Strokes[index].Key==0xA2?HeldCtrl:HeldV;
                    bool allow;
                    if(down) {
                        allow=order&&(before&Cancelled)==0&&(index==0||(before&HeldCtrl)!=0)&&Guard();
                        if(allow)after|=held|(index==0?PassedCtrl:PassedV);
                        else {after|=Cancelled;Interlocked.CompareExchange(ref failure,platform.Milliseconds>deadline?"shortcut_expired":"shortcut_guard_rejected",null);}
                    } else {
                        // Even after cancellation, release only a key this lease
                        // actually passed. Unpaired/duplicate keyups are suppressed.
                        allow=(before&held)!=0&&(!recovery||(before&RecoveryArmed)!=0);
                        if(allow)after&=~held;
                    }
                    if(Interlocked.CompareExchange(ref state,after,before)==before) {
                        ClearGuardIfReleased();
                        return allow?HookDisposition.AllowOwned:HookDisposition.BlockOwned;
                    }
                }
            }

            internal bool Invoke(Func<bool> beforeSend=null) {
                if(!Prepared)return false;
                if(Interlocked.CompareExchange(ref invokeCall,1,0)!=0) {Cancel("shortcut_already_invoked");return false;}
                try {
                    if((State&Cancelled)!=0||!Guard()) {Cancel("shortcut_guard_rejected");return false;}
                    // Publish the caller's invocation marker only after the last
                    // early guard. A veto here emits no keys. Once marked, always
                    // treat Send as potentially delivered; the hook still checks
                    // the live guard before allowing either keydown.
                    if(beforeSend!=null) {
                        try {if(!beforeSend()){Cancel("shortcut_preinvoke_rejected");return false;}}
                        catch {Cancel("shortcut_preinvoke_exception");return false;}
                    }
                    AddState(Invoked);
                    bool threw=false;
                    try {Interlocked.Exchange(ref mainInserted,(int)platform.Send(new[]{Strokes[0],Strokes[1],Strokes[2],Strokes[3]}));}
                    catch {threw=true;Cancel("shortcut_send_exception");}
                    if(MainInserted!=4)Cancel("shortcut_partial_insertion");
                    if(MainInserted==4)WaitForMask(15<<8,deadline);
                    if(MainInserted==4&&((State>>8)&15)==15&&(State&Cancelled)==0&&CtrlDownPassed&&VDownPassed&&KeysReleased) {
                        Interlocked.Exchange(ref successful,1);return true;
                    }
                    Cancel("shortcut_hook_unconfirmed");
                    // SendInput's reported count is not draft completion. A late
                    // primary keydown is now vetoed; one keyup-only batch repairs
                    // any primary prefix accepted before a partial/late failure.
                    bool noEvents=MainInserted==0&&MainObserved==0&&!threw;
                    if(!noEvents&&(!KeysReleased||MainObserved<4||threw))Recover();
                    return false;
                } finally {ClearGuardIfReleased();engine.Release(this);}
            }
            void WaitForMask(int mask,long until) {
                while((State&mask)!=mask&&platform.Milliseconds<=until)platform.WaitOneMillisecond();
            }
            void Recover() {
                AddState(RecoveryArmed);
                try {Interlocked.Exchange(ref recoveryInserted,(int)platform.Send(new[]{Strokes[4],Strokes[5]}));}
                catch {Interlocked.Exchange(ref recoveryInserted,0);}
                WaitForMask(3<<12,platform.Milliseconds+200);
                if(!KeysReleased||RecoveryInserted!=2||RecoveryObserved!=2) {
                    engine.Block();Interlocked.Exchange(ref failure,"shortcut_key_release_unconfirmed");
                }
            }
            internal void RequestStop() {
                Cancel("shortcut_stopping");
                // If Invoke wins this race it must observe Cancelled before it
                // sends anything; if it already started, its finally owns release.
                if(Interlocked.CompareExchange(ref invokeCall,0,0)==0)engine.Release(this);
            }
            public void Dispose() {
                if(engine==null)return;
                // Preserve a successful result for diagnostics, but disarm any
                // late/replayed keydown. Original/recovery keyups remain permitted.
                AddState(Cancelled);
                ClearGuardIfReleased();
                if(Interlocked.CompareExchange(ref invokeCall,0,0)==0)engine.Release(this);
            }
        }

        sealed class NativePlatform : IPlatform {
            public long Milliseconds {get{return (long)(Stopwatch.GetTimestamp()*(1000.0/Stopwatch.Frequency));}}
            public ulong NewMarker() {var bytes=new byte[8];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return BitConverter.ToUInt64(bytes,0);}
            public uint Send(Stroke[] strokes) {
                if(IntPtr.Size!=8)throw new PlatformNotSupportedException("Only validated x64 input layout is supported");
                var inputs=new INPUT[strokes.Length];
                for(int i=0;i<inputs.Length;i++)inputs[i]=new INPUT {Type=1,Keyboard=new KEYBDINPUT {VirtualKey=strokes[i].Key,Flags=strokes[i].Down?0u:2u,ExtraInfo=new UIntPtr(strokes[i].Marker)}};
                return SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(INPUT)));
            }
            public void WaitOneMillisecond() {Thread.Sleep(1);}
            [StructLayout(LayoutKind.Sequential)]struct KEYBDINPUT {internal ushort VirtualKey,Scan;internal uint Flags,Time;internal UIntPtr ExtraInfo;}
            [StructLayout(LayoutKind.Explicit,Size=40)]struct INPUT {[FieldOffset(0)]internal uint Type;[FieldOffset(8)]internal KEYBDINPUT Keyboard;}
            [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,INPUT[] inputs,int size);
        }
    }
}
