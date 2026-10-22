using System;

namespace WeChatPet.Runtime {
    public static class ObserverFailureTests {
        public static void Run(Action<string,bool> check) {
            var gate=new ObserverFailureGate();
            check("不兼容微信构建停止当前进程自动重连",gate.Block(10,100,"unsupported_wechat_build")&&gate.CodeFor(10,100)=="unsupported_wechat_build");
            bool stable=true;for(int i=0;i<100;i++)stable&=gate.CodeFor(10,100)=="unsupported_wechat_build";
            check("重复发现同一进程不会解锁构建失败",stable);
            check("构建失败不阻止另一微信进程校验",gate.CodeFor(11,200)==null);
            check("进程ID复用但启动时间变化后重新校验",gate.CodeFor(10,101)==null);
            check("新进程正常后不恢复旧进程失败",gate.CodeFor(10,100)==null);
            check("明确原生机器码签名失败锁定当前进程",gate.Block(12,300,"native_signature_mismatch_0x1b778f5")&&gate.CodeFor(12,300)!=null);
            check("辅助功能代码签名失败也停止当前进程",gate.Block(13,400,"lease_signature_mismatch")&&gate.CodeFor(13,400)!=null);
            bool transient=true;
            foreach(string reason in new[]{null,"", "observer_start_failed","lease_unavailable","keyboard_hook_unavailable","target_identity_changed","controls_unavailable","native_list_changed","native_row_binding_changed","native_datasource_type","native_memory_not_readable","native_sample_stale","native_signature_mismatch_0x","native_signature_mismatch_0xnothex","native_signature_mismatch_0x12_extra"})
                transient&=!gate.Block(14,500,reason)&&gate.CodeFor(14,500)==null;
            check("启动权限读取和数据结构暂态失败继续允许重连",transient);
            check("终止故障必须绑定完整进程身份",!gate.Block(0,600,"unsupported_wechat_build")&&!gate.Block(15,0,"unsupported_wechat_build"));
            check("多实例失败各自保留不会互相覆盖",gate.CodeFor(12,300)!=null&&gate.CodeFor(13,400)!=null);
            check("切换其他目标成功校验不清除原目标终止故障",gate.CodeFor(20,800)==null&&gate.CodeFor(12,300)!=null);
            check("兼容提示说明已停止及恢复条件",ObserverFailureGate.Explain("unsupported_wechat_build").Contains("重启微信")&&ObserverFailureGate.Explain("lease_signature_mismatch").Contains("读取和填入已停止"));
            bool propagated=false;
            try {ObserverFailureGate.GuardianReady(ObserverFailureGate.GuardianFailure(new InvalidOperationException("lease_signature_mismatch")));}
            catch(InvalidOperationException error) {propagated=error.Message=="lease_signature_mismatch"&&ObserverFailureGate.IsTerminal(error.Message);}
            check("守护进程明确签名失败经握手保留终止错误",propagated);
            bool generic=true;
            foreach(Exception error in new Exception[]{new InvalidOperationException("lease_state_unknown"),new InvalidOperationException("unsupported_wechat_build"),new System.IO.IOException("lease_signature_mismatch"),new Exception("private-debug-message")}) {
                string code=ObserverFailureGate.GuardianFailure(error);
                generic&=code=="lease_failed"&&!ObserverFailureGate.GuardianReady(code)&&!ObserverFailureGate.IsTerminal(code);
            }
            check("其他守护异常仍为通用失败不泄露信息不锁死",generic);
            check("守护正常握手成功无响应或未知值保持失败",ObserverFailureGate.GuardianReady("ready")&&!ObserverFailureGate.GuardianReady(null)&&!ObserverFailureGate.GuardianReady("unknown"));
        }
    }
}
