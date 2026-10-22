using System;
using System.Collections.Generic;

namespace WeChatPet.Runtime {
    // A loaded build cannot become supported by reopening a chat or changing a
    // window handle. Retry its build check only after the process identity changes.
    internal sealed class ObserverFailureGate {
        readonly Dictionary<int,Failure> blocked=new Dictionary<int,Failure>();
        sealed class Failure { public long Birth;public string Code; }

        internal static bool IsTerminal(string code) {
            if(code=="unsupported_wechat_build"||code=="lease_signature_mismatch")return true;
            const string prefix="native_signature_mismatch_0x";
            if(code==null||!code.StartsWith(prefix,StringComparison.Ordinal)||code.Length==prefix.Length)return false;
            for(int i=prefix.Length;i<code.Length;i++) {
                char c=code[i];if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F')))return false;
            }
            return true;
        }
        internal static string GuardianFailure(Exception error) {
            return error is InvalidOperationException&&error.Message=="lease_signature_mismatch"
                ?"lease_signature_mismatch":"lease_failed";
        }
        internal static bool GuardianReady(string response) {
            if(response=="lease_signature_mismatch")throw new InvalidOperationException("lease_signature_mismatch");
            return response=="ready";
        }
        public bool Block(int pid,long birth,string code) {
            if(pid<=0||birth<=0||!IsTerminal(code))return false;
            blocked[pid]=new Failure {Birth=birth,Code=code};return true;
        }
        public string CodeFor(int pid,long birth) {
            Failure failure;
            if(!blocked.TryGetValue(pid,out failure))return null;
            if(failure.Birth==birth)return failure.Code;
            blocked.Remove(pid);return null;
        }
        internal static string Explain(string code) {
            return code=="unsupported_wechat_build"
                ?"当前微信版本未适配，读取和填入已停止。请使用支持的微信版本并重启微信。"
                :"当前微信构建校验未通过，读取和填入已停止。重启微信后会重新校验。";
        }
    }
}
