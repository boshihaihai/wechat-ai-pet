using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace WeChatPet.Runtime {
    internal sealed class RuntimeSettingsPersistence {
        internal const string SaveFailure="设置未保存，已恢复原设置并停止推荐。请检查目录权限或磁盘空间后重试。";
        readonly RuntimeSettings settings;readonly Action<RuntimeSettings> write;readonly string readError;
        RuntimeSettings committed;
        internal string Error {get;private set;}
        internal RuntimeSettingsPersistence(RuntimeSettings settings,Action<RuntimeSettings> write,string readError=null) {
            this.settings=settings;this.write=write;this.readError=readError;committed=Copy(settings);Error=readError;
        }
        internal bool TrySave(Action invalidate) {
            // Invalidate old suggestions before even attempting a write. Keep the
            // same settings instance because open windows hold a reference to it.
            invalidate();
            if(readError!=null){Restore(committed,settings);Error=readError;return false;}
            try {write(settings);committed=Copy(settings);Error=null;return true;}
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            catch(SecurityException) { }
            Restore(committed,settings);Error=SaveFailure;return false;
        }
        static RuntimeSettings Copy(RuntimeSettings value) {var copy=new RuntimeSettings();Restore(value,copy);return copy;}
        static void Restore(RuntimeSettings source,RuntimeSettings target) {
            target.Provider=source.Provider;target.Model=source.Model;target.Device=source.Device;
            target.Paused=source.Paused;target.Validated=source.Validated;target.ReasoningEffort=source.ReasoningEffort;
            target.SelectedProcess=source.SelectedProcess;target.SelectedProcessBirth=source.SelectedProcessBirth;
            target.ModelsByProvider=source.ModelsByProvider==null?new Dictionary<string,string>():new Dictionary<string,string>(source.ModelsByProvider);
        }
    }
}
