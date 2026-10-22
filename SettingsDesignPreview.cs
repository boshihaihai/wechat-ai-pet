using System;
using WeChatPet.Runtime;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeChatPet
{
    // Render the settings content without showing a native window or connecting to a provider.
    public static class SettingsDesignPreview
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string output = Path.Combine(root, "tests");
            Directory.CreateDirectory(output);
            string preferences = Path.Combine(output, "settings-design-" + Guid.NewGuid().ToString("N") + ".json");
            PetWindow pet = null;
            PetSettingsWindow settings = null;
            try
            {
                pet = new PetWindow(root, preferences);
                pet.SelectSkin("personal");
                settings = new PetSettingsWindow(pet, root, delegate { }, delegate { }, delegate { }, delegate { return false; });
                FrameworkElement previewRoot = CreateRenderingRoot(settings);
                settings.SetPreviewExpanded(false);
                settings.Refresh();
                Render(previewRoot, Path.Combine(output, "settings-personal.png"));

                if (!pet.SelectSkin("whale"))
                    throw new InvalidOperationException("The installed whale assets could not be loaded: " + pet.LastAssetError);
                settings.Refresh();
                Render(previewRoot, Path.Combine(output, "settings-whale.png"));

                pet.SelectSkin("panda"); settings.Refresh();
                Render(previewRoot, Path.Combine(output, "settings-panda.png"));
                pet.Present(PetState.Ready, "离线预览建议", true);
                settings.PlayPreview(PetState.Thinking);
                settings.SetPreviewExpanded(true);
                settings.Refresh();
                Render(previewRoot, Path.Combine(output, "settings-expanded.png"));
                foreach (string provider in new[] { "chatgpt", "deepseek-account", "deepseek-api" }) {
                    var config = new RuntimeSettings { Provider = provider, Model = provider == "chatgpt" ? "gpt-6.1-sol" : "deepseek-flash", Validated = true };
                    var connection = new ModelConnectionWindow(config);
                    var option = ModelOption.Create(config.Model, provider == "chatgpt" ? "GPT-6.1 Sol" : "DeepSeek-V4.1-Flash", provider);
                    option.Efforts = provider == "chatgpt" ? new[]{"low","medium","high","xhigh","max"} : new[]{"none","low","high","max"};
                    option.DefaultEffort = provider == "chatgpt" ? "medium" : "high";
                    connection.SetCatalog(new[]{option}, true);
                    Render(Detach(connection.PreviewSurface, connection, 418), Path.Combine(output, "model-" + provider + ".png"), 418);
                    connection.Close();
                }
                Console.WriteLine("Rendered four settings previews at 144 DPI; no window was shown.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
                return 1;
            }
            finally
            {
                try { if (settings != null) settings.Close(); }
                finally
                {
                    try { if (pet != null) pet.Close(); }
                    finally { if (File.Exists(preferences)) File.Delete(preferences); }
                }
            }
        }

        private static FrameworkElement CreateRenderingRoot(PetSettingsWindow settings)
        {
            return Detach(settings.PreviewSurface, settings, 418);
        }
        private static FrameworkElement Detach(FrameworkElement surface, Window window, double width) {
            surface.SetValue(TextElement.FontFamilyProperty, window.FontFamily);
            surface.SetValue(TextElement.FontSizeProperty, window.FontSize);
            surface.UseLayoutRounding = true; surface.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(surface, TextFormattingMode.Display);
            Decorator parent = surface.Parent as Decorator;
            ContentControl control = surface.Parent as ContentControl;
            if (parent != null && parent.Child == surface) parent.Child = null;
            else if (control != null && control.Content == surface) control.Content = null;
            else throw new InvalidOperationException("Cannot detach preview");
            return new Border { Width = width, Child = surface, UseLayoutRounding = true, SnapsToDevicePixels = true };
        }

        private static void Render(FrameworkElement surface, string file, double width = 418)
        {
            const double dpi = 144;
            InvalidateLayout(surface);
            surface.Measure(new Size(width, Double.PositiveInfinity));
            double height = Math.Ceiling(surface.DesiredSize.Height);
            if (height <= 0 || Double.IsNaN(height) || Double.IsInfinity(height))
                throw new InvalidOperationException("The settings preview has no measurable height.");
            surface.Arrange(new Rect(0, 0, width, height));
            surface.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(width * dpi / 96), (int)Math.Ceiling(height * dpi / 96),
                dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(file)) encoder.Save(stream);
            Console.WriteLine(Path.GetFileName(file) + ": " + bitmap.PixelWidth + " x " + bitmap.PixelHeight);
        }

        private static void InvalidateLayout(DependencyObject element)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                InvalidateLayout(VisualTreeHelper.GetChild(element, i));
            UIElement visual = element as UIElement;
            if (visual != null) { visual.InvalidateMeasure(); visual.InvalidateArrange(); }
        }
    }
}

