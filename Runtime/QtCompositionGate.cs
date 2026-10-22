// Build-specific read-only Qt IME evidence, extracted from the validated round5 probe.
// Cache owns no handles; Sample opens QUERY_INFORMATION|VM_READ and closes in finally.
// Not an input control, UIA provider, or standalone permission to intercept Tab.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

public static class QtCompositionGate {
    static string DllPath;
    static string ExePath; public static void Configure(string exe,string dll){ExePath=exe;DllPath=dll;}
    const string ExpectedHash = "10f8e995453e2da46d4f2b5080cd6da1f13cc5147746adc119ceae38cb039de5";
    const long GlobalRva = 0xB923700, ContextVtableRva = 0x92C6BB8;
    const long MaxUserPointer = 0x00007FFFFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr length, out UIntPtr read);
    [DllImport("kernel32.dll", SetLastError=true)] static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out Mbi info, UIntPtr length);
    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
    [DllImport("psapi.dll", SetLastError=true)] static extern bool EnumProcessModulesEx(IntPtr process, IntPtr[] modules, uint bytes, out uint needed, uint filter);
    [DllImport("psapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern uint GetModuleFileNameEx(IntPtr process, IntPtr module, StringBuilder path, int size);
    [DllImport("psapi.dll", SetLastError=true)] static extern bool GetModuleInformation(IntPtr process, IntPtr module, out ModuleInfo info, uint size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", SetLastError=true)] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", SetLastError=true)] static extern bool GetGUIThreadInfo(uint tid, ref GuiInfo info);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);

    [StructLayout(LayoutKind.Sequential)]
    struct Mbi {
        public ulong BaseAddress, AllocationBase;
        public uint AllocationProtect, Alignment1;
        public ulong RegionSize;
        public uint State, Protect, Type, Alignment2;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ModuleInfo { public IntPtr Base; public uint Size; public IntPtr Entry; }
    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct GuiInfo {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }
    sealed class ProbeFailure : Exception {
        public ProbeFailure(string reason) : base(reason) {}
    }
    sealed class Chain {
        public long Integration, IntegrationVtable, Private, Context, ContextVtable, Window;
        public byte Flag;
        public bool Same(Chain other) {
            return other != null && Integration == other.Integration &&
                IntegrationVtable == other.IntegrationVtable && Private == other.Private &&
                Context == other.Context && ContextVtable == other.ContextVtable &&
                Window == other.Window && Flag == other.Flag;
        }
    }
    sealed class WindowState {
        public long Foreground, ForegroundRoot, Focus, FocusRoot, Active, Layout;
        public uint ForegroundPid, ForegroundTid, ExpectedPid, ExpectedTid, FocusPid, FocusTid, GuiFlags;
        public bool ExpectedValid, GuiOk;
        public bool Same(WindowState other) {
            return other != null && Foreground == other.Foreground && ForegroundRoot == other.ForegroundRoot &&
                ForegroundPid == other.ForegroundPid && ForegroundTid == other.ForegroundTid &&
                Focus == other.Focus && FocusRoot == other.FocusRoot && FocusPid == other.FocusPid && FocusTid == other.FocusTid &&
                Active == other.Active && Layout == other.Layout &&
                ExpectedValid == other.ExpectedValid && ExpectedPid == other.ExpectedPid &&
                ExpectedTid == other.ExpectedTid && GuiOk == other.GuiOk && GuiFlags == other.GuiFlags;
        }
    }

    static string Hex(long value) { return "0x" + unchecked((ulong)value).ToString("x"); }
    static void Require(bool condition, string reason) { if (!condition) throw new ProbeFailure(reason); }
    static void Pointer(long value, string name) {
        Require(value >= 0x10000 && value <= MaxUserPointer && (value & 7) == 0, name + "_invalid_pointer");
    }
    static byte[] Read(IntPtr process, long address, int size) {
        Require(address >= 0x10000 && size > 0 && address <= MaxUserPointer - size + 1, "read_address_invalid");
        Mbi mbi;
        Require(VirtualQueryEx(process, new IntPtr(address), out mbi, new UIntPtr((uint)Marshal.SizeOf(typeof(Mbi)))).ToUInt64() != 0, "memory_query_failed");
        uint basic = mbi.Protect & 0xff;
        bool readable = basic == 2 || basic == 4 || basic == 8 || basic == 0x20 || basic == 0x40 || basic == 0x80;
        Require(mbi.State == 0x1000 && readable && (mbi.Protect & 0x100) == 0, "memory_not_readable");
        Require((ulong)address >= mbi.BaseAddress && (ulong)address - mbi.BaseAddress <= mbi.RegionSize &&
                (ulong)size <= mbi.RegionSize - ((ulong)address - mbi.BaseAddress), "read_crosses_region");
        byte[] buffer = new byte[size]; UIntPtr read;
        Require(ReadProcessMemory(process, new IntPtr(address), buffer, new UIntPtr((uint)size), out read) &&
                read.ToUInt64() == (ulong)size, "read_process_memory_failed");
        return buffer;
    }
    static long U64(IntPtr process, long address) { return BitConverter.ToInt64(Read(process, address, 8), 0); }
    static long ReadPointer(IntPtr process, long address, string name) {
        long value = U64(process, address); Pointer(value, name); return value;
    }
    static void Signature(IntPtr process, long moduleBase, uint moduleSize, long rva, byte[] expected) {
        Require(rva >= 0 && rva + expected.Length <= moduleSize, "signature_outside_module");
        byte[] actual = Read(process, moduleBase + rva, expected.Length);
        for (int i = 0; i < expected.Length; i++)
            Require(actual[i] == expected[i], "machine_code_mismatch_at_" + Hex(rva));
    }
    static Chain ReadChain(IntPtr process, long moduleBase, int targetPid) {
        Chain value = new Chain();
        value.Integration = ReadPointer(process, moduleBase + GlobalRva, "integration");
        value.IntegrationVtable = ReadPointer(process, value.Integration, "integration_vtable");
        Require(value.IntegrationVtable == moduleBase + 0x8F0BA08 || value.IntegrationVtable == moduleBase + 0x8DEC3E8, "integration_vtable_mismatch");
        value.Private = ReadPointer(process, value.Integration + 8, "private");
        value.Context = ReadPointer(process, value.Private + 0x90, "context");
        value.ContextVtable = ReadPointer(process, value.Context, "context_vtable");
        Require(value.ContextVtable == moduleBase + ContextVtableRva, "context_vtable_mismatch");
        value.Window = U64(process, value.Context + 0x20);
        value.Flag = Read(process, value.Context + 0x34, 1)[0];
        Require(value.Flag <= 1, "composition_flag_out_of_range");
        if (value.Window != 0) {
            uint owner;
            Require(IsWindow(new IntPtr(value.Window)), "context_window_invalid");
            uint tid = GetWindowThreadProcessId(new IntPtr(value.Window), out owner);
            Require(tid != 0 && owner == (uint)targetPid, "context_window_owner_mismatch");
        }
        Require(value.Flag != 1 || value.Window != 0, "active_without_context_window");
        return value;
    }
    static WindowState Windows(long expectedWindow) {
        WindowState state = new WindowState();
        IntPtr expected = new IntPtr(expectedWindow);
        state.ExpectedValid = expectedWindow != 0 && IsWindow(expected);
        if (state.ExpectedValid)
            state.ExpectedTid = GetWindowThreadProcessId(expected, out state.ExpectedPid);
        IntPtr foreground = GetForegroundWindow();
        state.Foreground = foreground.ToInt64();
        state.ForegroundRoot = GetAncestor(foreground, 2).ToInt64();
        state.ForegroundTid = GetWindowThreadProcessId(foreground, out state.ForegroundPid);
        if (state.ExpectedTid != 0) {
            GuiInfo gui = new GuiInfo(); gui.Size = (uint)Marshal.SizeOf(typeof(GuiInfo));
            state.GuiOk = GetGUIThreadInfo(state.ExpectedTid, ref gui);
            state.Layout = GetKeyboardLayout(state.ExpectedTid).ToInt64();
            if (state.GuiOk) {
                state.Focus = gui.Focus.ToInt64(); state.Active = gui.Active.ToInt64(); state.GuiFlags = gui.Flags;
                state.FocusRoot = GetAncestor(gui.Focus, 2).ToInt64();
                if (gui.Focus != IntPtr.Zero) state.FocusTid = GetWindowThreadProcessId(gui.Focus, out state.FocusPid);
            }
        }
        return state;
    }
    static Dictionary<string,object> WindowEvidence(WindowState value) {
        return new Dictionary<string,object> {
            {"foregroundHwnd",value.Foreground},{"foregroundRootHwnd",value.ForegroundRoot},
            {"foregroundPid",value.ForegroundPid},{"foregroundThreadId",value.ForegroundTid},
            {"expectedWindowValid",value.ExpectedValid},{"expectedWindowPid",value.ExpectedPid},
            {"expectedWindowThreadId",value.ExpectedTid},{"guiInfoSuccess",value.GuiOk},
            {"focusHwnd",value.Focus},{"focusRootHwnd",value.FocusRoot},{"focusPid",value.FocusPid},
            {"focusThreadId",value.FocusTid},{"activeHwnd",value.Active},{"guiFlags",value.GuiFlags},
            {"layout",Hex(value.Layout)}
        };
    }
    static Dictionary<string,object> ChainEvidence(Chain value, long moduleBase) {
        uint owner = 0, tid = 0;
        if (value.Window != 0) tid = GetWindowThreadProcessId(new IntPtr(value.Window), out owner);
        return new Dictionary<string,object> {
            {"integration",Hex(value.Integration)},{"integrationVtableRva",Hex(value.IntegrationVtable-moduleBase)},
            {"private",Hex(value.Private)},{"context",Hex(value.Context)},{"contextVtableRva",Hex(value.ContextVtable-moduleBase)},
            {"contextHwnd",value.Window},{"contextWindowPid",owner},{"contextWindowThreadId",tid},
            {"rawFlag",value.Flag}
        };
    }
    static string DiskHash() { WeChatPet.Runtime.BuildIdentity.VerifyFile(DllPath); return ExpectedHash; }
    static ModuleInfo FindModule(IntPtr process) {
        StringBuilder exe = new StringBuilder(32768); int size = exe.Capacity;
        Require(QueryFullProcessImageName(process, 0, exe, ref size), "process_image_query_failed");
        Require(String.Equals(Path.GetFullPath(exe.ToString()), ExePath, StringComparison.OrdinalIgnoreCase), "process_image_path_mismatch");
        IntPtr[] modules = new IntPtr[2048]; uint needed;
        Require(EnumProcessModulesEx(process, modules, (uint)(modules.Length*IntPtr.Size), out needed, 3), "module_enumeration_failed");
        Require(needed <= modules.Length*IntPtr.Size && needed % IntPtr.Size == 0, "module_enumeration_truncated");
        IntPtr found = IntPtr.Zero; int matches = 0;
        for (int i = 0; i < needed/IntPtr.Size; i++) {
            StringBuilder path = new StringBuilder(32768);
            uint length = GetModuleFileNameEx(process, modules[i], path, path.Capacity);
            if (length == 0) continue;
            Require(length < path.Capacity-1, "module_path_truncated");
            if (String.Equals(path.ToString(), DllPath, StringComparison.OrdinalIgnoreCase)) { found = modules[i]; matches++; }
        }
        Require(matches == 1, "exact_loaded_module_not_unique");
        ModuleInfo info;
        Require(GetModuleInformation(process, found, out info, (uint)Marshal.SizeOf(typeof(ModuleInfo))), "module_info_failed");
        Pointer(info.Base.ToInt64(), "module_base");
        Require(GlobalRva + 8 <= info.Size && ContextVtableRva + 8 <= info.Size, "module_size_mismatch");
        return info;
    }

    sealed class CacheEntry {
        public int Pid;
        public ulong Created;
        public ModuleInfo Module;
        public long Length, WriteTicks, CreationTicks, VerifiedAt;
    }
    struct FileStamp {
        public long Length, WriteTicks, CreationTicks;
        public bool Same(FileStamp other) { return Length == other.Length && WriteTicks == other.WriteTicks && CreationTicks == other.CreationTicks; }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct NativeFileTime { public uint Low, High; public ulong Value { get { return ((ulong)High << 32) | Low; } } }
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetProcessTimes(IntPtr process, out NativeFileTime created, out NativeFileTime exited, out NativeFileTime kernel, out NativeFileTime user);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    static readonly object CacheLock = new object();
    static CacheEntry cache;
    static readonly long RehashTicks = Stopwatch.Frequency * 30;
    const long MaxSampleMilliseconds = 200;

    // The cache contains metadata only. It never owns a process or file handle.
    public static void InvalidateCache() { lock (CacheLock) { cache = null; } }
    static ulong ProcessIdentity(IntPtr process) {
        uint code;
        Require(GetExitCodeProcess(process, out code) && code == 259, "target_process_not_running");
        NativeFileTime created, exited, kernel, user;
        Require(GetProcessTimes(process, out created, out exited, out kernel, out user), "process_identity_unavailable");
        return created.Value;
    }
    static FileStamp Stamp() {
        FileInfo info = new FileInfo(DllPath); info.Refresh();
        Require(info.Exists && info.Length == 201552944, "dll_length_mismatch");
        return new FileStamp { Length=info.Length, WriteTicks=info.LastWriteTimeUtc.Ticks, CreationTicks=info.CreationTimeUtc.Ticks };
    }
    static bool CachedModuleMatches(IntPtr process, CacheEntry entry) {
        StringBuilder path = new StringBuilder(32768);
        uint length = GetModuleFileNameEx(process, entry.Module.Base, path, path.Capacity);
        if (length == 0 || length >= path.Capacity-1 || !String.Equals(path.ToString(), DllPath, StringComparison.OrdinalIgnoreCase)) return false;
        ModuleInfo now;
        return GetModuleInformation(process, entry.Module.Base, out now, (uint)Marshal.SizeOf(typeof(ModuleInfo))) &&
            now.Base == entry.Module.Base && now.Size == entry.Module.Size && now.Entry == entry.Module.Entry;
    }
    static ModuleInfo VerifiedModule(IntPtr process, int pid, ulong created, Dictionary<string,object> result, bool allowRefresh) {
        long now = Stopwatch.GetTimestamp();
        if (!allowRefresh) {
            // No filesystem metadata, hashing, or full module enumeration on this path.
            Require(cache != null && cache.Pid == pid && cache.Created == created &&
                now >= cache.VerifiedAt && now-cache.VerifiedAt < RehashTicks &&
                CachedModuleMatches(process,cache), "version_cache_unavailable_or_expired");
            result["versionCacheHit"] = true;
            result["versionCacheAgeMs"] = (now-cache.VerifiedAt)*1000.0/Stopwatch.Frequency;
            return cache.Module;
        }
        FileStamp stamp = Stamp();
        bool reuse = cache != null && cache.Pid == pid && cache.Created == created &&
            cache.Length == stamp.Length && cache.WriteTicks == stamp.WriteTicks && cache.CreationTicks == stamp.CreationTicks &&
            now >= cache.VerifiedAt && now-cache.VerifiedAt < RehashTicks && CachedModuleMatches(process,cache);
        if (reuse) {
            result["versionCacheHit"] = true;
            result["versionCacheAgeMs"] = (now-cache.VerifiedAt)*1000.0/Stopwatch.Frequency;
            return cache.Module;
        }
        cache = null; result["versionCacheHit"] = false;
        ModuleInfo module = FindModule(process);
        DiskHash();
        FileStamp after = Stamp();
        Require(stamp.Same(after), "dll_changed_while_hashing");
        cache = new CacheEntry { Pid=pid, Created=created, Module=module, Length=after.Length,
            WriteTicks=after.WriteTicks, CreationTicks=after.CreationTicks, VerifiedAt=Stopwatch.GetTimestamp() };
        result["versionCacheAgeMs"] = 0;
        return module;
    }
    static void VerifyCode(IntPtr process, ModuleInfo module) {
        long moduleBase = module.Base.ToInt64();
        Signature(process,moduleBase,module.Size,0xC42C63,new byte[]{0x48,0x89,0x35,0x96,0x0A,0xCE,0x0A});
        Signature(process,moduleBase,module.Size,0xC42CBF,new byte[]{0x48,0xC7,0x05,0x36,0x0A,0xCE,0x0A,0,0,0,0});
        Signature(process,moduleBase,module.Size,0x25C2552,new byte[]{0xC6,0x46,0x34,0x01});
        Signature(process,moduleBase,module.Size,0x25C17DD,new byte[]{0xC6,0x46,0x34,0x00});
        Signature(process,moduleBase,module.Size,0x25C183E,new byte[]{0xC6,0x46,0x34,0x00});
    }
    static void CheckWindow(WindowState state, int pid, long hwnd) {
        Require(state.ExpectedValid && state.ExpectedPid == (uint)pid && state.ExpectedTid != 0, "expected_window_invalid_or_wrong_owner");
        Require(state.GuiOk && state.Focus != 0 && state.FocusPid == (uint)pid, "target_focus_unavailable");
        long root = GetAncestor(new IntPtr(hwnd),2).ToInt64();
        Require(root != 0 && state.ForegroundPid == (uint)pid && state.ForegroundRoot == root &&
                state.FocusRoot == root, "target_not_foreground_and_focused");
        Require(state.Layout != 0 && (state.GuiFlags & 0x1e) == 0, "layout_or_gui_state_uncertain");
    }
    static void Unknown(Dictionary<string,object> result, string reason) {
        result["valid"] = false; result["status"] = "unknown"; result["composition"] = "unknown";
        result["compositionState"] = "unknown"; result["reason"] = reason;
        result["imeAllowsFurtherTabChecks"] = false;
    }

    /// <summary>
    /// Call from the existing observation/worker path, never from a latency-critical keyboard hook.
    /// Returns Qt composition evidence only: the caller must still verify the actual chat input,
    /// candidate vetoes, draft/session/tail, activity generation, exact HKL, freshness and write context.
    /// inactive is not a write or Tab authorization. No UIA/chat text is read.
    /// </summary>
    public static Dictionary<string,object> Sample(long hwnd) { return SampleCore(hwnd,true); }

    /// <summary>
    /// Uses already verified build metadata only. Missing/expired cache fails closed.
    /// Does not hash files or enumerate all modules; direct native reads are still synchronous,
    /// so this is not a hard real-time guarantee. The caller must enforce its own timing policy.
    /// </summary>
    public static Dictionary<string,object> SampleCached(long hwnd) { return SampleCore(hwnd,false); }

    // Separate evidence model: normal Sample/SampleCached retain their original window policy.
    internal sealed class PasteMenuWindowState {
        internal long Target,TargetRoot,Popup,PopupRoot,PopupOwner,PopupRootOwner,Foreground,Focus,FocusRoot,Active,Layout,Capture,MenuOwner,MoveSize;
        internal uint TargetPid,TargetTid,PopupPid,PopupTid,ForegroundPid,ForegroundTid,FocusPid,FocusTid,GuiFlags;
        internal bool TargetValid,TargetVisible,TargetIconic,PopupValid,PopupVisible,PopupIconic,GuiOk;
        internal bool Same(PasteMenuWindowState other) {
            return other!=null&&Target==other.Target&&TargetRoot==other.TargetRoot&&Popup==other.Popup&&PopupRoot==other.PopupRoot&&PopupOwner==other.PopupOwner&&PopupRootOwner==other.PopupRootOwner
                &&Foreground==other.Foreground&&Focus==other.Focus&&FocusRoot==other.FocusRoot&&Active==other.Active&&Layout==other.Layout
                &&Capture==other.Capture&&MenuOwner==other.MenuOwner&&MoveSize==other.MoveSize&&TargetPid==other.TargetPid&&TargetTid==other.TargetTid
                &&PopupPid==other.PopupPid&&PopupTid==other.PopupTid&&ForegroundPid==other.ForegroundPid&&ForegroundTid==other.ForegroundTid
                &&FocusPid==other.FocusPid&&FocusTid==other.FocusTid&&GuiFlags==other.GuiFlags&&TargetValid==other.TargetValid
                &&TargetVisible==other.TargetVisible&&TargetIconic==other.TargetIconic&&PopupValid==other.PopupValid&&PopupVisible==other.PopupVisible
                &&PopupIconic==other.PopupIconic&&GuiOk==other.GuiOk;
        }
    }

    // Pure policy helpers are exercised without P/Invoke or reading any running application.
    internal static bool ValidatePasteMenuWindow(PasteMenuWindowState state,out string reason) {
        reason="paste_menu_target_invalid";
        if(state==null||state.Target<=0||!state.TargetValid||!state.TargetVisible||state.TargetIconic
            ||state.TargetRoot!=state.Target||state.TargetPid==0||state.TargetPid>Int32.MaxValue||state.TargetTid==0)return false;
        reason="paste_menu_popup_binding_invalid";
        if(state.Popup<=0||state.Popup==state.Target||state.PopupRoot!=state.Popup||!state.PopupValid||!state.PopupVisible||state.PopupIconic
            ||state.PopupPid!=state.TargetPid||state.PopupTid!=state.TargetTid
            ||(state.PopupOwner!=state.Target&&state.PopupRootOwner!=state.Target))return false;
        reason="paste_menu_foreground_invalid";
        if((state.Foreground!=state.Target&&state.Foreground!=state.Popup)||state.ForegroundPid!=state.TargetPid||state.ForegroundTid!=state.TargetTid)return false;
        reason="paste_menu_focus_invalid";
        bool targetFocus=state.FocusRoot==state.Target;
        bool popupFocus=state.Focus==state.Popup&&state.FocusRoot==state.Popup;
        if(!state.GuiOk||state.Focus<=0||(!targetFocus&&!popupFocus)||state.FocusPid!=state.TargetPid||state.FocusTid!=state.TargetTid
            ||(state.Active!=state.Target&&state.Active!=state.Popup))return false;
        reason="paste_menu_gui_state_uncertain";
        // GUI_CARETBLINKING, GUI_INMENUMODE, GUI_POPUPMENUMODE only. System menus are excluded.
        if(state.Layout==0||(state.GuiFlags&~0x15u)!=0||state.MoveSize!=0
            ||(state.Capture!=0&&state.Capture!=state.Popup)
            ||(state.MenuOwner!=0&&state.MenuOwner!=state.Target&&state.MenuOwner!=state.Popup)
            ||((state.GuiFlags&0x14u)!=0&&state.MenuOwner==0))return false;
        reason="ok";return true;
    }
    internal static bool PasteMenuContextBound(PasteMenuWindowState state,long context,long contextRoot,uint contextPid,uint contextTid,byte? compositionFlag) {
        if(state==null||state.Target<=0||state.TargetPid==0||state.TargetTid==0||!compositionFlag.HasValue||compositionFlag.Value>1)return false;
        // Qt's compositionContext.hwnd is cleared by doneContext(), not kept as the
        // focused input window. Zero is valid only with a positively read inactive flag.
        // The exact menu/target binding and both stable chain reads remain mandatory.
        if(context==0)return compositionFlag.Value==0&&contextRoot==0&&contextPid==0&&contextTid==0;
        return context>0&&contextRoot==state.Target&&contextPid==state.TargetPid&&contextTid==state.TargetTid;
    }

    static PasteMenuWindowState PasteMenuWindows(long target,long popup) {
        var state=new PasteMenuWindowState{Target=target,Popup=popup};
        IntPtr hwnd=new IntPtr(target),menu=new IntPtr(popup);
        state.TargetValid=target>0&&IsWindow(hwnd);state.PopupValid=popup>0&&IsWindow(menu);
        if(state.TargetValid) {
            state.TargetTid=GetWindowThreadProcessId(hwnd,out state.TargetPid);state.TargetRoot=GetAncestor(hwnd,2).ToInt64();
            state.TargetVisible=IsWindowVisible(hwnd);state.TargetIconic=IsIconic(hwnd);
        }
        if(state.PopupValid) {
            state.PopupTid=GetWindowThreadProcessId(menu,out state.PopupPid);state.PopupRoot=GetAncestor(menu,2).ToInt64();state.PopupOwner=GetWindow(menu,4).ToInt64();
            state.PopupRootOwner=GetAncestor(menu,3).ToInt64();state.PopupVisible=IsWindowVisible(menu);state.PopupIconic=IsIconic(menu);
        }
        IntPtr foreground=GetForegroundWindow();state.Foreground=foreground.ToInt64();
        state.ForegroundTid=GetWindowThreadProcessId(foreground,out state.ForegroundPid);
        if(state.TargetTid!=0) {
            GuiInfo gui=new GuiInfo{Size=(uint)Marshal.SizeOf(typeof(GuiInfo))};
            state.GuiOk=GetGUIThreadInfo(state.TargetTid,ref gui);state.Layout=GetKeyboardLayout(state.TargetTid).ToInt64();
            if(state.GuiOk) {
                state.Focus=gui.Focus.ToInt64();state.FocusRoot=GetAncestor(gui.Focus,2).ToInt64();
                if(gui.Focus!=IntPtr.Zero)state.FocusTid=GetWindowThreadProcessId(gui.Focus,out state.FocusPid);
                state.Active=gui.Active.ToInt64();state.GuiFlags=gui.Flags;state.Capture=gui.Capture.ToInt64();
                state.MenuOwner=gui.MenuOwner.ToInt64();state.MoveSize=gui.MoveSize.ToInt64();
            }
        }
        return state;
    }
    static void CheckPasteMenuWindow(PasteMenuWindowState state) {
        string reason;Require(ValidatePasteMenuWindow(state,out reason),reason);
    }
    static void CheckPasteMenuContext(PasteMenuWindowState state,Chain chain) {
        uint pid=0,tid=0;long root=0;
        if(chain.Window!=0&&IsWindow(new IntPtr(chain.Window))) {
            tid=GetWindowThreadProcessId(new IntPtr(chain.Window),out pid);root=GetAncestor(new IntPtr(chain.Window),2).ToInt64();
        }
        Require(PasteMenuContextBound(state,chain.Window,root,pid,tid,chain.Flag),"paste_menu_context_not_bound_to_target");
    }
    static Dictionary<string,object> PasteMenuEvidence(PasteMenuWindowState value) {
        return new Dictionary<string,object> {
            {"target",value.Target},{"targetRoot",value.TargetRoot},{"targetPid",value.TargetPid},{"targetTid",value.TargetTid},
            {"popup",value.Popup},{"popupRoot",value.PopupRoot},{"popupOwner",value.PopupOwner},{"popupRootOwner",value.PopupRootOwner},{"popupPid",value.PopupPid},{"popupTid",value.PopupTid},
            {"foreground",value.Foreground},{"foregroundPid",value.ForegroundPid},{"foregroundTid",value.ForegroundTid},
            {"focus",value.Focus},{"focusRoot",value.FocusRoot},{"focusPid",value.FocusPid},{"focusTid",value.FocusTid},
            {"active",value.Active},{"layout",value.Layout},{"guiFlags",value.GuiFlags},{"capture",value.Capture},{"menuOwner",value.MenuOwner},{"moveSize",value.MoveSize},
            {"targetValid",value.TargetValid},{"targetVisible",value.TargetVisible},{"targetIconic",value.TargetIconic},
            {"popupValid",value.PopupValid},{"popupVisible",value.PopupVisible},{"popupIconic",value.PopupIconic},{"guiOk",value.GuiOk}
        };
    }

    /// <summary>
    /// Cached read-only IME sample for a specific native paste popup already verified by the caller.
    /// Does not discover or authorize a menu. Requires the exact same-process/thread owned popup,
    /// a verified Qt composition context (inactive with no window, or bound to the target),
    /// and stable window/layout evidence before and after the memory read.
    /// Unknown evidence must veto paste; neither an absent Composing field nor false defaults prove inactivity.
    /// </summary>
    public static Dictionary<string,object> SampleForPasteMenu(long target,long observedOwnedPopup) {
        Stopwatch timer=Stopwatch.StartNew();
        var result=new Dictionary<string,object> {
            {"capturedAt",DateTime.UtcNow.ToString("o")},{"valid",false},{"status","unknown"},{"composition","unknown"},
            {"compositionState","unknown"},{"reason","not_measured"},{"source","qt_windows_input_context_4.1.15.13"},
            {"readOnly",true},{"readsInputText",false},{"usesOcr",false},{"changesInput",false},{"uiAutomationUsed",false},
            {"imeAllowsFurtherTabChecks",false},{"tabDecision","pass_through"},{"tabEligibility","not_evaluated"},
            {"requiresChatInputAndContextValidation",true},{"chatInputVerifiedByThisProbe",false},{"candidateState","not_measured"},
            {"candidateAbsenceProvesInactive",false},{"layoutIsCompositionEvidence",false},{"sampleStaleForTab",true},
            {"processHandleOpened",false},{"processHandleClosed",false},{"cleanupComplete",false},{"versionCacheHit",false},
            {"dllSha256",ExpectedHash},{"cacheRefreshAllowed",false},{"pasteMenuPolicy",true},{"observedOwnedPopup",observedOwnedPopup}
        };
        IntPtr process=IntPtr.Zero;bool lockHeld=false;
        try {
            Require(IntPtr.Size==8,"host_not_x64");Require(target>0&&observedOwnedPopup>0,"paste_menu_window_required");
            lockHeld=Monitor.TryEnter(CacheLock);Require(lockHeld,"probe_busy");
            PasteMenuWindowState before=PasteMenuWindows(target,observedOwnedPopup);result["windowsBefore"]=PasteMenuEvidence(before);CheckPasteMenuWindow(before);
            int pid=(int)before.TargetPid;
            result["expectedWindowHandle"]=target;result["targetProcessId"]=pid;
            result["foregroundHandle"]=before.Foreground;result["foregroundProcessId"]=before.ForegroundPid;
            result["foregroundThreadId"]=before.ForegroundTid;result["nativeFocusHandle"]=before.Focus;
            process=OpenProcess(0x410,false,pid);Require(process!=IntPtr.Zero,"open_process_failed_"+Marshal.GetLastWin32Error());
            result["processHandleOpened"]=true;ulong created=ProcessIdentity(process);
            ModuleInfo module=VerifiedModule(process,pid,created,result,false);VerifyCode(process,module);result["machineCodeVerified"]=true;
            Chain first=ReadChain(process,module.Base.ToInt64(),pid);
            result["rawFlagFirst"]=first.Flag;result["contextHwndFirst"]=first.Window;result["contextAddressFirst"]=Hex(first.Context);
            result["compositionReadCount"]=1;CheckPasteMenuContext(before,first);
            Chain second=ReadChain(process,module.Base.ToInt64(),pid);
            result["rawFlag"]=second.Flag;result["contextHwnd"]=second.Window;result["contextAddress"]=Hex(second.Context);
            result["compositionReadCount"]=2;result["chainStable"]=first.Same(second);CheckPasteMenuContext(before,second);
            PasteMenuWindowState after=PasteMenuWindows(target,observedOwnedPopup);result["windowsAfter"]=PasteMenuEvidence(after);
            result["focusStable"]=before.Same(after);CheckPasteMenuWindow(after);CheckPasteMenuContext(after,second);
            result["layout"]=Hex(after.Layout);result["keyboardLayout"]=Hex(after.Layout);
            result["keyboardLayoutValue"]=after.Layout;result["chainStable"]=first.Same(second);result["focusStable"]=before.Same(after);
            Require(first.Same(second),"chain_or_flag_changed_during_sample");
            Require(before.Same(after),"paste_menu_window_binding_changed_during_sample");
            Require(ProcessIdentity(process)==created,"process_identity_changed");
            Require(CachedModuleMatches(process,cache),"module_changed_during_sample");
            Require(timer.ElapsedMilliseconds<=MaxSampleMilliseconds,"sample_stale");
            bool active=second.Flag!=0;result["valid"]=true;result["status"]="valid";
            result["composition"]=active?"active":"inactive";result["compositionState"]=active?"active_reported":"inactive_reported";
            result["reason"]=active?"qt_composition_active":"qt_composition_inactive";result["imeAllowsFurtherTabChecks"]=!active;
            result["isWeChatForeground"]=true;result["sampledAtMonotonicTicks"]=Stopwatch.GetTimestamp();return result;
        } catch(ProbeFailure failure) {
            Unknown(result,failure.Message);
            if(lockHeld&&failure.Message!="sample_stale"&&!failure.Message.StartsWith("paste_menu_",StringComparison.Ordinal))cache=null;
            return result;
        } catch(Exception failure) {
            Unknown(result,"probe_error");result["errorType"]=failure.GetType().Name;
            result["errorHresult"]="0x"+unchecked((uint)failure.HResult).ToString("X8");if(lockHeld)cache=null;return result;
        } finally {
            if(process!=IntPtr.Zero) {
                bool closed=CloseHandle(process);result["processHandleClosed"]=closed;result["cleanupComplete"]=closed;
                if(!closed) {result["cleanupError"]=Marshal.GetLastWin32Error();Unknown(result,"process_handle_close_failed");if(lockHeld)cache=null;}
            } else result["cleanupComplete"]=true;
            if(lockHeld)Monitor.Exit(CacheLock);timer.Stop();result["elapsedMs"]=timer.ElapsedMilliseconds;
            result["sampleStaleForTab"]=timer.ElapsedMilliseconds>MaxSampleMilliseconds;
            if(timer.ElapsedMilliseconds>MaxSampleMilliseconds)Unknown(result,"sample_stale");
        }
    }

    static Dictionary<string,object> SampleCore(long hwnd, bool allowRefresh) {
        Stopwatch timer = Stopwatch.StartNew();
        var result = new Dictionary<string,object> {
            {"capturedAt",DateTime.UtcNow.ToString("o")},{"valid",false},{"status","unknown"},
            {"composition","unknown"},{"compositionState","unknown"},{"reason","not_measured"},
            {"source","qt_windows_input_context_4.1.15.13"},{"readOnly",true},{"readsInputText",false},
            {"usesOcr",false},{"changesInput",false},{"uiAutomationUsed",false},
            {"imeAllowsFurtherTabChecks",false},{"tabDecision","pass_through"},{"tabEligibility","not_evaluated"},
            {"requiresChatInputAndContextValidation",true},{"chatInputVerifiedByThisProbe",false},
            {"candidateState","not_measured"},{"candidateAbsenceProvesInactive",false},
            {"layoutIsCompositionEvidence",false},{"sampleStaleForTab",true},
            {"processHandleOpened",false},{"processHandleClosed",false},{"cleanupComplete",false},
            {"versionCacheHit",false},{"dllSha256",ExpectedHash},{"cacheRefreshAllowed",allowRefresh}
        };
        IntPtr process = IntPtr.Zero; bool lockHeld = false;
        try {
            Require(IntPtr.Size == 8, "host_not_x64");
            Require(hwnd > 0, "expected_window_required");
            lockHeld = Monitor.TryEnter(CacheLock);
            Require(lockHeld, "probe_busy");
            WindowState before = Windows(hwnd);
            Require(before.ExpectedValid && before.ExpectedPid != 0 && before.ExpectedPid <= Int32.MaxValue, "expected_window_invalid");
            int pid = (int)before.ExpectedPid;
            CheckWindow(before,pid,hwnd);
            result["foregroundHandle"] = before.Foreground;
            result["foregroundProcessId"] = before.ForegroundPid;
            result["foregroundThreadId"] = before.ForegroundTid;
            result["nativeFocusHandle"] = before.Focus;
            result["expectedWindowHandle"] = hwnd;
            result["targetProcessId"] = pid;
            process = OpenProcess(0x410,false,pid); // QUERY_INFORMATION | VM_READ; never write/create-thread rights.
            Require(process != IntPtr.Zero, "open_process_failed_" + Marshal.GetLastWin32Error());
            result["processHandleOpened"] = true;
            ulong created = ProcessIdentity(process);
            ModuleInfo module = VerifiedModule(process,pid,created,result,allowRefresh);
            VerifyCode(process,module); result["machineCodeVerified"] = true;
            Chain first = ReadChain(process,module.Base.ToInt64(),pid);
            Chain second = ReadChain(process,module.Base.ToInt64(),pid);
            WindowState after = Windows(hwnd);
            result["windowsBefore"] = WindowEvidence(before); result["windowsAfter"] = WindowEvidence(after);
            result["rawFlagFirst"] = first.Flag; result["rawFlag"] = second.Flag;
            result["contextHwnd"] = second.Window;
            result["contextAddress"] = Hex(second.Context);
            result["layout"] = Hex(after.Layout); result["keyboardLayout"] = Hex(after.Layout);
            result["keyboardLayoutValue"] = after.Layout;
            result["chainStable"] = first.Same(second);
            result["focusStable"] = before.Same(after);
            Require(first.Same(second), "chain_or_flag_changed_during_sample");
            Require(before.Same(after), "window_binding_changed_during_sample");
            CheckWindow(after,pid,hwnd);
            if (second.Window != 0)
                Require(GetAncestor(new IntPtr(second.Window),2) == GetAncestor(new IntPtr(hwnd),2), "context_window_binding_mismatch");
            Require(ProcessIdentity(process) == created, "process_identity_changed");
            Require(CachedModuleMatches(process,cache), "module_changed_during_sample");
            Require(timer.ElapsedMilliseconds <= MaxSampleMilliseconds, "sample_stale");
            bool active = second.Flag != 0;
            result["valid"] = true; result["status"] = "valid";
            result["composition"] = active ? "active" : "inactive";
            result["compositionState"] = active ? "active_reported" : "inactive_reported";
            result["reason"] = active ? "qt_composition_active" : "qt_composition_inactive";
            // This only permits the caller to continue checking its own UIA/draft/session/activity rules.
            result["imeAllowsFurtherTabChecks"] = !active;
            result["isWeChatForeground"] = true;
            result["sampledAtMonotonicTicks"] = Stopwatch.GetTimestamp();
            return result;
        } catch (ProbeFailure failure) {
            Unknown(result,failure.Message);
            // Focus loss, contention and cold-cache latency do not invalidate a verified DLL identity.
            if (lockHeld && failure.Message != "sample_stale" &&
                failure.Message != "target_not_foreground_and_focused" && failure.Message != "target_focus_unavailable" &&
                failure.Message != "layout_or_gui_state_uncertain" && failure.Message != "window_binding_changed_during_sample")
                cache = null;
            return result;
        } catch (Exception failure) {
            Unknown(result,"probe_error");
            result["errorType"] = failure.GetType().Name;
            result["errorHresult"] = "0x" + unchecked((uint)failure.HResult).ToString("X8");
            if (lockHeld) cache = null;
            return result;
        } finally {
            if (process != IntPtr.Zero) {
                bool closed = CloseHandle(process);
                result["processHandleClosed"] = closed; result["cleanupComplete"] = closed;
                if (!closed) {
                    result["cleanupError"] = Marshal.GetLastWin32Error();
                    Unknown(result,"process_handle_close_failed");
                    if (lockHeld) cache = null;
                }
            } else result["cleanupComplete"] = true;
            if (lockHeld) Monitor.Exit(CacheLock);
            timer.Stop(); result["elapsedMs"] = timer.ElapsedMilliseconds;
            result["sampleStaleForTab"] = timer.ElapsedMilliseconds > MaxSampleMilliseconds;
            if (timer.ElapsedMilliseconds > MaxSampleMilliseconds) Unknown(result,"sample_stale");
        }
    }
}
