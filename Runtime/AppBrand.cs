using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WeChatPet {
    internal static class AppBrand {
        internal static void ApplyIcon(Window window) {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "wechat-pet", "brand", "wechat-pet.ico");
            if (!File.Exists(path)) return;
            var icon = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            icon.Freeze(); window.Icon = icon;
        }
    }
}
