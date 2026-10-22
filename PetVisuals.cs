using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace WeChatPet
{
    public enum PetState { Idle, Thinking, Ready, Filled, Paused, Error }

    // Full atlas cells are drawn once. No independently moving horizontal strips.
    public sealed class PetVisual : FrameworkElement
    {
        private static readonly Dictionary<string, BitmapSource[][]> AtlasCache = new Dictionary<string, BitmapSource[][]>();
        private readonly string assetRoot;
        private BitmapSource[][] frames;
        private readonly DrawingImage[] whales = new DrawingImage[6];
        private DrawingImage whaleHalfBlink, whaleBlink;
        private readonly string whaleDirectory;
        private static readonly int[] Rows = { 0, 7, 3, 4, 8, 5 };
        private static readonly int[][] Durations = {
            new[] { 280,110,110,140,140,320 },
            new[] { 120,120,120,120,120,220 },
            new[] { 140,140,140,280 },
            new[] { 140,140,140,140,280 },
            new[] { 300,300,300,300,300,560 },
            new[] { 140,140,140,140,140,140,140,240 }
        };
        private PetState state;
        private string skin = "personal";
        private bool motion = true;
        private double seconds;
        private bool IsResting { get { return state == PetState.Idle || state == PetState.Paused; } }
        public string CurrentSkin { get { return skin; } }
        public PetState CurrentState { get { return state; } }
        public bool MotionEnabled { get { return motion; } }
        public string LastAssetError { get; private set; }
        public double AnimationTime { get { return seconds; } }
        public string WhaleDirectory { get { return whaleDirectory; } }

        public PetVisual(string workspaceRoot)
        {
            Focusable = false;
            IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            assetRoot = Path.Combine(workspaceRoot, "assets", "wechat-pet");
            whaleDirectory = Path.Combine(assetRoot, "whale");
            frames = LoadAtlas("personal");
        }

        private BitmapSource[][] LoadAtlas(string requested)
        {
            string path = Path.GetFullPath(Path.Combine(assetRoot, requested,
                requested == "panda" ? "tuantuan-sprites.png" : "haihai-sprites.png"));
            BitmapSource[][] result;
            if (AtlasCache.TryGetValue(path, out result)) return result;
            BitmapImage sheet = new BitmapImage();
            sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad;
            sheet.UriSource = new Uri(path); sheet.EndInit(); sheet.Freeze();
            if (sheet.PixelWidth != 1536 || sheet.PixelHeight != 2288)
                throw new InvalidDataException("动画图集尺寸应为 1536 × 2288。");
            result = new BitmapSource[6][];
            for (int action = 0; action < 6; action++) {
                result[action] = new BitmapSource[Durations[action].Length];
                for (int frame = 0; frame < result[action].Length; frame++) {
                    var cell = new CroppedBitmap(sheet, new Int32Rect(frame * 192, Rows[action] * 208, 192, 208));
                    cell.Freeze(); result[action][frame] = cell;
                }
            }
            AtlasCache.Add(path, result);
            return result;
        }

        public int CurrentFrameIndex {
            get {
                if (!motion) return 0;
                if (IsResting) {
                    double blink = seconds % 4.6;
                    return blink < 3.6 || blink >= 3.88 ? 0 : blink < 3.68 || blink >= 3.80 ? 2 : 3;
                }
                int[] durations = Durations[(int)state];
                double elapsed = (seconds * 1000) % durations.Sum();
                for (int i = 0; i < durations.Length; i++) {
                    if (elapsed < durations[i]) return i;
                    elapsed -= durations[i];
                }
                return 0;
            }
        }
        public double CycleSeconds { get { return IsResting ? 4.6 : Durations[(int)state].Sum() / 1000.0; } }

        public bool SelectSkin(string requested)
        {
            if (requested != "personal" && requested != "whale" && requested != "panda") throw new ArgumentException("Unknown skin.");
            LastAssetError = null;
            if (requested == "whale")
            {
                try
                {
                    string[] names = { "resting", "working", "waiting", "celebrate", "sleeping", "error" };
                    for (int i = 0; i < names.Length; i++)
                        if (whales[i] == null) whales[i] = SafeWhaleSvg.Load(Path.Combine(whaleDirectory, names[i] + ".svg"));
                    if (whaleHalfBlink == null) whaleHalfBlink = SafeWhaleSvg.Load(Path.Combine(whaleDirectory, "resting.svg"), .5);
                    if (whaleBlink == null) whaleBlink = SafeWhaleSvg.Load(Path.Combine(whaleDirectory, "resting.svg"), .12);
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException || ex is XmlException || ex is InvalidDataException || ex is FormatException || ex is ArgumentException || ex is NotSupportedException)) throw;
                    skin = "personal"; frames = LoadAtlas("personal");
                    LastAssetError = "鲸鱼素材不可用，已使用个人形象。";
                    InvalidateVisual(); return false;
                }
            }
            if (requested != "whale") frames = LoadAtlas(requested);
            skin = requested; InvalidateVisual(); return true;
        }

        public void SetState(PetState next) { if (state != next) { state = next; seconds = 0; InvalidateVisual(); } }
        public void SetMotion(bool enabled) { motion = enabled; if (!enabled) seconds = 0; InvalidateVisual(); }
        public void ApplyFrame(double time) { seconds = motion ? Math.Max(0, time) : 0; InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth, h = ActualHeight;
            if (w < 1 || h < 1) return;
            double t = motion ? seconds : 0;
            double slow = motion ? Math.Sin(t * Math.PI * 2 / 3.8) : 0;
            if (skin == "whale") DrawWhale(dc, w, h, t, slow);
            else DrawSprite(dc, w, h);
        }

        private void DrawSprite(DrawingContext dc, double w, double h)
        {
            double scale = Math.Min((w - 12) / 192, (h - 18) / 208);
            // All frames retain the same canvas and baseline. No per-frame resizing,
            // alpha cross-fade or row slicing: eyes/hands move in the source artwork.
            var bounds = new Rect((w - 192 * scale) / 2, h - 10 - 208 * scale, 192 * scale, 208 * scale);
            int frame = CurrentFrameIndex;
            // Pausing recommendations is also a resting state: use the same stable
            // body, rather than looping the atlas's yawning/leaning sequence.
            dc.DrawImage(frames[IsResting ? 0 : (int)state][IsResting ? 0 : frame], bounds);
            if (IsResting && frame != 0) {
                // A fixed base preserves every body pixel. Only soft eye-shaped masks
                // reveal the source blink; there are no horizontal moving strips.
                var mask = new DrawingGroup();
                var fade = new RadialGradientBrush(); fade.GradientStops.Add(new GradientStop(Colors.White, 0));fade.GradientStops.Add(new GradientStop(Colors.White, .72));fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                using (var eyes = mask.Open()) {
                    if (skin == "personal") { eyes.DrawEllipse(fade, null, new Point(80,64), 17,15); eyes.DrawEllipse(fade, null, new Point(108,64), 17,15); }
                    else { eyes.DrawEllipse(fade, null, new Point(74,70), 25,24); eyes.DrawEllipse(fade, null, new Point(121,77), 25,24); }
                }
                var brush = new DrawingBrush(mask) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0,0,192,208), ViewportUnits = BrushMappingMode.Absolute, Viewport = bounds, Stretch = Stretch.Fill };
                dc.PushOpacityMask(brush);dc.DrawImage(frames[0][frame], bounds);dc.Pop();
            }
        }

        private void DrawWhale(DrawingContext dc, double w, double h, double t, double slow)
        {
            DrawingImage img = IsResting ? (CurrentFrameIndex == 0 ? whales[0] : CurrentFrameIndex == 3 ? whaleBlink : whaleHalfBlink) : whales[(int)state]; if (img == null) return;
            double scale = Math.Min((w - 32) / img.Width, (h * .75) / img.Height);
            double width = img.Width * scale, height = img.Height * scale;
            double lift = 0, angle = 0, sx = 1, sy = 1;
            if (motion)
            {
                switch (state)
                {
                    case PetState.Idle: break;
                    case PetState.Thinking: angle = Math.Sin(t * 2) * 2.2; sy += slow * .014; break;
                    case PetState.Ready: lift = 2 + Math.Sin(t * 2) * 2; angle = Math.Sin(t * 1.4); break;
                    case PetState.Filled: lift = Math.Pow(Math.Max(0, Math.Sin(t * 2.8)), 2) * 8; sx += Math.Sin(t * 2.8) * .025; sy -= Math.Sin(t * 2.8) * .025; break;
                    case PetState.Paused: break;
                    case PetState.Error: angle = Math.Sin(t * 1.5) * 2.6; break;
                }
            }
            double bottom = h - 14;
            dc.PushTransform(new TranslateTransform(0, -lift));
            dc.PushTransform(new RotateTransform(angle, w * .5, bottom - height * .45));
            dc.PushTransform(new ScaleTransform(sx, sy, w * .5, bottom));
            dc.DrawImage(img, new Rect((w - width) / 2, bottom - height, width, height));
            dc.Pop(); dc.Pop(); dc.Pop();
        }
    }

    // A deliberately small SVG reader for the installed whale pack. It has no
    // browser, URL resolver, script engine, external images, CSS or extensions.
    public static class SafeWhaleSvg
    {
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private static readonly HashSet<string> Tags = new HashSet<string>(new[] { "svg", "path", "defs", "linearGradient", "stop" });
        public static DrawingImage Load(string path, double eyeScale = 1)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length > 2097152) throw new InvalidDataException("Whale SVG missing or oversized.");
            XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2097152 };
            XDocument doc;
            using (XmlReader reader = XmlReader.Create(path, settings)) doc = XDocument.Load(reader, LoadOptions.None);
            XElement root = doc.Root;
            if (root == null || root.Name.LocalName != "svg" || root.Name.NamespaceName != "http://www.w3.org/2000/svg") throw new InvalidDataException("Invalid SVG root.");
            int count = 0;
            foreach (XElement e in root.DescendantsAndSelf())
            {
                if (++count > 5000 || e.Name.NamespaceName != root.Name.NamespaceName || !Tags.Contains(e.Name.LocalName)) throw new InvalidDataException("Unsupported SVG element.");
                ValidateAttributes(e);
                if (e.Nodes().OfType<XText>().Any(n => !String.IsNullOrWhiteSpace(n.Value))) throw new InvalidDataException("Text is not supported.");
            }
            double[] box = (Required(root, "viewBox")).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
            if (box.Length != 4 || box[2] <= 0 || box[3] <= 0 || box[2] > 4096 || box[3] > 4096) throw new InvalidDataException("Invalid SVG bounds.");
            Dictionary<string, Brush> gradients = new Dictionary<string, Brush>(StringComparer.Ordinal);
            foreach (XElement g in root.Descendants().Where(e => e.Name.LocalName == "linearGradient"))
            {
                if (g.Parent.Name.LocalName != "defs" || Required(g, "gradientUnits") != "userSpaceOnUse") throw new InvalidDataException("Unsupported gradient.");
                LinearGradientBrush brush = new LinearGradientBrush();
                brush.MappingMode = BrushMappingMode.Absolute;
                brush.StartPoint = new Point(Number(Required(g, "x1")), Number(Required(g, "y1")));
                brush.EndPoint = new Point(Number(Required(g, "x2")), Number(Required(g, "y2")));
                foreach (XElement stop in g.Elements())
                {
                    if (stop.Name.LocalName != "stop") throw new InvalidDataException("Invalid gradient child.");
                    double offset = Number((string)stop.Attribute("offset") ?? "0");
                    if (offset < 0 || offset > 1) throw new InvalidDataException("Invalid gradient offset.");
                    brush.GradientStops.Add(new GradientStop(ParseColor((string)stop.Attribute("stop-color") ?? "black"), offset));
                }
                if (brush.GradientStops.Count < 1) throw new InvalidDataException("Empty gradient.");
                brush.Freeze(); gradients.Add(Required(g, "id"), brush);
            }
            DrawingGroup drawing = new DrawingGroup();
            using (DrawingContext dc = drawing.Open())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(box[0], box[1], box[2], box[3]));
                foreach (XElement child in root.Elements())
                {
                    if (child.Name.LocalName == "defs") continue;
                    if (child.Name.LocalName != "path" || child.HasElements) throw new InvalidDataException("Unsupported SVG structure.");
                    string fill = (string)child.Attribute("fill") ?? (string)root.Attribute("fill") ?? "black";
                    Brush brush = null;
                    if (fill != "none")
                    {
                        Match match = Regex.Match(fill, "^url\\(#([A-Za-z0-9_-]+)\\)$");
                        if (match.Success) { if (!gradients.TryGetValue(match.Groups[1].Value, out brush)) throw new InvalidDataException("Missing gradient."); }
                        else { brush = new SolidColorBrush(ParseColor(fill)); brush.Freeze(); }
                    }
                    double opacity = Number((string)child.Attribute("opacity") ?? "1");
                    if (opacity < 0 || opacity > 1) throw new InvalidDataException("Invalid opacity.");
                    Geometry geometry = Geometry.Parse("F1 " + Required(child, "d")); geometry.Freeze();
                    bool blink = eyeScale != 1 && fill == "white";
                    dc.PushOpacity(opacity);
                    if (blink) dc.PushTransform(new ScaleTransform(1, eyeScale, geometry.Bounds.X + geometry.Bounds.Width / 2, geometry.Bounds.Y + geometry.Bounds.Height / 2));
                    dc.DrawGeometry(brush, null, geometry);
                    if (blink) dc.Pop();dc.Pop();
                }
            }
            drawing.Freeze(); DrawingImage result = new DrawingImage(drawing); result.Freeze(); return result;
        }
        private static void ValidateAttributes(XElement e)
        {
            string allowed;
            switch (e.Name.LocalName)
            {
                case "svg": allowed = "width height viewBox fill"; break;
                case "path": allowed = "d fill opacity"; break;
                case "defs": allowed = ""; break;
                case "linearGradient": allowed = "id x1 x2 y1 y2 gradientUnits"; break;
                default: allowed = "offset stop-color"; break;
            }
            foreach (XAttribute a in e.Attributes())
                if (!a.IsNamespaceDeclaration && (a.Name.NamespaceName.Length != 0 || !(" " + allowed + " ").Contains(" " + a.Name.LocalName + " ")))
                    throw new InvalidDataException("Unsupported SVG attribute.");
        }
        private static string Required(XElement e, string name) { XAttribute a = e.Attribute(name); if (a == null) throw new InvalidDataException("Missing SVG attribute."); return a.Value; }
        private static double Number(string v) { double n = Double.Parse(v, NumberStyles.Float, CI); if (Double.IsNaN(n) || Double.IsInfinity(n) || Math.Abs(n) > 1000000) throw new InvalidDataException("Invalid SVG number."); return n; }
        private static Color ParseColor(string text)
        {
            if (!Regex.IsMatch(text, "^(#[0-9a-fA-F]{3,8}|[A-Za-z]+)$")) throw new InvalidDataException("Unsupported SVG color.");
            return (Color)ColorConverter.ConvertFromString(text);
        }
    }
}
