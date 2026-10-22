using System;
using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace WeChatPet.Runtime {
    // The tray owns only presentation controls; hiding the pet never stops the runtime.
    internal sealed class TrayIcon : IDisposable {
        readonly Forms.NotifyIcon notification;
        readonly Forms.ContextMenuStrip menu;
        readonly Icon icon;
        bool disposed;
        internal TrayIcon(PetWindow pet, Func<DesktopRuntime> runtime, Action exit) {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","wechat-pet","brand","wechat-pet.ico");
            icon=new Icon(path,32,32);
            menu=new Forms.ContextMenuStrip {ShowImageMargin=false,ShowCheckMargin=false};
            menu.Items.Add("显示桌宠",null,delegate { pet.RestorePet(); });
            menu.Items.Add("桌宠设置…",null,delegate { pet.OpenSettings(); });
            menu.Items.Add("模型连接…",null,delegate { pet.OpenConnection(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            var pause=menu.Items.Add("暂停推荐",null,delegate { var r=runtime(); if(r!=null)r.SetPaused(!r.Settings.Paused); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出桌宠",null,delegate { exit(); });
            menu.Opening+=delegate { var r=runtime(); pause.Enabled=r!=null; pause.Text=r!=null&&r.Settings.Paused?"继续推荐":"暂停推荐"; };
            notification=new Forms.NotifyIcon {Icon=icon,Text="微信 AI 桌宠",ContextMenuStrip=menu,Visible=true};
            notification.DoubleClick+=delegate { pet.RestorePet(); };
        }
        public void Dispose() {
            if(disposed)return;disposed=true;
            notification.Visible=false;notification.Dispose();menu.Dispose();icon.Dispose();
        }
    }
}
