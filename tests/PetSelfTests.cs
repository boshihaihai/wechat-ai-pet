using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeChatPet {
    public static class PetTestMain {
        [STAThread]
        public static int Main() {
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            int status = PetSelfTests.Run(root);
            Console.WriteLine(File.ReadAllText(Path.Combine(root, "tests", "results.json")));
            return status;
        }
    }
    // Component rendering and state-contract checks. No top-level window is shown.
    internal static class PetSelfTests {
        static readonly List<object> results = new List<object>();
        static string outDir;
        static void Check(string name, bool passed) {
            results.Add(new { name = name, passed = passed });
            if (!passed) throw new InvalidOperationException(name);
        }
        internal static int Run(string root) {
            outDir = Path.Combine(root, "tests");
            Directory.CreateDirectory(outDir);
            string settings = Path.Combine(outDir, "isolated-settings-" + Guid.NewGuid().ToString("N") + ".json");
            PetWindow pet = null;
            bool success = false;
            try {
                Runtime.ReplyEmojiTests.Run(Check,outDir);
                Runtime.InlineOverflowTests.Run(Check);
                pet = new PetWindow(root, settings);
                Check("默认仅宠物本体，不显示气泡", !pet.IsBubbleVisible);
                Render(pet, null);
                Check("桌面本体没有常驻按钮", CountButtons((FrameworkElement)pet.Content) == 0);
                int pauseEvents = 0;
                pet.PauseChanged += delegate { pauseEvents++; };
                string[] skins = { "personal", "whale", "panda" };
                foreach (string skin in skins) {
                    if (skin == "whale") {
                        foreach (string filename in new[] { "resting", "working", "waiting", "celebrate", "sleeping", "error" })
                            SafeWhaleSvg.Load(Path.Combine(pet.AnimationVisual.WhaleDirectory, filename + ".svg"));
                    }
                    pet.SelectSkin(skin);
                    Check(skin + " 素材完整加载", pet.CurrentSkin == skin);
                    foreach (PetState state in Enum.GetValues(typeof(PetState))) {
                        bool resting = state == PetState.Idle || state == PetState.Paused;
                        pet.Preview(state);
                        pet.SetMotion(true);
                        pet.ApplyFrame(0.0);
                        byte[] initial = Render(pet, null);
                        byte[] idleBody = resting && skin != "whale" ? Render(pet, Path.Combine(outDir,skin+"-"+state.ToString().ToLowerInvariant()+"-open.png"),true) : null;
                        if(resting) {
                            bool stable=true;
                            foreach(double time in new[]{.35,1.2,2.4,3.55,4.2,9.4}){pet.ApplyFrame(time);stable &= Same(initial,Render(pet,null));}
                            Check(skin+" / "+state+" 眨眼间隔内没有歪头或身体运动",stable);
                        }
                        pet.ApplyFrame(resting ? 3.73 : 0.35);
                        string frameFile = (state == PetState.Ready || state == PetState.Thinking)
                            ? Path.Combine(outDir, skin + "-" + state.ToString().ToLowerInvariant() + ".png") : null;
                        byte[] animated = Render(pet, frameFile);
                        Check(skin + " / " + state + (resting ? " 休息状态保留眨眼" : " 动画帧随时间变化"), !Same(initial, animated));
                        if(idleBody!=null)Check(skin+" / "+state+" 眨眼时身体像素不变",Same(idleBody,Render(pet,Path.Combine(outDir,skin+"-"+state.ToString().ToLowerInvariant()+"-blink.png"),true)));
                        if (skin != "whale") {
                            pet.ApplyFrame(0); byte[] loopStart = Render(pet, null);
                            pet.ApplyFrame(pet.AnimationVisual.CycleSeconds * 100);
                            Check(skin + " / " + state + " 持续百次循环返回首帧", Same(loopStart, Render(pet, null)));
                            pet.ApplyFrame(.45); int frameBefore = pet.AnimationVisual.CurrentFrameIndex;
                            pet.AnimationVisual.SetState(state);
                            Check(skin + " / " + state + " 同状态刷新不重置动画", pet.AnimationVisual.CurrentFrameIndex == frameBefore && (resting || frameBefore > 0));
                        }
                        pet.SetMotion(false);
                        pet.ApplyFrame(0.1); byte[] frozen = Render(pet, null);
                        pet.ApplyFrame(1.2); byte[] frozenAgain = Render(pet, null);
                        Check(skin + " / " + state + " 关闭动效后静止", Same(frozen, frozenAgain));
                    }
                }
                pet.Preview(PetState.Ready);
                Check("动作预览不显示模拟回复卡片", !pet.IsBubbleVisible);
                pet.SetMotion(true);
                double center = pet.PetCenterX, bottom = pet.PetBottomY;
                pet.Present(PetState.Ready, "离线合成建议，测试换肤保持状态", true);
                Check("真实建议就绪才出现气泡", pet.IsBubbleVisible);
                Check("气泡出现时人物位置不变", Math.Abs(pet.PetCenterX - center) < 0.1 && Math.Abs(pet.PetBottomY - bottom) < 0.1);
                pet.SelectSkin("personal"); pet.SelectSkin("whale"); pet.SelectSkin("panda");
                Check("换肤保持业务状态与正文且不发暂停事件", pet.CurrentState == PetState.Ready && pet.IsLive && pauseEvents == 0 && pet.CurrentMessage == "离线合成建议，测试换肤保持状态");
                pet.Preview(PetState.Error);
                Check("连接模式禁止动作预览覆盖真实建议", pet.CurrentState == PetState.Ready && pet.IsLive);
                var settingsPreview = new PetSettingsWindow(pet, root, delegate {}, delegate {}, delegate {}, delegate { return false; });
                try {
                    foreach (PetState state in Enum.GetValues(typeof(PetState))) {
                        settingsPreview.PlayPreview(state);
                        Check("连接期间设置页可预览 " + state + " 且不修改推荐", settingsPreview.IsPreviewPlaying && settingsPreview.PreviewState == state && pet.CurrentState == PetState.Ready && pet.IsBubbleVisible && pet.CurrentMessage == "离线合成建议，测试换肤保持状态");
                    }
                    settingsPreview.SetPreviewExpanded(false);
                    Check("收起预览停止独立动画", !settingsPreview.IsPreviewPlaying);
                    settingsPreview.PlayPreview(PetState.Thinking);
                } finally { settingsPreview.Close(); }
                Check("关闭设置停止预览计时器", !settingsPreview.IsPreviewPlaying);
                pet.Present(PetState.Idle, "合成建议已失效", true);
                Check("建议失效收起气泡并保持人物位置", !pet.IsBubbleVisible && Math.Abs(pet.PetCenterX - center) < 0.1 && Math.Abs(pet.PetBottomY - bottom) < 0.1);
                pet.ShowRecommendationNotice("最新消息的发言人未确认，暂不生成");
                Check("群点击失败显示短提示且不出现Tab填入提示",pet.IsBubbleVisible&&pet.ReplyBubble.Hint.Visibility==Visibility.Collapsed&&pet.CurrentState==PetState.Idle);
                pet.Present(PetState.Thinking,"正在生成推荐",true);
                Check("开始新推荐立即清除先前失败提示",!pet.IsBubbleVisible);
                pet.Present(PetState.Idle,"等待群聊点击",true);
                var originalWorkArea=pet.WorkAreaForPoint;
                foreach(var work in new[]{new Rect(2000,0,1600,1000),new Rect(-1600,-100,1600,1000)}) {
                    pet.WorkAreaForPoint=delegate(Point p){return work;};pet.Left=work.Left+200;pet.Top=work.Top+400;
                    double monitorCenter=pet.PetCenterX;
                    pet.Present(PetState.Ready,"副屏合成推荐",true);pet.Present(PetState.Paused,"暂停",true);
                    Check("副屏状态切换保持位置 "+work.Left,Math.Abs(pet.PetCenterX-monitorCenter)<.1&&pet.Left>=work.Left&&pet.Left+pet.Width<=work.Right);
                    pet.Left=work.Right+100;pet.Present(PetState.Idle,"重新定位",true);
                    Check("屏幕边缘收回后人物仍在窗口内 "+work.Left,pet.PetCenterX>=pet.Left+4+pet.AnimationVisual.Width/2-.1&&pet.PetCenterX<=pet.Left+pet.Width-4-pet.AnimationVisual.Width/2+.1);
                    pet.Top=work.Top;pet.Present(PetState.Ready,"屏幕顶端的较长推荐气泡，需要保证全文入口仍能显示",true);
                    Check("屏幕顶部出现推荐时气泡不越界 "+work.Left,pet.Top>=work.Top&&pet.Top+pet.Height<=work.Bottom);
                    pet.Present(PetState.Idle,"复位",true);
                }
                pet.WorkAreaForPoint=originalWorkArea;
                foreach(string message in new[]{"普通推荐","[调皮]","文字和[微笑]混排，换行后也要保留完整的提示。\n再多说两句[调皮]，确保推荐变化时气泡高度及时更新。","[调皮][微笑][呲牙][偷笑][调皮][微笑]","短回复","[调皮]"}) {
                    pet.Present(PetState.Ready,message,true);
                    Render(pet,message=="[调皮]"?Path.Combine(outDir,"pet-emoji-bubble.png"):null);
                    var rootPanel=(System.Windows.Controls.Grid)pet.Content;
                    var replyBubble=(System.Windows.Controls.Border)rootPanel.Children[0];
                    var replyBody=pet.ReplyBubble.Body;
                    var replyHint=pet.ReplyBubble.Hint;
                    double hintTop=replyHint.TranslatePoint(new Point(),replyBubble).Y;
                    Check("正式气泡高度随文字与表情变化及时更新 "+message,Math.Abs(pet.Height-(replyBubble.DesiredSize.Height+pet.AnimationVisual.Height+8))<.1);
                    Check("正式气泡的 Tab 提示完整且不与正文重叠 "+message,hintTop>=replyBody.TranslatePoint(new Point(),replyBubble).Y+replyBody.ActualHeight+4.9&&hintTop+replyHint.ActualHeight<=replyBubble.ActualHeight-replyBubble.Padding.Bottom-replyBubble.BorderThickness.Bottom+.1);
                    Check("正式宠物气泡保持原始填入正文 "+message,pet.CurrentMessage==message&&pet.IsBubbleVisible);
                }
                pet.Present(PetState.Idle,"推荐已失效",true);Render(pet,null);
                var clearedBody=pet.ReplyBubble.Body;
                Check("正式气泡撤销后清除表情且恢复宠物高度",!pet.IsBubbleVisible&&clearedBody.Inlines.Count==0&&Math.Abs(pet.Height-(pet.AnimationVisual.Height+8))<.1);
                pet.SetMotion(false);
                pet.Close(); pet = null;
                pet = new PetWindow(root, settings);
                Check("重启恢复形象与动效设置", pet.CurrentSkin == "panda" && !pet.MotionEnabled);
                success = true;
            } catch (Exception error) {
                results.Add(new { name = "exception", passed = false, type = error.GetType().Name, detail = error.Message });
            } finally {
                if (pet != null) pet.Close();
                if (File.Exists(settings)) File.Delete(settings);
                File.WriteAllText(Path.Combine(outDir, "results.json"), new JavaScriptSerializer().Serialize(
                    new { success = success, checkedAt = DateTimeOffset.UtcNow.ToString("o"), mode = "offline-component-tests", visibleWindows = 0, wechatReads = 0, modelCalls = 0, sendActions = 0, checks = results }), new UTF8Encoding(false));
            }
            return success ? 0 : 1;
        }
        static int CountButtons(DependencyObject parent) {
            var element=parent as UIElement;if(element!=null&&element.Visibility!=Visibility.Visible)return 0;
            int total = parent is System.Windows.Controls.Button ? 1 : 0;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                total += CountButtons(VisualTreeHelper.GetChild(parent, i));
            return total;
        }
        static byte[] Render(PetWindow pet, string file, bool bodyOnly = false) {
            var element = (FrameworkElement)pet.Content;
            double width = Double.IsNaN(pet.Width) ? 440 : pet.Width;
            double height = Double.IsNaN(pet.Height) ? 420 : pet.Height;
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            int stride = bitmap.PixelWidth * 4;
            byte[] pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            if (file != null) {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(file)) encoder.Save(stream);
            }
            int start = bodyOnly ? stride * (int)(bitmap.PixelHeight * .58) : 0;
            using (var hash = SHA256.Create()) return hash.ComputeHash(pixels,start,pixels.Length-start);
        }
        static bool Same(byte[] a, byte[] b) {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}


