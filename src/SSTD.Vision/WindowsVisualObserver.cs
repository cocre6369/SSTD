using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using SSTD.Core;
using SSTD.Security;

namespace SSTD.Vision;

public sealed class WindowsVisualObserver : IVisualObserver
{
    private readonly Dictionary<string, (nint Window, Rect Bounds, DateTimeOffset Time)> _targets = new();
    private int _index;
    public async Task<ActionResult> InspectAsync(WindowSnapshot window, string? elementId, bool allowModelImage, CancellationToken ct)
    {
        if (window.Elements.Count == 0 && !window.IsBlocked) return ActionResult.Fail("OCR requires UI Automation password screening. Enable UI Automation or use a structured accessible application.");
        if (window.IsBlocked || window.IsPasswordContext || window.IsElevated || SecurityEngine.TargetBlock(window).Length > 0) return ActionResult.Fail("Screenshots of protected applications and authentication contexts are forbidden.");
        var region = elementId is null ? null : window.Find(elementId);
        if (elementId is not null && region is null) return ActionResult.Fail("The visual region ID is stale.");
        if (region?.Offscreen == true) return ActionResult.Fail("Scroll the target region into view before capturing it.");
        var handle = window.Handle;
        if (!NativeCapture.Bounds(handle, out var bounds)) return ActionResult.Fail("The window has no capturable bounds.");
        var rect = region?.Bounds ?? bounds;
        var x = (int)Math.Max(bounds.X, rect.X); var y = (int)Math.Max(bounds.Y, rect.Y);
        var right = (int)Math.Min(bounds.X + bounds.Width, rect.X + rect.Width); var bottom = (int)Math.Min(bounds.Y + bounds.Height, rect.Y + rect.Height);
        if (right <= x || bottom <= y) return ActionResult.Fail("The target is outside the visible window.");
        if ((right - x) * (bottom - y) > 16_000_000) return ActionResult.Fail("The capture region is too large; select a smaller relevant region.");
        byte[] png;
        try
        {
            using var bitmap = new Bitmap(right - x, bottom - y, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(x, y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            // The foreground is checked again to avoid sending an intervening password dialog to a model.
            if (NativeCapture.Foreground() != handle) return ActionResult.Fail("The foreground changed during capture; the image was discarded.");
            using var ms = new MemoryStream(); bitmap.Save(ms, ImageFormat.Png); png = ms.ToArray();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ExternalException or ArgumentException) { return ActionResult.Fail("Windows could not capture this region. Protected or minimized surfaces are not captured."); }
        ct.ThrowIfCancellationRequested();
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return ActionResult.Fail("Windows OCR has no installed recognition language. Add an OCR-capable language pack in Windows manually, or use an application with accessibility support.");
        try
        {
            using var stream = new InMemoryRandomAccessStream(); await stream.WriteAsync(png.AsBuffer()).AsTask(ct).ConfigureAwait(false); stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
            var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(ct).ConfigureAwait(false);
            if (software.PixelWidth > OcrEngine.MaxImageDimension || software.PixelHeight > OcrEngine.MaxImageDimension) return ActionResult.Fail("This region exceeds Windows OCR's dimension limit. Capture a smaller element.");
            var result = await engine.RecognizeAsync(software).AsTask(ct).ConfigureAwait(false);
            if (NativeCapture.Foreground() != handle) return ActionResult.Fail("Window changed during OCR; the observation was discarded.");
            if (SensitiveData.ContainsSecret(result.Text) || System.Text.RegularExpressions.Regex.IsMatch(result.Text, @"password|passcode|verification code|recovery code|private key|sign in to|enter your code", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return ActionResult.Fail("Possible credential content was detected. The screenshot and OCR text were discarded; authenticate manually.");
            var elements = new List<UiElement>(window.Elements);
            foreach (var line in result.Lines.Take(120))
            {
                ct.ThrowIfCancellationRequested();
                var boxes = line.Words.Select(w => w.BoundingRect).ToList(); if (boxes.Count == 0) continue;
                var left = boxes.Min(b => b.X); var top = boxes.Min(b => b.Y); var r = boxes.Max(b => b.X + b.Width); var btm = boxes.Max(b => b.Y + b.Height);
                var target = new Rect(x + left, y + top, r - left, btm - top); var id = "ocr" + (++_index);
                elements.Add(new UiElement { Id = id, Name = SensitiveData.Redact(line.Text), ControlType = "VisualText", Bounds = target, Enabled = true, Source = "OCR" });
                _targets[id] = (handle, target, DateTimeOffset.UtcNow);
            }
            if (_targets.Count > 1000) { foreach (var key in _targets.Keys.Take(500).ToList()) _targets.Remove(key); }
            var image = allowModelImage ? Convert.ToBase64String(png) : null;
            var observation = window with { Elements = elements, ScreenshotBase64 = image, CapturedAt = DateTimeOffset.UtcNow,
                Warnings = [.. window.Warnings, "On-demand OCR fallback. Visual text boxes are observations, not semantic button guarantees. Reinspection is required before acting."] };
            return new(true, result.Lines.Count > 0, $"Windows OCR recognized {result.Lines.Count} text lines. Image {(image is null ? "was not retained or sent to the model" : "is available only to the local vision model; never saved")}.", observation, image);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        { return ActionResult.Fail("Windows OCR failed: " + SensitiveData.SafeError(e)); }
    }
    public bool TryResolve(WindowSnapshot w, string id, out Rect rect)
    {
        rect = new(0, 0, 0, 0);
        if (!_targets.TryGetValue(id, out var target) || target.Window != w.Handle || DateTimeOffset.UtcNow - target.Time > TimeSpan.FromSeconds(20)) return false;
        rect = target.Bounds; return true;
    }
}
internal static class NativeCapture
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(nint h, out RECT rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(nint h);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    public static nint Foreground() => GetForegroundWindow();
    public static bool Bounds(nint h, out Rect rect)
    {
        rect = new(0, 0, 0, 0); if (h == 0 || IsIconic(h) || !GetWindowRect(h, out var r)) return false;
        rect = new(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top)); return rect.HasArea;
    }
}
