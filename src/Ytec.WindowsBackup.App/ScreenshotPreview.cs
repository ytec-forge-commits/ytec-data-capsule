using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ytec.WindowsBackup.App;

internal enum ScreenshotPreviewPage
{
    None,
    Main,
    Backup,
    Wifi,
    Bookmarks,
}

internal static class ScreenshotPreview
{
#if YTEC_UI_TEST
    private static bool _captureStarted;

    public static ScreenshotPreviewPage Page { get; private set; }

    public static string? OutputDirectory { get; private set; }

    public static bool IsEnabled => Page != ScreenshotPreviewPage.None;

    public static void Initialize(IEnumerable<string> arguments)
    {
        Page = ScreenshotPreviewPage.None;
        OutputDirectory = null;
        foreach (var argument in arguments)
        {
            const string outputPrefix = "--screenshot-dir=";
            if (argument.StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase))
            {
                OutputDirectory = argument.Substring(outputPrefix.Length).Trim();
                continue;
            }

            const string pagePrefix = "--screenshot=";
            if (!argument.StartsWith(pagePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Page = argument.Substring(pagePrefix.Length).Trim().ToLowerInvariant() switch
            {
                "main" => ScreenshotPreviewPage.Main,
                "backup" => ScreenshotPreviewPage.Backup,
                "wifi" => ScreenshotPreviewPage.Wifi,
                "bookmarks" or "bookmark" => ScreenshotPreviewPage.Bookmarks,
                _ => ScreenshotPreviewPage.None,
            };
        }
    }

    public static void AttachCapture(Window window, ScreenshotPreviewPage page)
    {
        if (Page != page || string.IsNullOrWhiteSpace(OutputDirectory))
        {
            return;
        }

        window.ContentRendered += (_, _) =>
        {
            if (_captureStarted)
            {
                return;
            }

            _captureStarted = true;
            window.Dispatcher.BeginInvoke(
                new Action(() => SaveAndExit(window, page)),
                DispatcherPriority.ApplicationIdle);
        };
    }

    private static void SaveAndExit(Window window, ScreenshotPreviewPage page)
    {
        try
        {
            if (window.Content is not FrameworkElement content ||
                content.ActualWidth <= 0 || content.ActualHeight <= 0)
            {
                throw new InvalidOperationException(
                    "The screenshot surface is not ready.");
            }

            var transform = PresentationSource.FromVisual(content)?
                .CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            var dpiScaleX = transform.M11;
            var dpiScaleY = transform.M22;
            var width = Math.Max(
                1,
                (int)Math.Ceiling(content.ActualWidth * dpiScaleX));
            var height = Math.Max(
                1,
                (int)Math.Ceiling(content.ActualHeight * dpiScaleY));
            var bitmap = new RenderTargetBitmap(
                width,
                height,
                96 * dpiScaleX,
                96 * dpiScaleY,
                PixelFormats.Pbgra32);
            var composed = new DrawingVisual();
            using (var drawing = composed.RenderOpen())
            {
                drawing.DrawRectangle(
                    window.Background ?? System.Windows.Media.Brushes.White,
                    null,
                    new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                drawing.DrawRectangle(
                    new VisualBrush(content),
                    null,
                    new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            }
            bitmap.Render(composed);

            Directory.CreateDirectory(OutputDirectory!);
            var outputPath = Path.Combine(
                OutputDirectory!,
                page.ToString().ToLowerInvariant() + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(outputPath))
            {
                encoder.Save(stream);
            }
        }
        finally
        {
            System.Windows.Application.Current.Shutdown();
            Environment.Exit(0);
        }
    }
#else
    public static ScreenshotPreviewPage Page => ScreenshotPreviewPage.None;

    public static string? OutputDirectory => null;

    public static bool IsEnabled => false;

    public static void Initialize(IEnumerable<string> arguments)
    {
    }

    public static void AttachCapture(Window window, ScreenshotPreviewPage page)
    {
    }
#endif
}
