using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WeChatPet.Runtime {
    public sealed class RuntimeSettings {
        public string Provider="chatgpt", Model="", Device=Guid.NewGuid().ToString();
        public bool Paused, Validated;
        public string ReasoningEffort="auto";
        public Dictionary<string,string> ModelsByProvider=new Dictionary<string,string>();
        public int SelectedProcess;
        public long SelectedProcessBirth;
    }
    public static class LocalStore {
        public static readonly string Data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WeChatPet");
        public static void Write(string path,string content,bool secret) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));byte[] bytes=Encoding.UTF8.GetBytes(content);
            if(secret)bytes=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);
            string tmp=path+".tmp";File.WriteAllBytes(tmp,bytes);
            if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
        }
        public static string Read(string path,bool secret) {
            byte[] bytes=File.ReadAllBytes(path);if(secret)bytes=ProtectedData.Unprotect(bytes,null,DataProtectionScope.CurrentUser);return Encoding.UTF8.GetString(bytes);
        }
        internal const string SettingsReadFailure="设置文件无法读取，原文件已保留，推荐已停止。请解除文件占用或检查文件内容、目录权限后重启桌宠。";
        public static RuntimeSettings Load() {string error;return Load(out error);}
        public static RuntimeSettings Load(out string error) {return LoadSettings(Path.Combine(Data,"runtime.json"),out error);}
        internal static RuntimeSettings LoadSettings(string path,out string error) {
            error=null;
            try {
                var value=new JavaScriptSerializer().Deserialize<RuntimeSettings>(Read(path,false));
                Guid device;
                if(value==null||value.Model==null||value.ReasoningEffort==null||value.ModelsByProvider==null||
                    (value.Provider!="chatgpt"&&value.Provider!="deepseek-account"&&value.Provider!="deepseek-api")||!Guid.TryParseExact(value.Device,"D",out device))throw new InvalidDataException();
                return value;
            }catch(FileNotFoundException){return new RuntimeSettings();}
             catch(DirectoryNotFoundException){return new RuntimeSettings();}
             catch {error=SettingsReadFailure;return new RuntimeSettings();}
        }
        public static void Save(RuntimeSettings settings) { Write(Path.Combine(Data,"runtime.json"),new JavaScriptSerializer().Serialize(settings),false); }
    }
    public sealed class ModelClient : IDisposable {
        readonly object sync=new object(), writer=new object();readonly string vaultPath;
        readonly Dictionary<string,TaskCompletionSource<object>> requests=new Dictionary<string,TaskCompletionSource<object>>();
        Dictionary<string,object> credentials=new Dictionary<string,object>();Process process;bool closed;
        public event Action<string,string> AuthorizationUrl;
        public event Action CredentialsChanged;
        public Task Initialization { get; private set; }
        public string RecoveryNotice { get; private set; }
        public bool HasCredential(string provider) { lock(sync)return credentials.ContainsKey(provider); }
        public ModelClient(string home,RuntimeSettings settings,string isolatedData=null) {
            string data=isolatedData??LocalStore.Data;Directory.CreateDirectory(data);
            vaultPath=Path.Combine(data,"credentials.dpapi");
            string recovery;credentials=LoadCredentials(vaultPath,out recovery);RecoveryNotice=recovery;
            var psi=new ProcessStartInfo {FileName=Path.Combine(home,"runtime","node.exe"),Arguments="--use-env-proxy \""+Path.Combine(home,"runtime","model-worker.mjs")+"\"",WorkingDirectory=data,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false)};
            psi.EnvironmentVariables.Clear();
            foreach(string key in new[]{"SystemRoot","TEMP","TMP","USERPROFILE","LOCALAPPDATA","HTTP_PROXY","HTTPS_PROXY","NO_PROXY"}) {string value=Environment.GetEnvironmentVariable(key);if(value!=null)psi.EnvironmentVariables[key]=value;}
            process=new Process {StartInfo=psi,EnableRaisingEvents=true};
            process.OutputDataReceived+=Output;process.ErrorDataReceived+=delegate { /* Provider stderr is intentionally not persisted. */ };
            process.Exited+=delegate { FailAll("模型服务已停止，请重启桌宠"); };
            process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
            Initialization=Request("init",new Dictionary<string,object>{{"device",settings.Device},{"credentials",credentials}});
        }
        internal static Dictionary<string,object> LoadCredentials(string path,out string recovery) {
            recovery=null;byte[] encrypted;
            // Missing is a fresh installation. Access/locking/other I/O failures
            // must remain failures; they are not evidence of a corrupt vault.
            try {encrypted=File.ReadAllBytes(path);}catch(FileNotFoundException){return new Dictionary<string,object>();}
            try {
                if(encrypted.Length>1024*1024)throw new InvalidDataException();
                string text=new UTF8Encoding(false,true).GetString(ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser));
                var value=new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(text) as Dictionary<string,object>;
                if(!ValidCredentials(value))throw new InvalidDataException();
                return value;
            }catch(CryptographicException) { }
             catch(DecoderFallbackException) { }
             catch(ArgumentException) { }
             catch(InvalidOperationException) { }
             catch(InvalidDataException) { }
            string backup=path+".unreadable-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".bak";
            try {File.Move(path,backup);}
            catch(IOException e){throw new InvalidOperationException("CREDENTIAL_BACKUP_FAILED",e);}
            catch(UnauthorizedAccessException e){throw new InvalidOperationException("CREDENTIAL_BACKUP_FAILED",e);}
            recovery="原连接信息无法读取，已保留备份，请重新连接。";
            return new Dictionary<string,object>();
        }
        static bool CredentialText(Dictionary<string,object> value,string name,int max=32768) {
            object raw;return value.TryGetValue(name,out raw)&&raw is string&&!String.IsNullOrWhiteSpace((string)raw)&&((string)raw).Length<=max;
        }
        static bool ValidCredentials(Dictionary<string,object> value) {
            if(value==null)return false;
            foreach(var pair in value) {
                var fields=pair.Value as Dictionary<string,object>;if(fields==null)return false;
                if(pair.Key=="chatgpt") {
                    object expires,idToken;
                    if(!CredentialText(fields,"access")||!CredentialText(fields,"refresh")||!CredentialText(fields,"clientId",512)||!fields.TryGetValue("expires",out expires)||!(expires is int||expires is long||expires is decimal||expires is double))return false;
                    double timestamp=Convert.ToDouble(expires);if(Double.IsNaN(timestamp)||Double.IsInfinity(timestamp)||timestamp<0)return false;
                    if(fields.TryGetValue("idToken",out idToken)&&idToken!=null&&(!(idToken is string)||((string)idToken).Length>32768))return false;
                }else if(pair.Key=="deepseek-account") {
                    object issuer;if(!CredentialText(fields,"token")||!fields.TryGetValue("issuer",out issuer)||!(issuer is string)||(string)issuer!="https://platform.deepseek.com")return false;
                }else if(pair.Key=="deepseek-api") {
                    if(!CredentialText(fields,"key",512)||!System.Text.RegularExpressions.Regex.IsMatch((string)fields["key"],@"^[\x21-\x7e]{10,512}$"))return false;
                }else return false;
            }
            return true;
        }
        void Output(object sender,DataReceivedEventArgs e) {
            if(e.Data==null||e.Data.Length>262144)return;
            try {
                var json=new JavaScriptSerializer {MaxJsonLength=262144};var v=json.Deserialize<Dictionary<string,object>>(e.Data);
                object kind;
                if(v.TryGetValue("type",out kind)) {
                    string type=Convert.ToString(kind);
                    if(type=="auth_url") {
                        string provider=Convert.ToString(v["provider"]),url=Convert.ToString(v["url"]);Uri parsed;
                        if(Uri.TryCreate(url,UriKind.Absolute,out parsed)&&parsed.Scheme=="https"&&((provider=="chatgpt"&&parsed.Host=="auth.openai.com"&&parsed.AbsolutePath=="/api/accounts/authorize")||(provider=="deepseek-account"&&parsed.Host=="platform.deepseek.com"&&parsed.AbsolutePath=="/dsh/authorize"))) {
                            var handler=AuthorizationUrl;if(handler!=null)handler(provider,url);
                        }
                    }
                    if(type=="credential") {
                        bool ok=false;
                        try {lock(sync) {
                            string provider=Convert.ToString(v["provider"]);
                            if(provider!="chatgpt"&&provider!="deepseek-account"&&provider!="deepseek-api")throw new IOException();
                            var next=new Dictionary<string,object>(credentials);
                            if(v["value"]==null)next.Remove(provider);else next[provider]=v["value"];
                            if(!ValidCredentials(next))throw new InvalidDataException();
                            LocalStore.Write(vaultPath,json.Serialize(next),true);credentials=next;RecoveryNotice=null;ok=true;
                        }}catch { }
                        Send(new{op="vaultAck",id=v["id"],ok=ok});
                        if(ok&&CredentialsChanged!=null)CredentialsChanged();
                    }
                    return;
                }
                TaskCompletionSource<object> pending;
                lock(sync) {string id=Convert.ToString(v["id"]);if(!requests.TryGetValue(id,out pending))return;requests.Remove(id);}
                if(Convert.ToBoolean(v["ok"]))pending.TrySetResult(v.ContainsKey("result")?v["result"]:null);
                else pending.TrySetException(new InvalidOperationException(Convert.ToString(v["code"])));
            }catch { FailAll("模型通信异常"); }
        }
        void Send(object value) {lock(writer) {if(closed)throw new ObjectDisposedException("ModelClient");byte[] bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=18*1024*1024}.Serialize(value)+"\n");process.StandardInput.BaseStream.Write(bytes,0,bytes.Length);process.StandardInput.BaseStream.Flush();}}
        public async Task<object> Request(string op,Dictionary<string,object> fields=null) {
            var c=fields==null?new Dictionary<string,object>():new Dictionary<string,object>(fields);
            string id=Guid.NewGuid().ToString("N");c["id"]=id;c["op"]=op;
            var pending=new TaskCompletionSource<object>();lock(sync)requests.Add(id,pending);
            try {
                Send(c);
                var winner=await Task.WhenAny(pending.Task,Task.Delay(op=="auth/start"?310000:40000)).ConfigureAwait(false);
                if(winner!=pending.Task) {Dispose();throw new TimeoutException("MODEL_WORKER_TIMEOUT");}
                return await pending.Task.ConfigureAwait(false);
            } finally {lock(sync)requests.Remove(id);}
        }
        public void Cancel() {var ignored=Request("cancel");ignored.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        void FailAll(string message) {
            TaskCompletionSource<object>[] all;
            lock(sync) {all=new List<TaskCompletionSource<object>>(requests.Values).ToArray();requests.Clear();}
            foreach(var p in all)p.TrySetException(new IOException(message));
        }
        public void Dispose() {
            if(closed)return;
            try {Send(new{op="shutdown"});}catch { }closed=true;
            if(process!=null) {try {process.StandardInput.Close();if(!process.WaitForExit(1500))process.Kill();}catch { }process.Dispose();}
            FailAll("模型服务已停止");
        }
        public static string Explain(Exception e) {
            string code=e.Message;
            if(code=="CREDENTIAL_BACKUP_FAILED")return "连接信息无法备份，原文件已保留；请检查应用数据目录权限后重启。";
            if(e is UnauthorizedAccessException)return "无法访问应用数据目录，请从已安装的桌宠启动";
            if(e is IOException)return "模型服务未启动或已停止，请重启桌宠";
            if(code.Contains("AUTH")||code.Contains("401"))return "登录已失效，请重新连接账号";
            if(code.Contains("QUOTA")||code.Contains("402"))return "当前账号额度不足";
            if(code=="OUTPUT_LIMIT")return "回复超过16000字符，请缩小本次回复范围";
            if(code=="VISION_UNSUPPORTED")return "当前模型不支持图片，请在模型连接中选择支持图片的模型";
            if(code=="INVALID_IMAGE"||code=="IMAGE_LIMIT")return "图片未能读取或超过限制，请使用4MB以内的普通图片";
            if(code=="INCOMPLETE_OUTPUT")return "模型未完整生成回复，请重试";
            if(code=="GROUP_LATEST_TEXT_REQUIRED")return "最新群消息不是文字，暂不生成回复";
            if(code=="GROUP_SENDER_REQUIRED"||code=="GROUP_SENDER_CONFLICT"||code=="INVALID_GROUP_SENDER_NAME")return "群成员身份尚未确认，暂不生成回复";
            if(code=="GROUP_MEMORY_UNSUPPORTED")return "群聊不能使用私聊记忆，请重新打开群聊后再试";
            if(code=="INVALID_GROUP_CONTEXT"||code=="INVALID_CONTEXT_MODE")return "当前聊天内容尚未确认，请稍后重新点击宠物";
            if(code.Contains("RATE_LIMIT"))return "请求过于频繁，请稍后再试";
            if(code.Contains("CANCEL"))return "操作已取消";
            if(code.Contains("PLATFORM_REJECTED")||code.Contains("ACCESS_DENIED"))return "平台未接受这次授权，请查看账号或服务可用范围";
            if(code.Contains("TIMEOUT"))return "连接超时，请稍后重试";
            string safe=System.Text.RegularExpressions.Regex.Replace(code,@"[^A-Z_0-9]","");
            return "连接未通过，请检查登录、模型和网络"+(safe.Length==0?"":"（"+safe.Substring(0,Math.Min(50,safe.Length))+"）");
        }
    }
}
