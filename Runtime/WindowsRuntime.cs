using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace WeChatPet.Runtime {
    public static class Win {
        [StructLayout(LayoutKind.Sequential)] public struct ScreenRect {public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out ScreenRect rect);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        public static bool PhysicalBounds(IntPtr hwnd,out ScreenRect rect) {
            IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
            try{return GetWindowRect(hwnd,out rect);}finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
        }
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint t);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int k);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint a,bool i,int p);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll",SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr p,IntPtr a,byte[] b,UIntPtr n,out UIntPtr r);
        [DllImport("kernel32.dll",SetLastError=true)] public static extern bool WriteProcessMemory(IntPtr p,IntPtr a,byte[] b,UIntPtr n,out UIntPtr r);
        [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
        public static void SameUser(Process p) {
            if(p.SessionId != Process.GetCurrentProcess().SessionId) throw new InvalidOperationException("wrong_windows_session");
            IntPtr token;
            if(!OpenProcessToken(p.Handle,8,out token)) throw new InvalidOperationException("user_check_failed");
            try { using(var identity = new WindowsIdentity(token))
                if(identity.User != WindowsIdentity.GetCurrent().User) throw new InvalidOperationException("wrong_windows_user"); }
            finally { CloseHandle(token); }
        }
        public static byte[] Read(IntPtr p,long address,int length) {
            byte[] b = new byte[length]; UIntPtr read;
            if(!ReadProcessMemory(p,new IntPtr(address),b,new UIntPtr((uint)length),out read)||read.ToUInt64()!=(ulong)length) throw new IOException("native_read_failed"); return b;
        }
    }
    internal static class TargetIdentity {
        public static bool IsCurrent(int pid,long birth,long hwnd) {
            if(pid<=0||birth<=0||hwnd==0)return false;
            try {
                using(var p=Process.GetProcessById(pid)) {
                    if(p.HasExited||p.StartTime.ToUniversalTime().Ticks!=birth)return false;
                    uint owner;
                    return Win.GetWindowThreadProcessId(new IntPtr(hwnd),out owner)!=0&&owner==pid;
                }
            }catch{return false;}
        }
    }
    public static class BuildIdentity {
        public const string Hash = "10f8e995453e2da46d4f2b5080cd6da1f13cc5147746adc119ceae38cb039de5";
        static FileStream held; static string verifiedPath;
        static readonly object sync = new object();
        public static void VerifyFile(string path) {
            lock(sync) {
                if(held!=null && String.Equals(path,verifiedPath,StringComparison.OrdinalIgnoreCase)) return;
                if(held!=null) { held.Dispose(); held=null; }
                var file = File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                try {
                    if(file.Length!=201552944) throw new InvalidOperationException("unsupported_wechat_build");
                    using(var sha=SHA256.Create()) if(BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant()!=Hash) throw new InvalidOperationException("unsupported_wechat_build");
                    held=file; verifiedPath=path;
                } catch { file.Dispose(); throw; }
            }
        }
        public static ProcessModule Bind(int pid, long birth, long hwnd) {
            var p=Process.GetProcessById(pid); Win.SameUser(p);
            uint owner; Win.GetWindowThreadProcessId(new IntPtr(hwnd),out owner);
            if(p.StartTime.ToUniversalTime().Ticks!=birth || p.ProcessName!="Weixin" || owner!=pid) throw new InvalidOperationException("target_identity_changed");
            var matches=p.Modules.Cast<ProcessModule>().Where(m=>m.ModuleName.Equals("Weixin.dll",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1) throw new InvalidOperationException("wechat_module_missing");
            VerifyFile(matches[0].FileName);
            NativeMessageReader.Configure(p.MainModule.FileName,matches[0].FileName);
            QtCompositionGate.Configure(p.MainModule.FileName,matches[0].FileName);
            return matches[0];
        }
    }
    // Separate process: stdin EOF or missed heartbeats restore only the byte this lease owns.
    public static class AccessibilityGuardian {
        public static int Run(int pid,long birth,long hwnd) {
            IntPtr handle=IntPtr.Zero; bool owned=false; long address=0;
            try {
                var module=BuildIdentity.Bind(pid,birth,hwnd);
                handle=Win.OpenProcess(0x438,false,pid);
                if(handle==IntPtr.Zero) throw new IOException("lease_access_denied");
                if(BitConverter.ToString(Win.Read(handle,module.BaseAddress.ToInt64()+0x849bcb,13)).Replace("-","").ToLowerInvariant()!="803d66c08e0a000f84ea060000") throw new InvalidOperationException("lease_signature_mismatch");
                address=module.BaseAddress.ToInt64()+0xb135c38;
                byte original=Win.Read(handle,address,1)[0];
                if(original>1) throw new InvalidOperationException("lease_state_unknown");
                if(original==0) { owned=true; Write(handle,address,1); }
                Console.WriteLine("ready"); Console.Out.Flush();
                while(true) {
                    if(!GuardianHeartbeat.WaitForNext(Console.In)) break;
                    if(Win.Read(handle,address,1)[0]!=1) break;
                }
                return 0;
            } catch(Exception error) { Console.WriteLine(ObserverFailureGate.GuardianFailure(error)); return 1; }
            finally {
                if(handle!=IntPtr.Zero) {
                    try { if(owned && Win.Read(handle,address,1)[0]==1) { Write(handle,address,0); Console.WriteLine("restored"); } }
                    catch { Console.WriteLine("restore_unconfirmed"); }
                    Win.CloseHandle(handle);
                }
            }
        }
        static void Write(IntPtr p,long a,byte value) {
            UIntPtr n;
            if(!Win.WriteProcessMemory(p,new IntPtr(a),new[]{value},new UIntPtr(1),out n)||n.ToUInt64()!=1||Win.Read(p,a,1)[0]!=value) throw new IOException("lease_write_failed");
        }
    }
}
