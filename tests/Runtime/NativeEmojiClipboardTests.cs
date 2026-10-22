using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace WeChatPet.Runtime {
    // Entirely in-memory: no Windows APIs, UIA, or system clipboard access. The production
    // entry point is tested only after shutdown, when it rejects before apartment creation.
    internal static class NativeEmojiClipboardTests {
        internal static void Run(Action<string,bool> check) {
            Case(check,"verified paste restores every original format",()=> {
                var p=Original();var original=p.CopyData();var lease=NativeEmojiClipboard.BeginForTests(p,"好呀[笑脸]");
                Assert(lease.CanInvoke&&p.CurrentFormats.SequenceEqual(new uint[]{p.OwnerFormat,p.PrivacyFormat,13}));
                Assert(p.Text=="好呀[笑脸]"&&p.Peek(p.PrivacyFormat).SequenceEqual(new byte[]{1}));
                Assert(lease.MarkInvocationStarted());lease.Complete(true);
                Assert(lease.Restored&&lease.Status=="verified_restored"&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"dispose before invocation restores",()=> {
                var p=Original();var original=p.CopyData();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                lease.Dispose();Assert(lease.Restored&&!lease.InvocationStarted&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"shortcut guard cancellation before input preserves the original clipboard",()=> {
                var p=Original();var original=p.CopyData();var lease=NativeEmojiClipboard.BeginForTests(p,"[调皮]");
                var input=new ShortcutFake();var shortcut=input.Engine.Prepare(()=>false,500);
                Assert(!shortcut.Invoke(lease.MarkInvocationStarted)&&input.Batches==0&&!shortcut.InvocationStarted&&!lease.InvocationStarted);
                lease.Dispose();shortcut.Dispose();
                Assert(lease.Restored&&lease.Status=="not_invoked_restored"&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"shortcut clipboard veto emits no input and preserves a newer user copy",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[调皮]");
                var input=new ShortcutFake();var shortcut=input.Engine.Prepare(()=>true,500);p.ExternalCopy("new user copy");
                Assert(!shortcut.Invoke(lease.MarkInvocationStarted)&&input.Batches==0&&!shortcut.InvocationStarted&&!lease.InvocationStarted);
                lease.Dispose();shortcut.Dispose();Assert(p.Text=="new user copy"&&!lease.Restored);NoLeaks(p);
            });
            Case(check,"shortcut clipboard lock before input safely restores on cleanup",()=> {
                var p=Original();var original=p.CopyData();var lease=NativeEmojiClipboard.BeginForTests(p,"[调皮]");
                var input=new ShortcutFake();var shortcut=input.Engine.Prepare(()=>true,500);p.FailNextOpen=true;
                Assert(!shortcut.Invoke(lease.MarkInvocationStarted)&&input.Batches==0&&!lease.InvocationStarted);
                lease.Dispose();shortcut.Dispose();Assert(lease.Restored&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"shortcut input invocation with unconfirmed result never restores old clipboard",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[调皮]");
                var input=new ShortcutFake{Observe=false};var shortcut=input.Engine.Prepare(()=>true,500);
                Assert(!shortcut.Invoke(lease.MarkInvocationStarted)&&input.Batches>0&&shortcut.InvocationStarted&&lease.InvocationStarted);
                lease.Dispose();shortcut.Dispose();
                Assert(!lease.Restored&&lease.Isolated&&p.CurrentFormats.Length==0&&lease.Status=="unverified_isolated_not_restored");NoLeaks(p);
            });
            Case(check,"shortcut cancellation after publication still blocks every keydown",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[调皮]");
                var input=new ShortcutFake();bool eligible=true;var shortcut=input.Engine.Prepare(()=>eligible,500);
                Assert(!shortcut.Invoke(()=>{bool marked=lease.MarkInvocationStarted();eligible=false;return marked;})&&input.Batches==1&&
                    !shortcut.CtrlDownPassed&&!shortcut.VDownPassed&&shortcut.KeysReleased);
                lease.Dispose();shortcut.Dispose();Assert(!lease.Restored&&lease.Isolated&&p.CurrentFormats.Length==0);NoLeaks(p);
            });
            Case(check,"shortcut delivery alone cannot confirm clipboard restoration",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[调皮]");
                var input=new ShortcutFake();var shortcut=input.Engine.Prepare(()=>true,500);
                Assert(shortcut.Invoke(lease.MarkInvocationStarted)&&input.Batches==1&&shortcut.VDownPassed&&shortcut.KeysReleased);
                lease.Dispose();shortcut.Dispose();Assert(!lease.Restored&&lease.Isolated&&p.CurrentFormats.Length==0);NoLeaks(p);
            });
            Case(check,"shortcut plus verified draft restores original clipboard exactly once",()=> {
                var p=Original();var original=p.CopyData();var lease=NativeEmojiClipboard.BeginForTests(p,"[调皮]");
                var input=new ShortcutFake();var shortcut=input.Engine.Prepare(()=>true,500);
                Assert(shortcut.Invoke(lease.MarkInvocationStarted)&&input.Batches==1);
                lease.Complete(true);lease.Dispose();shortcut.Dispose();
                Assert(lease.Restored&&lease.Status=="verified_restored"&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"unverified invocation isolates instead of restoring old data",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.MarkInvocationStarted());lease.Dispose();
                Assert(!lease.Restored&&lease.Isolated&&lease.Finished&&p.CurrentFormats.Length==0);
                Assert(lease.Status=="unverified_isolated_not_restored");NoLeaks(p);
            });
            Case(check,"new user copy before invocation is not overwritten",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");p.ExternalCopy("user copy");
                Assert(!lease.MarkInvocationStarted());lease.Dispose();
                Assert(p.Text=="user copy"&&!lease.Restored&&lease.Status=="clipboard_replaced_before_invocation");NoLeaks(p);
            });
            foreach(bool verified in new[]{false,true}) {
                bool result=verified;
                Case(check,"new user copy after invocation is retained (verified="+result+")",()=> {
                    var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                    Assert(lease.MarkInvocationStarted());p.ExternalCopy("new copy");lease.Complete(result);
                    Assert(p.Text=="new copy"&&!lease.Restored&&!lease.Isolated);NoLeaks(p);
                });
            }
            Case(check,"unsupported format leaves clipboard untouched",()=> {
                var p=Original();var original=p.CopyData();p.FailDuplicateFormat=2;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(!lease.CanInvoke&&lease.Finished&&p.EmptyCalls==0&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"duplicate exception releases earlier snapshot handles",()=> {
                var p=Original();var original=p.CopyData();p.ThrowDuplicateFormat=2;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.Status=="snapshot_failed"&&p.EmptyCalls==0&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"oversized snapshots are rejected without publication",()=> {
                var p=Original();p.DuplicateSizeOverride=33L*1024*1024;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.Status=="snapshot_too_large"&&p.EmptyCalls==0);NoLeaks(p);
            });
            Case(check,"sequence changing during snapshot is rejected",()=> {
                var p=Original();p.ChangeSequenceOnDuplicate=true;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.Status=="snapshot_changed"&&p.EmptyCalls==0);NoLeaks(p);
            });
            Case(check,"format list changing during snapshot is rejected",()=> {
                var p=Original();p.ChangeSecondFormats=true;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.Status=="snapshot_changed"&&p.EmptyCalls==0);NoLeaks(p);
            });
            Case(check,"temporary allocation failure preserves original clipboard",()=> {
                var p=Original();var original=p.CopyData();p.FailAllocateCall=2;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.Status=="temporary_allocation_failed"&&p.EmptyCalls==0&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"partial temporary publication restores all original formats",()=> {
                var p=Original();var original=p.CopyData();p.FailPutCall=2;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.Status=="publish_failed_restored"&&lease.Restored&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"throw during temporary publication restores original formats",()=> {
                var p=Original();var original=p.CopyData();p.ThrowPutCall=2;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.Status=="prepare_exception_restored"&&lease.Restored&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"partial restore is reported and not retried",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.MarkInvocationStarted());p.FailPutCall=p.PutCalls+2;lease.Complete(true);
                Assert(!lease.Restored&&lease.Status=="restore_failed"&&p.EmptyCalls==2&&p.PutCalls==6);
                Assert(p.CurrentFormats.SequenceEqual(new uint[]{13,0xC234}));NoLeaks(p);
            });
            Case(check,"restore exception never empties partially restored data twice",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.MarkInvocationStarted());p.ThrowPutCall=p.PutCalls+2;lease.Complete(true);
                Assert(!lease.Restored&&lease.Status=="restore_failed"&&p.EmptyCalls==2&&p.PutCalls==6);NoLeaks(p);
            });
            Case(check,"clipboard busy during cleanup does not claim restoration",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.MarkInvocationStarted());p.FailNextOpen=true;lease.Complete(true);
                Assert(!lease.Restored&&!lease.Isolated&&!lease.Finished&&lease.Status=="cleanup_clipboard_busy_pending");
                lease.Complete(false);Assert(lease.Restored&&lease.Status=="verified_restored");NoLeaks(p);
            });
            Case(check,"failed isolation is explicitly reported",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.MarkInvocationStarted());p.FailEmptyCall=p.EmptyCalls+1;lease.Dispose();
                Assert(!lease.Isolated&&!lease.Restored&&!lease.Finished&&lease.Status=="isolation_pending_not_restored");
                lease.Dispose();Assert(lease.Isolated&&!lease.Restored);NoLeaks(p);
            });
            Case(check,"disposal and completion are idempotent",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.MarkInvocationStarted());Assert(!lease.MarkInvocationStarted());lease.Complete(true);
                int calls=p.EmptyCalls+p.PutCalls;lease.Complete(false);lease.Dispose();
                Assert(p.EmptyCalls+p.PutCalls==calls&&lease.Restored);NoLeaks(p);
            });
            Case(check,"changed marker with unchanged sequence prevents restore",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                p.CorruptMarker();lease.Dispose();Assert(!lease.Restored&&p.EmptyCalls==1);
                Assert(lease.SequenceChanged==false&&lease.OwnerTokenMatches==false&&lease.OwnerWindowMatches==true);NoLeaks(p);
            });
            Case(check,"unchanged marker with new sequence prevents restore",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                p.AdvanceSequence();lease.Dispose();Assert(!lease.Restored&&p.EmptyCalls==1);
                Assert(lease.SequenceChanged==true&&lease.OwnerTokenMatches==true&&lease.OwnerWindowMatches==true);NoLeaks(p);
            });
            Case(check,"zero-filled HGLOBAL padding does not invalidate own marker",()=> {
                var p=Original();p.PadOwnerRead=true;var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.MarkInvocationStarted());lease.Complete(true);Assert(lease.Restored);NoLeaks(p);
            });
            Case(check,"empty original clipboard is restored to empty",()=> {
                var p=new FakeClipboard();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                lease.Dispose();Assert(lease.Restored&&p.CurrentFormats.Length==0);NoLeaks(p);
            });
            foreach(string value in new[]{null,"","has\0null",new string('x',32769)}) {
                string input=value;
                Case(check,"invalid input rejected before opening clipboard (length="+(input==null?-1:input.Length)+")",()=> {
                    var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,input);
                    Assert(!lease.CanInvoke&&lease.Status=="invalid_text"&&p.OpenCalls==0);NoLeaks(p);
                });
            }
            Case(check,"external copy immediately before cleanup lock is preserved",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.MarkInvocationStarted());p.BeforeNextOpen=()=>p.ExternalCopy("racing copy");lease.Complete(true);
                Assert(p.Text=="racing copy"&&!lease.Restored);NoLeaks(p);
            });
            Case(check,"initial clipboard lock failure performs no mutation",()=> {
                var p=Original();p.FailNextOpen=true;var original=p.CopyData();
                var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.Status=="clipboard_busy"&&p.SameData(original)&&p.EmptyCalls==0);NoLeaks(p);
            });
            Case(check,"failed initial empty leaves original data intact",()=> {
                var p=Original();p.FailEmptyCall=1;var original=p.CopyData();
                var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");
                Assert(lease.Status=="clipboard_empty_failed"&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"busy invocation gate cannot mark invocation and disposal restores",()=> {
                var p=Original();var lease=NativeEmojiClipboard.BeginForTests(p,"[笑脸]");p.FailNextOpen=true;
                Assert(!lease.MarkInvocationStarted()&&!lease.InvocationStarted);lease.Dispose();Assert(lease.Restored);NoLeaks(p);
            });
            Case(check,"empty production shutdown does not initialize Windows clipboard",()=> {
                Assert(NativeEmojiClipboard.CancelActive()=="no_active_clipboard_transaction");
                Assert(NativeEmojiClipboard.Begin("[色]").Status=="clipboard_worker_stopping");
            });
            Case(check,"publication deadline restores an uninvoked lease",()=> {
                var p=Original();var original=p.CopyData();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var lease=slot.Begin(p,a=>a(),timers.Schedule,"[色]");
                Assert(timers.Items.Count==1&&timers.Items[0].Delay==3500&&slot.HasActive);
                timers.Items[0].Fire();
                Assert(lease.Restored&&!lease.InvocationStarted&&p.SameData(original)&&!slot.HasActive&&lease.DeadlineElapsed);
                Assert(timers.Items[0].Disposed&&!lease.MarkInvocationStarted());NoLeaks(p);
            });
            Case(check,"publication deadline isolates an unconfirmed invocation",()=> {
                var p=Original();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var lease=slot.Begin(p,a=>a(),timers.Schedule,"[调皮]");Assert(lease.MarkInvocationStarted());
                timers.Items[0].Fire();
                Assert(lease.Isolated&&!lease.Restored&&!slot.HasActive&&p.CurrentFormats.Length==0&&lease.DeadlineElapsed);NoLeaks(p);
            });
            Case(check,"verified completion cancels deadline and late callback is inert",()=> {
                var p=Original();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var lease=slot.Begin(p,a=>a(),timers.Schedule,"[色]");Assert(lease.MarkInvocationStarted());lease.Complete(true);
                Assert(timers.Items[0].Disposed&&lease.Restored&&!slot.HasActive);
                int calls=p.EmptyCalls+p.PutCalls;timers.Items[0].Fire();
                Assert(calls==p.EmptyCalls+p.PutCalls&&lease.Restored&&!lease.DeadlineElapsed);NoLeaks(p);
            });
            foreach(bool invoked in new[]{false,true}) {
                bool started=invoked;
                Case(check,"shutdown cleans published lease (invoked="+started+")",()=> {
                    var p=Original();var original=p.CopyData();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                    var lease=slot.Begin(p,a=>a(),timers.Schedule,"[调皮]");if(started)Assert(lease.MarkInvocationStarted());
                    slot.CloseAndCancel(0,null);
                    Assert(lease.Finished&&!slot.HasActive&&slot.IsClosed&&timers.Items[0].Disposed);
                    Assert(started?lease.Isolated&&p.CurrentFormats.Length==0:lease.Restored&&p.SameData(original));
                    int opens=p.OpenCalls;
                    Assert(slot.Begin(p,a=>a(),timers.Schedule,"new").Status=="clipboard_worker_stopping"&&p.OpenCalls==opens);NoLeaks(p);
                });
            }
            Case(check,"late old deadline cannot release or overwrite the next lease",()=> {
                var p=Original();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var first=slot.Begin(p,a=>a(),timers.Schedule,"[色]");var oldTimer=timers.Items[0];first.Dispose();
                var second=slot.Begin(p,a=>a(),timers.Schedule,"[调皮]");int calls=p.EmptyCalls+p.PutCalls;oldTimer.Fire();
                Assert(p.Text=="[调皮]"&&calls==p.EmptyCalls+p.PutCalls&&slot.HasActive&&second.CanInvoke);
                Assert(slot.Begin(p,a=>a(),timers.Schedule,"third").Status=="clipboard_transaction_busy");
                second.Dispose();NoLeaks(p);
            });
            Case(check,"busy deadline cleanup retains lease and retries before invocation",()=> {
                var p=Original();var original=p.CopyData();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var lease=slot.Begin(p,a=>a(),timers.Schedule,"[色]");p.FailNextOpen=true;timers.Items[0].Fire();
                Assert(!lease.Finished&&slot.HasActive&&!lease.CanInvoke&&!lease.MarkInvocationStarted());
                Assert(timers.Items[0].Disposed&&timers.Items.Count==2&&timers.Items[1].Delay==100);
                Assert(slot.Begin(p,a=>a(),timers.Schedule,"other").Status=="clipboard_transaction_busy");
                timers.Items[1].Fire();Assert(lease.Restored&&p.SameData(original)&&!slot.HasActive);NoLeaks(p);
            });
            Case(check,"busy invoked deadline cleanup retry only isolates",()=> {
                var p=Original();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var lease=slot.Begin(p,a=>a(),timers.Schedule,"[色]");Assert(lease.MarkInvocationStarted());
                p.FailNextOpen=true;timers.Items[0].Fire();Assert(!lease.Finished&&slot.HasActive);
                timers.Items[1].Fire();Assert(lease.Isolated&&!lease.Restored&&!slot.HasActive);NoLeaks(p);
            });
            Case(check,"verified result persists across cleanup retry",()=> {
                var p=Original();var original=p.CopyData();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var lease=slot.Begin(p,a=>a(),timers.Schedule,"[色]");Assert(lease.MarkInvocationStarted());
                p.FailNextOpen=true;lease.Complete(true);Assert(!lease.Finished&&timers.Items[0].Disposed);
                timers.Items[1].Fire();Assert(lease.Restored&&!lease.Isolated&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"failed empty during restoration preserves snapshot for retry",()=> {
                var p=Original();var original=p.CopyData();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var lease=slot.Begin(p,a=>a(),timers.Schedule,"[色]");p.FailEmptyCall=p.EmptyCalls+1;lease.Dispose();
                Assert(!lease.Finished&&slot.HasActive&&lease.Status=="restore_pending_not_restored");
                timers.Items[1].Fire();Assert(lease.Restored&&p.SameData(original)&&!slot.HasActive);NoLeaks(p);
            });
            Case(check,"failed isolation keeps ownership until retry succeeds",()=> {
                var p=Original();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var lease=slot.Begin(p,a=>a(),timers.Schedule,"[色]");Assert(lease.MarkInvocationStarted());
                p.FailEmptyCall=p.EmptyCalls+1;timers.Items[0].Fire();Assert(!lease.Finished&&slot.HasActive);
                timers.Items[1].Fire();Assert(lease.Isolated&&!lease.Restored&&!slot.HasActive);NoLeaks(p);
            });
            Case(check,"shutdown retry targets the captured lease only",()=> {
                var p=Original();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var first=slot.Begin(p,a=>a(),timers.Schedule,"[色]");p.FailNextOpen=true;
                NativeEmojiClipboard.Lease second=null;
                slot.CancelActive(1,()=> { first.Dispose();second=slot.Begin(p,a=>a(),timers.Schedule,"[调皮]"); });
                Assert(first.Restored&&second!=null&&second.CanInvoke&&p.Text=="[调皮]"&&slot.HasActive);
                second.Dispose();NoLeaks(p);
            });
            Case(check,"closing an empty slot rejects late Begin without touching platform",()=> {
                var p=Original();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                slot.CloseAndCancel(0,null);
                Assert(slot.Begin(p,a=>a(),timers.Schedule,"[色]").Status=="clipboard_worker_stopping");
                Assert(p.OpenCalls==0&&timers.Items.Count==0&&!slot.HasActive);NoLeaks(p);
            });
            Case(check,"verified completion and queued timer callback serialize",()=> {
                var p=Original();var original=p.CopyData();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                var lease=slot.Begin(p,a=>a(),timers.Schedule,"[色]");Assert(lease.MarkInvocationStarted());
                using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)) {
                    p.BeforeNextOpen=()=> { entered.Set();if(!release.WaitOne(2000))throw new Exception("release timed out"); };
                    var complete=new Thread(()=>lease.Complete(true)){IsBackground=true};complete.Start();
                    Assert(entered.WaitOne(2000));
                    var deadline=new Thread(()=>timers.Items[0].Fire()){IsBackground=true};deadline.Start();
                    release.Set();Assert(complete.Join(2000)&&deadline.Join(2000));
                }
                Assert(lease.Restored&&!lease.Isolated&&p.SameData(original)&&!slot.HasActive);NoLeaks(p);
            });
            Case(check,"shutdown catches a lease already registered during Begin",()=> {
                var p=Original();var original=p.CopyData();var timers=new ManualTimers();var slot=new NativeEmojiClipboard.ActiveLeaseSlot();
                using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)) {
                    p.BeforeNextOpen=()=> { entered.Set();if(!release.WaitOne(2000))throw new Exception("release timed out"); };
                    NativeEmojiClipboard.Lease lease=null;
                    var begin=new Thread(()=>lease=slot.Begin(p,a=>a(),timers.Schedule,"[色]")){IsBackground=true};begin.Start();
                    Assert(entered.WaitOne(2000));
                    var close=new Thread(()=>slot.CloseAndCancel(0,null)){IsBackground=true};close.Start();
                    Assert(SpinWait.SpinUntil(()=>slot.IsClosed,2000));
                    release.Set();Assert(begin.Join(2000)&&close.Join(2000));
                    Assert(lease!=null&&lease.Restored&&p.SameData(original)&&!slot.HasActive&&slot.IsClosed);
                }
                NoLeaks(p);
            });
            Case(check,"sequence and synthetic format committed on close are captured",()=> {
                var p=Original();var original=p.CopyData();p.SynthesizeOnNextClose=true;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[调皮]");
                Assert(p.CurrentFormats.Contains((uint)16)&&lease.CanInvoke&&lease.MarkInvocationStarted());
                Assert(lease.SequenceChanged==false&&lease.OwnerTokenMatches==true&&lease.OwnerWindowMatches==true);
                lease.Complete(true);Assert(lease.Restored&&p.SameData(original));NoLeaks(p);
            });
            Case(check,"close-committed publication restores when context cancels before invocation",()=> {
                var p=Original();var original=p.CopyData();p.SynthesizeOnNextClose=true;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[色]");lease.Dispose();
                Assert(lease.Restored&&!lease.InvocationStarted&&p.SameData(original)&&lease.SequenceChanged==false);NoLeaks(p);
            });
            Case(check,"foreign copy between publication close and sequence sample is retained",()=> {
                var p=Original();p.AfterNextClose=()=>p.ExternalCopy("external text");
                var lease=NativeEmojiClipboard.BeginForTests(p,"[色]");Assert(!lease.MarkInvocationStarted());
                Assert(p.Text=="external text"&&!lease.Restored&&lease.SequenceChanged==false);
                Assert(lease.OwnerWindowMatches==false&&lease.OwnerTokenMatches==false);NoLeaks(p);
            });
            Case(check,"copied ownership token cannot claim a foreign clipboard publication",()=> {
                var p=Original();p.AfterNextClose=()=>p.ExternalCopyWithOwnerToken("external text");
                var lease=NativeEmojiClipboard.BeginForTests(p,"[色]");lease.Dispose();
                Assert(p.Text=="external text"&&!lease.Restored&&lease.SequenceChanged==false);
                Assert(lease.OwnerWindowMatches==false&&lease.OwnerTokenMatches==true&&p.EmptyCalls==1);NoLeaks(p);
            });
            Case(check,"unknown sequence cannot authorize invocation even with own marker",()=> {
                var p=Original();p.ForceZeroSequence=true;
                var lease=NativeEmojiClipboard.BeginForTests(p,"[色]");Assert(!lease.MarkInvocationStarted());
                Assert(!lease.Restored&&lease.OwnerTokenMatches==true&&lease.OwnerWindowMatches==true);NoLeaks(p);
            });
        }
        sealed class ShortcutFake : NativeEmojiPasteShortcut.IPlatform {
            long time;ulong marker=100;
            internal int Batches;
            internal bool Observe=true;
            internal readonly NativeEmojiPasteShortcut.Engine Engine;
            internal ShortcutFake(){Engine=new NativeEmojiPasteShortcut.Engine(this);}
            public long Milliseconds {get{return time;}}
            public ulong NewMarker(){return ++marker;}
            public void WaitOneMillisecond(){time++;}
            public uint Send(NativeEmojiPasteShortcut.Stroke[] strokes) {
                Batches++;
                if(Observe)foreach(var stroke in strokes)Engine.FilterHook(stroke.Key,stroke.Down,stroke.Down?0x10u:0x90u,stroke.Marker);
                return (uint)strokes.Length;
            }
        }

        sealed class ManualTimers {
            internal sealed class Entry : IDisposable {
                readonly Action action;
                internal readonly int Delay;
                internal bool Disposed;
                internal Entry(Action action,int delay) { this.action=action;Delay=delay; }
                // Deliberately invokes even after disposal to simulate an already queued callback.
                internal void Fire() { action(); }
                public void Dispose() { Disposed=true; }
            }
            internal readonly List<Entry> Items=new List<Entry>();
            internal IDisposable Schedule(Action action,int milliseconds) {
                var entry=new Entry(action,milliseconds);Items.Add(entry);return entry;
            }
        }

        static void Case(Action<string,bool> check,string name,Action test) {
            try { test();check("Clipboard: "+name,true); }
            catch(Exception ex) { check("Clipboard: "+name+" ("+ex.Message+")",false); }
        }
        static void Assert(bool condition) { if(!condition)throw new Exception("assertion failed"); }
        static void NoLeaks(FakeClipboard p) { Assert(p.LocalHandleCount==0&&!p.IsOpen); }
        static FakeClipboard Original() {
            var p=new FakeClipboard();
            p.Seed(13,Encoding.Unicode.GetBytes("original secret\0"));
            p.Seed(2,new byte[]{10,20,30,40});
            p.Seed(0xC234,new byte[]{0,5,6,0,9});
            return p;
        }
        static int Main() {
            int passed=0,failed=0;
            Run((name,ok)=> { if(ok)passed++;else {failed++;Console.WriteLine("FAIL "+name);} });
            Console.WriteLine("NativeEmojiClipboard offline checks: "+passed+" passed, "+failed+" failed. No system clipboard or UI access.");
            return failed==0?0:1;
        }

        sealed class FakeClipboard : NativeEmojiClipboard.IClipboardPlatform {
            sealed class HandleData { internal byte[] Bytes;internal bool SystemOwned; }
            readonly Dictionary<long,HandleData> handles=new Dictionary<long,HandleData>();
            readonly Dictionary<uint,long> current=new Dictionary<uint,long>();
            readonly List<uint> order=new List<uint>();
            long next=100;uint sequence=1;bool open,ownerIsOurs;
            int formatCalls,allocateCalls;
            internal int OpenCalls,EmptyCalls,PutCalls;
            internal uint FailDuplicateFormat,ThrowDuplicateFormat;
            internal int FailAllocateCall,FailPutCall,ThrowPutCall,FailEmptyCall;
            internal long DuplicateSizeOverride;
            internal bool ChangeSequenceOnDuplicate,ChangeSecondFormats,FailNextOpen,PadOwnerRead;
            internal bool SynthesizeOnNextClose,ForceZeroSequence;
            internal Action BeforeNextOpen,AfterNextClose;
            public uint OwnerFormat { get {return 0xC001;} }
            public uint PrivacyFormat { get {return 0xC002;} }
            // GetClipboardSequenceNumber also works while the clipboard is closed.
            public uint Sequence { get {return ForceZeroSequence?0:sequence;} }
            public bool IsOwner { get {RequireOpen();return ownerIsOurs;} }
            internal bool IsOpen {get {return open;} }
            internal int LocalHandleCount {get {return handles.Values.Count(item=>!item.SystemOwned);} }
            internal uint[] CurrentFormats {get {return order.ToArray();} }
            internal string Text {get {return current.ContainsKey(13)?Encoding.Unicode.GetString(Peek(13)).TrimEnd('\0'):null;} }
            internal byte[] Peek(uint format) {return (byte[])handles[current[format]].Bytes.Clone();}
            internal void Seed(uint format,byte[] bytes) {
                if(open)throw new Exception("external mutation while clipboard locked");
                long handle=NewHandle(bytes,true);current.Add(format,handle);order.Add(format);sequence++;ownerIsOurs=false;
            }
            internal Dictionary<uint,byte[]> CopyData() {return current.Keys.ToDictionary(key=>key,key=>Peek(key));}
            internal bool SameData(Dictionary<uint,byte[]> expected) {return expected.Count==current.Count&&expected.All(pair=>current.ContainsKey(pair.Key)&&pair.Value.SequenceEqual(Peek(pair.Key)));}
            internal void ExternalCopy(string text) {
                if(open)throw new Exception("external mutation while clipboard locked");
                ClearSystemHandles();Seed(13,Encoding.Unicode.GetBytes(text+"\0"));
            }
            internal void ExternalCopyWithOwnerToken(string text) {
                var marker=Peek(OwnerFormat);ExternalCopy(text);Seed(OwnerFormat,marker);
            }
            internal void CorruptMarker() {handles[current[OwnerFormat]].Bytes[0]^=1;}
            internal void AdvanceSequence() {sequence++;}
            public bool Open() {
                OpenCalls++;var before=BeforeNextOpen;BeforeNextOpen=null;if(before!=null)before();
                if(FailNextOpen) {FailNextOpen=false;return false;}
                if(open)throw new Exception("nested Open");open=true;return true;
            }
            public void Close() {
                RequireOpen();open=false;
                if(SynthesizeOnNextClose) {
                    SynthesizeOnNextClose=false;
                    current.Add(16,NewHandle(new byte[]{9,4,0,0},true));order.Add(16);sequence++;
                }
                var after=AfterNextClose;AfterNextClose=null;if(after!=null)after();
            }
            public IList<uint> Formats() {
                RequireOpen();formatCalls++;var formats=order.ToList();
                if(ChangeSecondFormats&&formatCalls==2)formats.Reverse();return formats;
            }
            public NativeEmojiClipboard.ClipboardItem Duplicate(uint format) {
                RequireOpen();if(format==ThrowDuplicateFormat)throw new Exception("duplicate failed");if(format==FailDuplicateFormat)return null;
                byte[] bytes=Peek(format);if(ChangeSequenceOnDuplicate)sequence++;
                return new NativeEmojiClipboard.ClipboardItem{Format=format,Handle=new IntPtr(NewHandle(bytes,false)),Size=DuplicateSizeOverride!=0?DuplicateSizeOverride:bytes.Length};
            }
            public NativeEmojiClipboard.ClipboardItem Allocate(uint format,byte[] bytes) {
                RequireOpen();allocateCalls++;if(allocateCalls==FailAllocateCall)return null;
                return new NativeEmojiClipboard.ClipboardItem{Format=format,Handle=new IntPtr(NewHandle(bytes,false)),Size=bytes.Length};
            }
            public byte[] Read(uint format,int maximumBytes) {
                RequireOpen();if(!current.ContainsKey(format))return null;var bytes=Peek(format);
                if(PadOwnerRead&&format==OwnerFormat)bytes=bytes.Concat(new byte[16]).ToArray();
                return bytes.Length<=maximumBytes?bytes:null;
            }
            public bool Empty() {
                RequireOpen();EmptyCalls++;if(EmptyCalls==FailEmptyCall)return false;ClearSystemHandles();ownerIsOurs=true;return true;
            }
            public bool Put(NativeEmojiClipboard.ClipboardItem item) {
                RequireOpen();PutCalls++;if(PutCalls==ThrowPutCall)throw new Exception("put failed");if(PutCalls==FailPutCall)return false;
                long handle=item.Handle.ToInt64();Assert(handle!=0&&handles.ContainsKey(handle)&&!handles[handle].SystemOwned);
                if(current.ContainsKey(item.Format))handles.Remove(current[item.Format]);else order.Add(item.Format);
                handles[handle].SystemOwned=true;current[item.Format]=handle;item.Handle=IntPtr.Zero;sequence++;return true;
            }
            public void Release(NativeEmojiClipboard.ClipboardItem item) {
                if(item==null||item.Handle==IntPtr.Zero)return;long handle=item.Handle.ToInt64();
                Assert(handles.ContainsKey(handle)&&!handles[handle].SystemOwned);handles.Remove(handle);item.Handle=IntPtr.Zero;
            }
            long NewHandle(byte[] bytes,bool system) {long handle=next++;handles.Add(handle,new HandleData{Bytes=(byte[])bytes.Clone(),SystemOwned=system});return handle;}
            void ClearSystemHandles() {foreach(long handle in current.Values) {Assert(handles[handle].SystemOwned);handles.Remove(handle);}current.Clear();order.Clear();sequence++;}
            void RequireOpen() {if(!open)throw new Exception("operation without clipboard lock");}
        }
    }
}
