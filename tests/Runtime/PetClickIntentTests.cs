using System;

namespace WeChatPet.Runtime {
    internal static class PetClickIntentTests {
        public static void Run(Action<string,bool> check) {
            var click=new PetClickIntent();
            click.Begin(10,20,"original-group");
            check("群聊单击抬起前不生成",click.Take(1000)==null);
            check("群聊单击开始等待双击间隔",click.Release(1000,500)&&click.Take(1499)==null);
            check("群聊单击延迟后只触发一次",click.Take(1500)=="original-group"&&click.Take(1501)==null);
            click.Begin(10,20,"original-group");click.Release(1000,500);click.Cancel();
            check("双击设置取消待发群聊请求",click.Take(1600)==null);
            click.Begin(10,20,"original-group");
            check("手指轻微移动仍可作为单击",!click.Move(12,22,4,4)&&click.Release(1000,500)&&click.Take(1500)=="original-group");
            click.Begin(10,20,"original-group");
            check("拖动越过系统阈值取消群聊请求",click.Move(14,20,4,4)&&!click.Release(1000,500)&&click.Take(1500)==null);
            click.Begin(10,20,"original-group");click.Move(14,20,4,4);
            check("拖回原位置也不会误作单击",!click.Move(10,20,4,4)&&!click.Release(1000,500));
            click.Begin(10,20,null);
            check("私聊或未确认群聊点击没有请求",!click.Release(1000,500)&&click.Take(1500)==null);
            click.Begin(10,20,"original-group");click.Release(1000,500);click.Cancel();
            check("隐藏或关闭取消等待中的点击",click.Take(1500)==null);
            var s=new ChatSnapshot{Available=true,GroupChat=true,AccountId="account-a",GroupId="group-a",Session="session-a",Tail="tail-a",ContentRevision="content-a",InputIdentity="input-a",ViewIdentity="view-a",Activity=1,Window=10};
            string token=PetClickIntent.ContextToken(s);
            s.CapturedTicks=9999;
            check("同群新采样时间不使点击意图失效",PetClickIntent.ContextToken(s)==token);
            s.GroupId="group-b";check("延迟单击不能改发另一个群",PetClickIntent.ContextToken(s)!=token);s.GroupId="group-a";
            s.AccountId="account-b";check("延迟单击不能转到另一个微信账号",PetClickIntent.ContextToken(s)!=token);s.AccountId="account-a";
            s.ContentRevision="new-content";check("群内新来信使延迟点击失效",PetClickIntent.ContextToken(s)!=token);s.ContentRevision="content-a";
            s.Activity++;check("开始输入使延迟点击失效",PetClickIntent.ContextToken(s)!=token);s.Activity--;
            s.InputIdentity="new-input";check("输入控件重建使延迟点击失效",PetClickIntent.ContextToken(s)!=token);s.InputIdentity="input-a";
            s.GroupChat=false;check("从群切回私聊不会发起群请求",PetClickIntent.ContextToken(s)==null);
            check("没有可读会话不会发起群请求",PetClickIntent.ContextToken(null)==null);
        }
    }
}
