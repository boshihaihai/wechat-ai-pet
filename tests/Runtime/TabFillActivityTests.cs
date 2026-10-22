using System;

namespace WeChatPet.Runtime {
    internal static class TabFillActivityTests {
        internal static void Run(Action<string,bool> check) {
            ObserverProgram.ProcessTabState(false,null);
            try {
                int accepts=0;Func<bool> accept=delegate {accepts++;return true;};
                check("首次接受真实Tab本身不撤销推荐",!ObserverProgram.IsInputActivityKey(9,true,false,false)&&ObserverProgram.ProcessTabState(true,accept)&&accepts==1);
                check("已接管Tab自动重复继续吞掉且不打断填入",!ObserverProgram.IsInputActivityKey(9,true,false,true)&&ObserverProgram.ProcessTabState(true,accept)&&accepts==1);
                check("未识别注入Tab即使夹在已接管Tab期间也计输入活动",ObserverProgram.IsInputActivityKey(9,true,true,true));
                check("已接管Tab配对抬起仍吞掉且不打断填入",!ObserverProgram.IsInputActivityKey(9,false,false,true)&&ObserverProgram.ProcessTabState(false,null));

                int interrupted=ObserverProgram.IsInputActivityKey(9,true,false,true)?1:0;
                bool swallowed=ObserverProgram.ProcessTabState(true,delegate {accepts++;return false;});
                check("填入期间抬起后再按真实Tab会取消旧填入并放行用户键",interrupted==1&&!swallowed&&accepts==2);
                check("未接管的新Tab自动重复也保留活动且继续放行",ObserverProgram.IsInputActivityKey(9,true,false,true)&&!ObserverProgram.ProcessTabState(true,accept)&&accepts==2);
                check("未接管的新Tab抬起照常放行",!ObserverProgram.IsInputActivityKey(9,false,false,true)&&!ObserverProgram.ProcessTabState(false,null));

                check("真实普通键及外来注入键没有填入豁免",ObserverProgram.IsInputActivityKey(0x41,true,false,true)&&ObserverProgram.IsInputActivityKey(0x11,true,false,true)&&ObserverProgram.IsInputActivityKey(0x56,true,true,true));
                check("普通键抬起不伪造新的输入活动",!ObserverProgram.IsInputActivityKey(0x41,false,false,true));
                check("没有在途填入时普通Tab沿用原规则",!ObserverProgram.IsInputActivityKey(9,true,false,false)&&!ObserverProgram.IsInputActivityKey(9,true,true,false));

                check("取消后的下一次真实Tab仍可单次接管",ObserverProgram.ProcessTabState(true,accept)&&accepts==3);
                check("新一轮已接管Tab保持重复与抬键配对",!ObserverProgram.IsInputActivityKey(9,true,false,true)&&ObserverProgram.ProcessTabState(true,accept)&&ObserverProgram.ProcessTabState(false,null)&&accepts==3);
            } finally {ObserverProgram.ProcessTabState(false,null);}
        }
    }
}
