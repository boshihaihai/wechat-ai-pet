using System;
using System.IO;

namespace WeChatPet.Runtime {
    internal static class DiagnosticsWriteStatusTests {
        internal static void Run(Action<string,bool> check) {
            var state=new DiagnosticsWriteStatus();int calls=0;
            check("诊断尚未写入不虚构成功时间",state.LastSuccessfulTicks==0);
            check("诊断成功记录实际写入时间",state.TryWrite(()=>{calls++;},100)&&calls==1&&state.LastSuccessfulTicks==100);
            check("诊断失败不打断运行也不刷新旧成功时间",!state.TryWrite(()=>{throw new IOException("sensitive detail");},200)&&state.LastSuccessfulTicks==100&&state.ConsecutiveFailures==1);
            check("诊断仅保留异常类别而非路径或正文",state.ErrorCode=="IOException");
            check("连续诊断失败可观察且允许重试",!state.TryWrite(()=>{throw new UnauthorizedAccessException();},300)&&state.ConsecutiveFailures==2&&state.ErrorCode=="UnauthorizedAccessException");
            check("存储恢复后诊断自动恢复并清除警告",state.TryWrite(()=>{calls++;},400)&&calls==2&&state.LastSuccessfulTicks==400&&state.ConsecutiveFailures==0&&state.ErrorCode==null);
        }
    }
}
