using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace WeChatPet.Runtime {
    // A separate installation key: runtime message tags intentionally remain ephemeral.
    public sealed class MemoryIdentity {
        readonly byte[] key;
        public MemoryIdentity(string directory) {
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"memory-key.dpapi");
            using(var gate=new Mutex(false,"Local\\WeChatPet.MemoryKey."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)) {
                bool entered=false;
                try {
                    try {entered=gate.WaitOne(5000);}catch(AbandonedMutexException){entered=true;}
                    if(!entered)throw new IOException("memory_key_busy");
                    if(!File.Exists(path)) {
                        // Never silently replace a lost key while existing memories remain.
                        if(File.Exists(Path.Combine(directory,"memory.dpapi")))throw new IOException("memory_key_missing");
                        var bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);
                        LocalStore.Write(path,Convert.ToBase64String(bytes),true);
                    }
                    key=Convert.FromBase64String(LocalStore.Read(path,true));
                    if(key.Length!=32)throw new IOException("memory_key_invalid");
                }finally{if(entered)gate.ReleaseMutex();}
            }
        }
        string Tag(params string[] parts) {
            var text=new StringBuilder("wechat-memory-v1");
            foreach(string part in parts)text.Append('|').Append(part.Length).Append(':').Append(part);
            using(var h=new HMACSHA256(key))return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","").ToLowerInvariant();
        }
        public string Account(string account){return Tag("account",account);}
        public string Friend(string account,string friend){return Tag("friend",account,friend);}
        public string Group(string account,string group){return Tag("group",account,group);}
        public string GroupMember(string account,string group,string member){return Tag("group-member",account,group,member);}
        public string Message(string account,string friend,string message){return Tag("message",account,friend,message);}
        public static bool Valid(string id){return id!=null&&System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-f0-9]{64}$");}
    }
}
