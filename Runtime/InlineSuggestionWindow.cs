using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WeChatPet.Runtime {
    // Read-only visual layer: no input pattern, clipboard, key injection or send API.
    internal sealed class InlineSuggestionWindow : Window {
        readonly TextBlock suggestion, hint;
        readonly Border hintBadge;
        readonly ColumnDefinition fullColumn,hintColumn;
        readonly FullReplyPreview fullPreview;
        readonly DispatcherTimer timer;
        readonly Func<bool> enabled;
        readonly Func<ChatSnapshot> snapshot;
        readonly Func<Recommendation> recommendation;
        IntPtr handle; bool disposed;
        internal string LastDecision { get; private set; }
        internal string BackgroundColor { get; private set; }
        internal FullReplyPreview FullPreview {get{return fullPreview;}}
        internal bool FullPreviewRequired {get;private set;}
        internal static bool Eligible(bool hidden, ChatSnapshot s, Recommendation r, long now) {
            return BlockReason(hidden,s,r,now)==null;
        }
        internal static string BlockReason(bool hidden,ChatSnapshot s,Recommendation r,long now) {
            if(!hidden)return "pet_visible_or_paused";
            if(r==null)return "no_recommendation";
            if(s==null||!s.Available)return "read_unavailable";
            if(!s.AtEnd)return "history_position";
            if(!s.Foreground)return "wechat_background";
            if(!s.Focused)return "input_not_focused";
            if(!s.DraftEmpty||!s.DraftConsistent)return "draft_not_empty_or_inconsistent";
            if(s.Composing)return "composing";
            if(!s.ImeAllowed)return "ime_unknown";
            if(s.GroupChat!=r.GroupChat||(s.GroupChat&&(!MemoryIdentity.Valid(s.AccountId)||!MemoryIdentity.Valid(s.GroupId)||s.AccountId!=r.AccountId||s.GroupId!=r.GroupId)))return "context_changed";
            if(s.Window==0||String.IsNullOrEmpty(s.Session)||String.IsNullOrEmpty(r.Text)||s.Session!=r.Session||s.Tail!=r.Tail||s.ContentRevision!=r.ContentRevision||s.InputIdentity!=r.InputIdentity||s.Activity!=r.Activity)return "context_changed";
            if(now<s.CapturedTicks||now-s.CapturedTicks>TimeSpan.TicksPerMillisecond*500)return "snapshot_expired";
            if(now<r.CreatedTicks||now-r.CreatedTicks>TimeSpan.TicksPerSecond*60)return "recommendation_expired";
            if(!(Finite(s.InputLeft)&&Finite(s.InputTop)&&Finite(s.InputWidth)&&Finite(s.InputHeight)&&s.InputWidth>=100&&s.InputHeight>=45&&s.InputWidth<=s.WindowWidth&&s.InputHeight<=s.WindowHeight
                &&s.InputLeft>=s.WindowLeft&&s.InputTop>=s.WindowTop&&s.InputLeft+s.InputWidth<=s.WindowLeft+s.WindowWidth+2&&s.InputTop+s.InputHeight<=s.WindowTop+s.WindowHeight+2))return "input_geometry_invalid";
            return null;
        }
        static bool Finite(double n){return !Double.IsNaN(n)&&!Double.IsInfinity(n);}
        internal InlineSuggestionWindow(Func<bool> enabled,Func<ChatSnapshot> snapshot,Func<Recommendation> recommendation) {
            this.enabled=enabled;this.snapshot=snapshot;this.recommendation=recommendation;
            fullPreview=new FullReplyPreview();
            Title="微信回复预览";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;
            ShowActivated=false;ShowInTaskbar=false;Focusable=false;IsHitTestVisible=false;Topmost=true;
            Width=300;Height=80;FontFamily=new FontFamily("Microsoft YaHei UI");UseLayoutRounding=true;
            var panel=new Grid{ClipToBounds=true};
            panel.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            fullColumn=new ColumnDefinition{Width=new GridLength(0)};panel.ColumnDefinitions.Add(fullColumn);
            hintColumn=new ColumnDefinition{Width=new GridLength(100)};panel.ColumnDefinitions.Add(hintColumn);
            suggestion=new TextBlock{Foreground=SettingsTheme.Brush("#9CA0A8"),FontSize=14,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};panel.Children.Add(suggestion);
            hint=new TextBlock{Text="按 Tab 接受",Foreground=SettingsTheme.Brush("#9CA0A8"),FontSize=11};
            hintBadge=new Border{Child=hint,BorderBrush=SettingsTheme.Brush("#E4E7EB"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(7,2,7,2),Margin=new Thickness(14,0,0,0),VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumn(hintBadge,2);panel.Children.Add(hintBadge);Content=panel;
            SourceInitialized+=delegate {
                handle=new WindowInteropHelper(this).Handle;
                SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000000|0x00000020|0x00000080);
                HwndSource.FromHwnd(handle).AddHook(delegate(IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled) {
                    if(m==0x84){handled=true;return new IntPtr(-1);} // HTTRANSPARENT
                    if(m==0x21){handled=true;return new IntPtr(3);} // MA_NOACTIVATE
                    return IntPtr.Zero;
                });
            };
            timer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(50)};timer.Tick+=delegate{Refresh();};timer.Start();
        }
        internal void Refresh() {
            if(disposed)return;
            var s=snapshot();var r=recommendation();long now=DateTime.UtcNow.Ticks;
            LastDecision=BlockReason(enabled(),s,r,now);
            if(LastDecision!=null){Conceal();return;}
            if(Win.GetForegroundWindow()!=new IntPtr(s.Window)||Win.IsIconic(new IntPtr(s.Window))) {LastDecision="foreground_changed";Conceal();return;}
            // A moving/resizing window must produce a fresh observer position first.
            NativeRect rect;IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
            try {
                if(!GetWindowRect(new IntPtr(s.Window),out rect)||Math.Abs(rect.Left-s.WindowLeft)>2||Math.Abs(rect.Top-s.WindowTop)>2||Math.Abs(rect.Right-rect.Left-s.WindowWidth)>2||Math.Abs(rect.Bottom-rect.Top-s.WindowHeight)>2){LastDecision="window_moved";Conceal();return;}
                double targetScale=Math.Max(1,GetDpiForWindow(new IntPtr(s.Window))/96.0);
                double pixelWidth=Math.Max(1,s.InputWidth-6*targetScale),pixelHeight=Math.Min(28*targetScale,s.InputHeight-8*targetScale);
                // Read one blank background pixel, never text or image content. The
                // opaque row replaces WeChat's own placeholder visually, not its draft.
                // WeChat renders through the compositor; its window DC can return
                // black. Sample the foreground input's blank screen pixel instead.
                IntPtr dc=GetDC(IntPtr.Zero);uint color=0xffffffff;
                try {if(dc!=IntPtr.Zero)color=GetPixel(dc,(int)(s.InputLeft+s.InputWidth-6*targetScale),(int)(s.InputTop+s.InputHeight-6*targetScale));}
                finally {if(dc!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,dc);}
                if(color==0xffffffff){LastDecision="background_unavailable";Conceal();return;}
                var background=Color.FromRgb((byte)(color&255),(byte)((color>>8)&255),(byte)((color>>16)&255));
                BackgroundColor=background.ToString();Background=new SolidColorBrush(background);
                if(handle==IntPtr.Zero)new WindowInteropHelper(this).EnsureHandle();
                var source=HwndSource.FromHwnd(handle);double ownScale=source.CompositionTarget.TransformToDevice.M11;
                Width=pixelWidth/ownScale;Height=pixelHeight/ownScale;
                double ratio=targetScale/ownScale;
                double hintPixels=pixelWidth>=280*targetScale?100*targetScale:0;
                LayoutReply(r.Text,pixelWidth/ownScale,ratio,hintPixels>0,ownScale);
                if(!IsVisible)Show();
                bool positioned=SetWindowPos(handle,new IntPtr(-1),(int)Math.Round(s.InputLeft+3*targetScale),(int)Math.Round(s.InputTop),(int)Math.Round(pixelWidth),(int)Math.Round(pixelHeight),0x0010);
                LastDecision=positioned&&Win.IsWindowVisible(handle)?"visible":"native_window_hidden";
                if(LastDecision=="visible"&&FullPreviewRequired)fullPreview.Present(r.Id,r.Text,s.InputLeft+3*targetScale+pixelWidth-72*targetScale-hintPixels,s.InputTop,targetScale);else fullPreview.Conceal();
            }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
        }
        // Measure the same WPF runs and inline images that are drawn in the row.
        // Always decide against the space WITHOUT the full-text button: using the
        // current column width here would make the decision depend on the last frame.
        internal bool LayoutReply(string raw,double rowWidth,double ratio,bool showHint,double pixelsPerDip) {
            suggestion.FontSize=14*ratio;ReplyEmoji.Set(suggestion,raw,true);
            hint.FontSize=11*ratio;
            double hintWidth=showHint?100*ratio:0;
            FullPreviewRequired=NeedsFullPreview(suggestion,raw,Math.Max(0,rowWidth-hintWidth),pixelsPerDip);
            fullColumn.Width=new GridLength(FullPreviewRequired?72*ratio:0);
            hintColumn.Width=new GridLength(hintWidth);hintBadge.Visibility=showHint?Visibility.Visible:Visibility.Collapsed;
            hintBadge.Margin=new Thickness(14*ratio,0,0,0);hintBadge.Padding=new Thickness(7*ratio,2*ratio,7*ratio,2*ratio);
            if(!FullPreviewRequired)fullPreview.Conceal();
            return FullPreviewRequired;
        }
        internal static bool NeedsFullPreview(TextBlock rendered,string raw,double availableWidth,double pixelsPerDip) {
            if((raw??"").IndexOfAny(new[]{'\r','\n','\u2028','\u2029'})>=0)return true;
            rendered.Measure(new Size(Double.PositiveInfinity,Double.PositiveInfinity));
            // Round to physical pixels as the native overlay does. A stable width
            // has a stable decision even when text ends close to the clipping edge.
            return Math.Ceiling(rendered.DesiredSize.Width*pixelsPerDip)>Math.Floor(Math.Max(0,availableWidth)*pixelsPerDip);
        }
        void Conceal(){if(IsVisible)Hide();ReplyEmoji.Set(suggestion,"");FullPreviewRequired=false;fullColumn.Width=new GridLength(0);fullPreview.Conceal();}
        internal void Stop(){disposed=true;timer.Stop();fullPreview.Close();Close();}
        [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
        [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll")]static extern int SetWindowLong(IntPtr hwnd,int index,int value);
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
        [DllImport("gdi32.dll")]static extern uint GetPixel(IntPtr dc,int x,int y);
    }
}
