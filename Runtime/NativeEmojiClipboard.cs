using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WeChatPet.Runtime {
    // Contains no input injection. The caller owns the exact, once-only paste invocation.
    // Unknown invocation outcomes discard the original snapshot; Status reports that loss.
    internal static class NativeEmojiClipboard {
        static readonly Lazy<ClipboardApartment> apartment = new Lazy<ClipboardApartment>(() => new ClipboardApartment());
        static readonly ActiveLeaseSlot production = new ActiveLeaseSlot();
        internal const int PublicationDeadlineMilliseconds=3500;
        const int CleanupRetryMilliseconds=100;

        internal static Lease Begin(string text) {
            if(production.IsClosed)return Lease.Failed("clipboard_worker_stopping");
            try {
                var worker = apartment.Value;
                return production.Begin(worker.Platform,worker.Run,Schedule,text);
            } catch { return Lease.Failed("clipboard_worker_unavailable"); }
        }

        // Does not initialize the apartment. Call before the observer exits while its STA
        // still exists. A foreign clipboard lock can prevent cleanup; report that outcome.
        internal static string CancelActive() {
            return production.CloseAndCancel(5,() => Thread.Sleep(CleanupRetryMilliseconds));
        }

        static IDisposable Schedule(Action action,int milliseconds) {
            return new System.Threading.Timer(state => { try { action(); } catch { } },null,milliseconds,Timeout.Infinite);
        }

        // The identity check prevents a late completion from releasing a newer lease.
        // Tests instantiate a separate slot with an in-memory platform and manual timers.
        internal sealed class ActiveLeaseSlot {
            readonly object gate=new object();
            Lease active;
            bool closed;
            internal bool HasActive { get { lock(gate)return active!=null; } }
            internal bool IsClosed { get { lock(gate)return closed; } }
            internal Lease Begin(IClipboardPlatform platform,Action<Action> dispatch,Func<Action,int,IDisposable> schedule,string text) {
                Lease lease;
                lock(gate) {
                    if(closed)return Lease.Failed("clipboard_worker_stopping");
                    if(active!=null)return Lease.Failed("clipboard_transaction_busy");
                    lease=new Lease(new ClipboardTransaction(platform),dispatch,schedule,Release);
                    active=lease;
                }
                // Begin can still wait for an existing owner's delayed rendering. The
                // deadline starts only after publication; it is not a hard Begin timeout.
                try { lease.Start(text);return lease; }
                catch {
                    try { lease.Complete(false); } catch { }
                    return Lease.Failed("clipboard_worker_unavailable");
                }
            }
            void Release(Lease lease) { lock(gate)if(Object.ReferenceEquals(active,lease))active=null; }
            internal string CancelActive() { return CancelActive(0,null); }
            internal string CancelActive(int retries,Action pause) {
                return Cancel(false,retries,pause);
            }
            internal string CloseAndCancel(int retries,Action pause) {
                return Cancel(true,retries,pause);
            }
            string Cancel(bool close,int retries,Action pause) {
                Lease lease;lock(gate) { if(close)closed=true;lease=active; }
                if(lease==null)return "no_active_clipboard_transaction";
                lease.Complete(false);
                for(int attempt=0;attempt<retries&&!lease.Finished;attempt++) {
                    if(pause!=null)pause();
                    lease.Complete(false);
                }
                return lease.Status;
            }
        }

        // This entry point never initializes the production apartment or any Windows API.
        internal static Lease BeginForTests(IClipboardPlatform platform, string text) {
            var lease = new Lease(new ClipboardTransaction(platform), action => action(), null, null);
            lease.Start(text);
            return lease;
        }

        internal sealed class Lease : IDisposable {
            readonly ClipboardTransaction transaction;
            readonly Action<Action> dispatch;
            readonly Func<Action,int,IDisposable> schedule;
            readonly object lifecycle=new object();
            Action<Lease> release;
            IDisposable deadline,cleanupRetry;
            bool closing,deadlineElapsed;
            string dispatchFailure;
            readonly string failed;
            internal Lease(ClipboardTransaction transaction, Action<Action> dispatch, Func<Action,int,IDisposable> schedule, Action<Lease> release) {
                this.transaction=transaction; this.dispatch=dispatch; this.schedule=schedule; this.release=release;
            }
            Lease(string reason) { failed=reason; }
            internal static Lease Failed(string reason) { return new Lease(reason); }
            internal bool CanInvoke { get { lock(lifecycle)return transaction!=null&&!closing&&transaction.CanInvoke; } }
            internal bool InvocationStarted { get { return transaction!=null && transaction.InvocationStarted; } }
            internal bool Restored { get { return transaction!=null && transaction.Restored; } }
            internal bool Isolated { get { return transaction!=null && transaction.Isolated; } }
            internal bool Finished { get { return transaction==null || transaction.Finished; } }
            internal bool DeadlineElapsed { get { lock(lifecycle)return deadlineElapsed; } }
            // Diagnostics contain no clipboard text, marker bytes, window handles or sequences.
            internal bool? SequenceChanged { get { return transaction==null ? null : transaction.SequenceChanged; } }
            internal bool? OwnerTokenMatches { get { return transaction==null ? null : transaction.OwnerTokenMatches; } }
            internal bool? OwnerWindowMatches { get { return transaction==null ? null : transaction.OwnerWindowMatches; } }
            internal string Status { get { lock(lifecycle)return transaction==null ? failed : (!Finished&&dispatchFailure!=null?dispatchFailure:transaction.Status); } }
            internal void Start(string text) {
                lock(lifecycle) {
                    dispatch(() => transaction.Begin(text));
                    ReleaseIfFinished();
                    if(!Finished&&schedule!=null) {
                        var timer=schedule(Expire,PublicationDeadlineMilliseconds);
                        if(Finished||closing)timer.Dispose();else deadline=timer;
                    }
                }
            }
            internal bool MarkInvocationStarted() {
                if(transaction==null)return false;
                lock(lifecycle) {
                    if(closing)return false;
                    bool result=false; dispatch(() => result=transaction.MarkInvocationStarted()); ReleaseIfFinished(); return result;
                }
            }
            void Expire() {
                lock(lifecycle) {
                    if(Finished)return;
                    deadlineElapsed=true;Complete(false);
                }
            }
            internal void Complete(bool verified) {
                if(transaction==null)return;
                lock(lifecycle) {
                    if(Finished) { ReleaseIfFinished();return; }
                    closing=true;
                    CancelTimer(ref deadline);CancelTimer(ref cleanupRetry);
                    try { dispatch(() => transaction.Complete(verified));dispatchFailure=null; }
                    catch { dispatchFailure="cleanup_dispatch_unavailable_pending"; }
                    finally {
                        ReleaseIfFinished();
                        // A foreign lock must not silently discard the snapshot or release
                        // the slot while our temporary publication is still outstanding.
                        if(!Finished&&schedule!=null) {
                            var timer=schedule(() => Complete(false),CleanupRetryMilliseconds);
                            if(Finished)timer.Dispose();else cleanupRetry=timer;
                        }
                    }
                }
            }
            static void CancelTimer(ref IDisposable timer) { var value=timer;timer=null;if(value!=null)value.Dispose(); }
            void ReleaseIfFinished() {
                if(!Finished)return;
                CancelTimer(ref deadline);CancelTimer(ref cleanupRetry);
                var callback=release;release=null;if(callback!=null)callback(this);
            }
            public void Dispose() { Complete(false); }
        }

        internal sealed class ClipboardItem {
            internal uint Format;
            internal IntPtr Handle;
            internal long Size;
        }

        internal interface IClipboardPlatform {
            uint OwnerFormat { get; }
            uint PrivacyFormat { get; }
            bool Open();
            void Close();
            uint Sequence { get; }
            bool IsOwner { get; }
            IList<uint> Formats();
            ClipboardItem Duplicate(uint format);
            ClipboardItem Allocate(uint format, byte[] data);
            byte[] Read(uint format, int maximumBytes);
            bool Empty();
            // Successful Put transfers ownership and zeroes item.Handle.
            bool Put(ClipboardItem item);
            void Release(ClipboardItem item);
        }

        internal sealed class ClipboardTransaction {
            const int MaxFormats=128;
            const long MaxSnapshotBytes=32L*1024*1024;
            readonly IClipboardPlatform platform;
            readonly object gate=new object();
            readonly List<ClipboardItem> snapshot=new List<ClipboardItem>();
            byte[] marker;
            uint installedSequence;
            bool prepared,invoked,confirmed,finished,restored,isolated;
            bool? sequenceChanged,ownerTokenMatches,ownerWindowMatches;
            string status="not_started";
            internal ClipboardTransaction(IClipboardPlatform platform) { this.platform=platform; }
            internal bool CanInvoke { get { lock(gate)return prepared&&!invoked&&!finished; } }
            internal bool InvocationStarted { get { lock(gate)return invoked; } }
            internal bool Finished { get { lock(gate)return finished; } }
            internal bool Restored { get { lock(gate)return restored; } }
            internal bool Isolated { get { lock(gate)return isolated; } }
            internal string Status { get { lock(gate)return status; } }
            internal bool? SequenceChanged { get { lock(gate)return sequenceChanged; } }
            internal bool? OwnerTokenMatches { get { lock(gate)return ownerTokenMatches; } }
            internal bool? OwnerWindowMatches { get { lock(gate)return ownerWindowMatches; } }

            internal void Begin(string text) {
                lock(gate) {
                    if(prepared||finished)return;
                    if(String.IsNullOrEmpty(text)||text.Length>32768||text.IndexOf('\0')>=0) { End("invalid_text");return; }
                    bool opened=false,changed=false;
                    var temporary=new List<ClipboardItem>();
                    try {
                        if(!platform.Open()) { End("clipboard_busy");return; }
                        opened=true;
                        uint originalSequence=platform.Sequence;
                        var formats=platform.Formats();
                        if(formats==null||formats.Count>MaxFormats||formats.Distinct().Count()!=formats.Count) { End("snapshot_formats_unavailable");return; }
                        long total=0;
                        foreach(uint format in formats) {
                            var item=platform.Duplicate(format);
                            if(item==null||item.Handle==IntPtr.Zero) { End("snapshot_format_unsupported");return; }
                            snapshot.Add(item);total+=item.Size;
                            if(item.Size<=0||total>MaxSnapshotBytes) { End("snapshot_too_large");return; }
                        }
                        var finalFormats=platform.Formats();
                        if(platform.Sequence!=originalSequence||finalFormats==null||!formats.SequenceEqual(finalFormats)) { End("snapshot_changed");return; }
                        marker=Guid.NewGuid().ToByteArray();
                        temporary.Add(platform.Allocate(platform.OwnerFormat,marker));
                        // Windows' documented flag prevents reply text entering clipboard history/cloud sync.
                        temporary.Add(platform.Allocate(platform.PrivacyFormat,new byte[]{1}));
                        temporary.Add(platform.Allocate(13,Encoding.Unicode.GetBytes(text+"\0")));
                        if(temporary.Any(item=>item==null||item.Handle==IntPtr.Zero)) { End("temporary_allocation_failed");return; }
                        if(!platform.Empty()) { End("clipboard_empty_failed");return; }
                        changed=true;
                        foreach(var item in temporary) {
                            if(!platform.Put(item)) {
                                restored=RestoreLocked(); End(restored?"publish_failed_restored":"publish_failed_restore_failed");return;
                            }
                        }
                        prepared=true;status="prepared";
                    } catch {
                        if(changed) { try { restored=RestoreLocked(); } catch { } }
                        End(changed?(restored?"prepare_exception_restored":"prepare_exception_restore_failed"):"snapshot_failed");
                    } finally {
                        foreach(var item in temporary)if(item!=null)platform.Release(item);
                        if(opened)platform.Close();
                    }
                    // Sample only after CloseClipboard commits the publication. Windows can
                    // synthesize formats when closing (e.g. CF_LOCALE for CF_TEXT), and the
                    // sequence API describes changes, not a per-SetClipboardData version.
                    // A copy racing this sample is still rejected by the owner window + token.
                    // https://learn.microsoft.com/en-us/windows/win32/dataxchg/standard-clipboard-formats
                    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber
                    if(prepared&&!finished)installedSequence=platform.Sequence;
                }
            }

            internal bool MarkInvocationStarted() {
                lock(gate) {
                    if(!prepared||invoked||finished)return false;
                    bool opened=false;
                    try {
                        if(!platform.Open()) { status="invocation_clipboard_busy";return false; }
                        opened=true;
                        if(!OwnedLocked()) { End("clipboard_replaced_before_invocation");return false; }
                        // Set before calling external UIA code, including when that call throws.
                        invoked=true;status="invocation_started";return true;
                    } catch { status="invocation_ownership_unavailable";return false; }
                    finally { if(opened)platform.Close(); }
                }
            }

            internal void Complete(bool verified) {
                lock(gate) {
                    if(finished)return;
                    if(!prepared) { End("not_prepared");return; }
                    if(verified&&invoked)confirmed=true;
                    bool opened=false;
                    try {
                        if(!platform.Open()) { status="cleanup_clipboard_busy_pending";return; }
                        opened=true;
                        if(!OwnedLocked()) { End("clipboard_replaced_not_restored");return; }
                        if(!invoked||confirmed) {
                            bool restoreStarted;
                            restored=RestoreLocked(out restoreStarted);
                            if(!restored&&!restoreStarted) { status="restore_pending_not_restored";return; }
                            End(restored?(invoked?"verified_restored":"not_invoked_restored"):"restore_failed");
                        } else {
                            // No timer-based restoration: an unresolved invocation must not see old data.
                            isolated=platform.Empty();
                            if(isolated)End("unverified_isolated_not_restored");
                            else status="isolation_pending_not_restored";
                        }
                    } catch { status="cleanup_exception_pending"; }
                    finally { if(opened)platform.Close(); }
                }
            }

            bool OwnedLocked() {
                uint currentSequence=platform.Sequence;
                sequenceChanged=currentSequence!=installedSequence;
                ownerWindowMatches=platform.IsOwner;
                var current=platform.Read(platform.OwnerFormat,64);
                // GlobalSize may include zero-filled allocation padding beyond the 16-byte token.
                ownerTokenMatches=current!=null&&marker!=null&&current.Length>=marker.Length
                    &&current.Take(marker.Length).SequenceEqual(marker)&&current.Skip(marker.Length).All(value=>value==0);
                return installedSequence!=0&&currentSequence!=0&&sequenceChanged==false
                    &&ownerWindowMatches==true&&ownerTokenMatches==true;
            }
            bool RestoreLocked() {
                bool restoreStarted;return RestoreLocked(out restoreStarted);
            }
            bool RestoreLocked(out bool restoreStarted) {
                restoreStarted=false;
                try { if(!platform.Empty())return false;restoreStarted=true; } catch { return false; }
                bool all=true;
                // Each handle gets only one publication attempt. Never empty again after partial restore.
                foreach(var item in snapshot) {
                    try { if(item.Handle!=IntPtr.Zero&&!platform.Put(item))all=false; }
                    catch { all=false; }
                }
                return all;
            }
            void End(string reason) {
                status=reason;finished=true;prepared=false;
                foreach(var item in snapshot)platform.Release(item);
                snapshot.Clear();marker=null;
            }
        }

        sealed class ClipboardApartment {
            readonly ManualResetEvent started=new ManualResetEvent(false);
            readonly Thread thread;
            Control dispatcher;
            Exception startupError;
            internal IClipboardPlatform Platform;
            internal ClipboardApartment() {
                thread=new Thread(ThreadMain) { IsBackground=true,Name="NativeEmojiClipboard" };
                thread.SetApartmentState(ApartmentState.STA);thread.Start();started.WaitOne();
                if(startupError!=null)throw new InvalidOperationException("Clipboard apartment unavailable",startupError);
            }
            void ThreadMain() {
                try {
                    dispatcher=new Control();
                    IntPtr hwnd=dispatcher.Handle; // Hidden window, never Show()/Activate().
                    Platform=new Win32ClipboardPlatform(hwnd);
                } catch(Exception ex) { startupError=ex; }
                finally { started.Set(); }
                if(startupError==null) {
                    try { Application.Run(new ApplicationContext()); }
                    finally { dispatcher.Dispose(); }
                }
            }
            internal void Run(Action action) {
                if(Thread.CurrentThread==thread)action(); else dispatcher.Invoke(action);
            }
        }

        sealed class Win32ClipboardPlatform : IClipboardPlatform {
            const long MaxItemBytes=16L*1024*1024;
            readonly IntPtr hwnd;
            readonly uint ownerFormat,privacyFormat;
            internal Win32ClipboardPlatform(IntPtr hwnd) {
                this.hwnd=hwnd;
                ownerFormat=RegisterClipboardFormat("WeChatPet.NativeEmojiClipboard.Owner.v1");
                privacyFormat=RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
                if(ownerFormat==0||privacyFormat==0)throw new Win32Exception();
            }
            public uint OwnerFormat { get { return ownerFormat; } }
            public uint PrivacyFormat { get { return privacyFormat; } }
            public bool Open() { return OpenClipboard(hwnd); }
            public void Close() { CloseClipboard(); }
            public uint Sequence { get { return GetClipboardSequenceNumber(); } }
            public bool IsOwner { get { return GetClipboardOwner()==hwnd; } }
            public IList<uint> Formats() {
                var list=new List<uint>();uint previous=0;
                while(true) {
                    SetLastError(0);uint next=EnumClipboardFormats(previous);
                    if(next==0) { if(Marshal.GetLastWin32Error()!=0)throw new Win32Exception();break; }
                    list.Add(next);if(list.Count>128)throw new InvalidOperationException("Too many clipboard formats");previous=next;
                }
                return list;
            }
            public ClipboardItem Duplicate(uint format) {
                // Owner-display/private handles cannot be restored with transferable ownership.
                // Enhanced metafiles are intentionally rejected: OleDuplicateData only documents
                // special handle cloning for BITMAP, PALETTE and METAFILEPICT.
                bool memory=format==1||format==7||format==8||format==10||format==11||format==12||format==13||format==15||format==16||format==17||format>=0xC000;
                if(!memory&&format!=2&&format!=3&&format!=9)return null;
                IntPtr source=GetClipboardData(format);if(source==IntPtr.Zero)return null;
                long size;
                if(format==2) {
                    BITMAP bitmap;
                    if(GetObject(source,Marshal.SizeOf(typeof(BITMAP)),out bitmap)==0)return null;
                    size=Math.Abs((long)bitmap.Height)*Math.Abs((long)bitmap.WidthBytes);
                } else if(format==9) {
                    uint count=GetPaletteEntries(source,0,0,IntPtr.Zero);if(count==0)return null;size=4L*count;
                } else {
                    size=(long)GlobalSize(source).ToUInt64();
                    if(format==3) {
                        if(size<Marshal.SizeOf(typeof(METAFILEPICT)))return null;
                        IntPtr data=GlobalLock(source);if(data==IntPtr.Zero)return null;
                        try {
                            IntPtr metafile=((METAFILEPICT)Marshal.PtrToStructure(data,typeof(METAFILEPICT))).Metafile;
                            if(metafile==IntPtr.Zero)return null;
                            uint bytes=GetMetaFileBitsEx(metafile,0,IntPtr.Zero);if(bytes==0)return null;
                            size+=bytes;
                        }
                        finally { GlobalUnlock(source); }
                    }
                }
                if(size<=0||size>MaxItemBytes)return null;
                // SetClipboardData requires movable global memory for copied memory formats.
                IntPtr copy=OleDuplicateData(source,(ushort)format,0x0002);
                if(copy==IntPtr.Zero||copy==source)return null;
                return new ClipboardItem{Format=format,Handle=copy,Size=size};
            }
            public ClipboardItem Allocate(uint format,byte[] data) {
                IntPtr handle=GlobalAlloc(0x0042,new UIntPtr((uint)data.Length));
                if(handle==IntPtr.Zero)return null;
                IntPtr address=GlobalLock(handle);
                if(address==IntPtr.Zero) { GlobalFree(handle);return null; }
                try { Marshal.Copy(data,0,address,data.Length); }
                catch { GlobalUnlock(handle);GlobalFree(handle);throw; }
                GlobalUnlock(handle);
                return new ClipboardItem{Format=format,Handle=handle,Size=data.Length};
            }
            public byte[] Read(uint format,int maximumBytes) {
                IntPtr handle=GetClipboardData(format);if(handle==IntPtr.Zero)return null;
                ulong size=GlobalSize(handle).ToUInt64();if(size==0||size>(ulong)maximumBytes)return null;
                IntPtr address=GlobalLock(handle);if(address==IntPtr.Zero)return null;
                try { var data=new byte[(int)size];Marshal.Copy(address,data,0,data.Length);return data; }
                finally { GlobalUnlock(handle); }
            }
            public bool Empty() { return EmptyClipboard(); }
            public bool Put(ClipboardItem item) {
                if(item.Handle==IntPtr.Zero)return false;
                if(SetClipboardData(item.Format,item.Handle)==IntPtr.Zero)return false;
                item.Handle=IntPtr.Zero;return true;
            }
            public void Release(ClipboardItem item) {
                if(item==null||item.Handle==IntPtr.Zero)return;
                IntPtr handle=item.Handle;item.Handle=IntPtr.Zero;
                if(item.Format==2||item.Format==9) { DeleteObject(handle);return; }
                if(item.Format==3) {
                    IntPtr address=GlobalLock(handle);
                    if(address!=IntPtr.Zero) {
                        try { DeleteMetaFile(((METAFILEPICT)Marshal.PtrToStructure(address,typeof(METAFILEPICT))).Metafile); }
                        finally { GlobalUnlock(handle); }
                    }
                }
                GlobalFree(handle);
            }
            [StructLayout(LayoutKind.Sequential)] struct BITMAP { internal int Type,Width,Height,WidthBytes;internal ushort Planes,BitsPixel;internal IntPtr Bits; }
            [StructLayout(LayoutKind.Sequential)] struct METAFILEPICT { internal int MappingMode,XExtent,YExtent;internal IntPtr Metafile; }
            [DllImport("user32.dll",SetLastError=true)] static extern bool OpenClipboard(IntPtr hwnd);
            [DllImport("user32.dll",SetLastError=true)] static extern bool CloseClipboard();
            [DllImport("user32.dll",SetLastError=true)] static extern bool EmptyClipboard();
            [DllImport("user32.dll",SetLastError=true)] static extern uint EnumClipboardFormats(uint format);
            [DllImport("user32.dll",SetLastError=true)] static extern IntPtr GetClipboardData(uint format);
            [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetClipboardData(uint format,IntPtr data);
            [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
            [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
            [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint RegisterClipboardFormat(string name);
            [DllImport("ole32.dll")] static extern IntPtr OleDuplicateData(IntPtr source,ushort format,uint flags);
            [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr GlobalAlloc(uint flags,UIntPtr bytes);
            [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr GlobalLock(IntPtr handle);
            [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr handle);
            [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr handle);
            [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr handle);
            [DllImport("kernel32.dll")] static extern void SetLastError(uint error);
            [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
            [DllImport("gdi32.dll")] static extern bool DeleteMetaFile(IntPtr handle);
            [DllImport("gdi32.dll")] static extern uint GetMetaFileBitsEx(IntPtr handle,uint size,IntPtr data);
            [DllImport("gdi32.dll")] static extern uint GetPaletteEntries(IntPtr handle,uint start,uint count,IntPtr data);
            [DllImport("gdi32.dll",EntryPoint="GetObjectW")] static extern int GetObject(IntPtr handle,int size,out BITMAP bitmap);
        }
    }
}
