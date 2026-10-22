using System;
using System.IO;
using System.Linq;
using System.Text;

namespace WeChatPet.Runtime {
    public static class ModelRecoveryTests {
        public static void Run(Action<string,bool> check) {
            // Match the other runtime suites: the executable's writable test
            // directory is stable even when Windows redirects or restricts TEMP.
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"model-recovery-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,"credentials.dpapi");
            try {
                string notice;var loaded=ModelClient.LoadCredentials(path,out notice);
                check("首次启动没有凭证文件时不提示损坏",loaded.Count==0&&notice==null&&!File.Exists(path));
                string valid="{\"chatgpt\":{\"access\":\"synthetic-access\",\"refresh\":\"synthetic-refresh\",\"expires\":0,\"clientId\":\"synthetic-client\"},\"deepseek-api\":{\"key\":\"synthetic-api-key\"},\"deepseek-account\":{\"token\":\"synthetic-token\",\"issuer\":\"https://platform.deepseek.com\"}}";
                LocalStore.Write(path,valid,true);byte[] original=File.ReadAllBytes(path);
                loaded=ModelClient.LoadCredentials(path,out notice);
                check("有效三入口凭证和已过期会员令牌原样保留",loaded.Count==3&&notice==null&&original.SequenceEqual(File.ReadAllBytes(path))&&Directory.GetFiles(directory,"*.bak").Length==0);
                byte[] corrupt=Encoding.UTF8.GetBytes("synthetic-corrupt-vault");File.WriteAllBytes(path,corrupt);
                loaded=ModelClient.LoadCredentials(path,out notice);string first=Directory.GetFiles(directory,"*.bak").Single();
                check("损坏DPAPI先保留原始备份再允许空连接",loaded.Count==0&&!String.IsNullOrEmpty(notice)&&!File.Exists(path)&&corrupt.SequenceEqual(File.ReadAllBytes(first)));
                File.WriteAllBytes(path,corrupt);ModelClient.LoadCredentials(path,out notice);
                check("重复恢复使用唯一备份且不覆盖旧备份",Directory.GetFiles(directory,"*.bak").Length==2&&corrupt.SequenceEqual(File.ReadAllBytes(first)));
                foreach(string invalid in new[]{"null","[]","{","{\"chatgpt\":null}","{\"chatgpt\":[]}","{\"chatgpt\":{\"access\":\"a\",\"refresh\":\"r\",\"clientId\":\"c\",\"expires\":\"0\"}}","{\"deepseek-api\":{\"key\":12}}","{\"deepseek-account\":{\"token\":\"synthetic\",\"issuer\":\"https://example.com\"}}","{\"unknown\":{}}"}) {
                    LocalStore.Write(path,invalid,true);byte[] bytes=File.ReadAllBytes(path);int before=Directory.GetFiles(directory,"*.bak").Length;
                    loaded=ModelClient.LoadCredentials(path,out notice);
                    check("已加密的无效JSON或凭证结构可以恢复："+invalid,loaded.Count==0&&!String.IsNullOrEmpty(notice)&&!File.Exists(path)&&Directory.GetFiles(directory,"*.bak").Length==before+1&&Directory.GetFiles(directory,"*.bak").Any(file=>bytes.SequenceEqual(File.ReadAllBytes(file))));
                }
                LocalStore.Write(path,"{}",true);loaded=ModelClient.LoadCredentials(path,out notice);
                check("正常空凭证库不触发恢复",loaded.Count==0&&notice==null&&File.Exists(path));
                File.WriteAllBytes(path,corrupt);int backupCount=Directory.GetFiles(directory,"*.bak").Length;bool readFailed=false;
                using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None)) {
                    try{ModelClient.LoadCredentials(path,out notice);}catch(IOException){readFailed=true;}
                }
                check("读取锁定等IO错误不误判损坏或改动凭证",readFailed&&File.Exists(path)&&corrupt.SequenceEqual(File.ReadAllBytes(path))&&Directory.GetFiles(directory,"*.bak").Length==backupCount);
                bool backupFailed=false;
                // Reading is allowed, but moving/deleting the open original is not.
                using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    try{ModelClient.LoadCredentials(path,out notice);}catch(InvalidOperationException e){backupFailed=e.Message=="CREDENTIAL_BACKUP_FAILED"&&ModelClient.Explain(e).Contains("原文件已保留");}
                }
                check("备份失败明确报错且不覆盖原凭证",backupFailed&&File.Exists(path)&&corrupt.SequenceEqual(File.ReadAllBytes(path))&&Directory.GetFiles(directory,"*.bak").Length==backupCount);
                ModelClient.LoadCredentials(path,out notice);LocalStore.Write(path,"{\"deepseek-api\":{\"key\":\"synthetic-reconnected-key\"}}",true);
                loaded=ModelClient.LoadCredentials(path,out notice);
                check("恢复后可保存新连接并正常重启读取",loaded.ContainsKey("deepseek-api")&&notice==null&&File.Exists(first));
            }finally {
                foreach(string file in Directory.GetFiles(directory))File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }
}
