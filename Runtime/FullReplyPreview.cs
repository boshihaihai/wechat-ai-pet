using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace WeChatPet.Runtime {
    // A separate, non-activating read-only surface. The ghost text remains click-through.
    internal sealed class FullReplyPreview : Window {
        readonly Button toggle;
        readonly TextBlock text;
        readonly ScrollViewer scroll;
        readonly Border card;
        IntPtr handle;
        string id,rawText;
        double anchorX,anchorY,scale;
        internal bool Expanded {get;private set;}
        internal string FullText {get{return rawText??"";}}
        internal FullReplyPreview() {
            Title="查看推荐全文";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true;Background=Brushes.Transparent;ShowActivated=false;ShowInTaskbar=false;Topmost=true;Focusable=false;
            FontFamily=new FontFamily("Microsoft YaHei UI");
            var panel=new DockPanel();Content=panel;
            toggle=SettingsTheme.Button("查看全文","查看推荐全文");toggle.FontSize=11;toggle.Padding=new Thickness(4,0,4,0);toggle.Height=28;toggle.Width=72;toggle.Focusable=false;toggle.HorizontalAlignment=HorizontalAlignment.Right;
            toggle.Background=SettingsTheme.Paper;toggle.Foreground=SettingsTheme.Muted;
            DockPanel.SetDock(toggle,Dock.Bottom);panel.Children.Add(toggle);
            text=new TextBlock{FontSize=14,LineHeight=23,TextWrapping=TextWrapping.Wrap,Foreground=SettingsTheme.Ink};
            scroll=new ScrollViewer{Content=text,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=false};
            ReplyPreviewStyle.Apply(scroll);
            card=new Border{Child=scroll,Background=SettingsTheme.Paper,BorderBrush=SettingsTheme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(16),Margin=new Thickness(0,0,0,5),Visibility=Visibility.Collapsed};panel.Children.Add(card);
            toggle.Click+=delegate{SetExpanded(!Expanded);};
            SourceInitialized+=delegate {
                handle=new WindowInteropHelper(this).Handle;
                SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000000|0x00000080);
                HwndSource.FromHwnd(handle).AddHook(delegate(IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled){if(m==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});
            };
        }
        internal void Present(string nextId,string fullText,double x,double y,double dpiScale) {
            if(id!=nextId){id=nextId;rawText=fullText;Expanded=false;scroll.ScrollToTop();}
            anchorX=x;anchorY=y;scale=dpiScale;ArrangePreview();
        }
        internal void SetExpanded(bool value){Expanded=value;ArrangePreview();}
        void ArrangePreview() {
            if(id==null)return;
            IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
            try {
            if(handle==IntPtr.Zero)new WindowInteropHelper(this).EnsureHandle();
            double ownScale=HwndSource.FromHwnd(handle).CompositionTarget.TransformToDevice.M11;
            var area=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)anchorX,(int)anchorY)).WorkingArea;
            double width=Expanded?Math.Min(420*scale,area.Width-16):72*scale;
            double roomAbove=anchorY-area.Top-8;
            double height=Expanded?Math.Min(350*scale,Math.Max(100*scale,roomAbove+28*scale)):28*scale;
            double left=Math.Max(area.Left+4,Math.Min(area.Right-width-4,anchorX+72*scale-width));
            double top=Math.Max(area.Top+4,anchorY+28*scale-height);
            Width=width/ownScale;Height=height/ownScale;
            text.FontSize=14*scale/ownScale;text.LineHeight=23*scale/ownScale;
            ReplyEmoji.Set(text,rawText);
            toggle.Width=72*scale/ownScale;toggle.Height=28*scale/ownScale;toggle.FontSize=11*scale/ownScale;
            toggle.Content=Expanded?"收起全文":"查看全文";card.Visibility=Expanded?Visibility.Visible:Visibility.Collapsed;
            if(!IsVisible)Show();
            SetWindowPos(handle,new IntPtr(-1),(int)left,(int)top,(int)width,(int)height,0x0010);
            }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
        }
        internal void Conceal(){if(IsVisible)Hide();id=null;rawText=null;ReplyEmoji.Set(text,"");Expanded=false;}
        [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll")]static extern int SetWindowLong(IntPtr hwnd,int index,int value);
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }
}
