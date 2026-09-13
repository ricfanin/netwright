using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Netwright.Engine.Uia;

namespace Netwright.Engine.Capture;

public sealed record CapturedImage(byte[] Png, int Width, int Height, int SourceWidth, int SourceHeight);

/// <summary>
/// Captures windows with <c>PrintWindow(PW_RENDERFULLCONTENT)</c>, which renders the window itself
/// rather than copying the screen, so the capture is correct even when other windows cover it.
/// </summary>
internal static class WindowCapture
{
    public static CapturedImage Capture(nint windowHandle, Rectangle? elementBounds, int maxSize)
    {
        if (windowHandle == 0 || !NativeMethods.IsWindow(windowHandle))
        {
            throw new NetwrightException(ErrorCodes.NotSupported, "The element has no window to capture.");
        }

        if (NativeMethods.IsIconic(windowHandle))
        {
            throw new NetwrightException(
                ErrorCodes.NotSupported,
                "The window is minimized, and Windows does not render minimized windows.",
                "Restore it with desktop_window action=restore, then capture again.");
        }

        if (!NativeMethods.GetWindowRect(windowHandle, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            throw new NetwrightException(ErrorCodes.NotSupported, "The window has no visible area to capture.");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        using var full = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(full))
        {
            var hdc = graphics.GetHdc();
            try
            {
                if (!NativeMethods.PrintWindow(windowHandle, hdc, NativeMethods.PwRenderFullContent))
                {
                    throw new NetwrightException(ErrorCodes.NotSupported, "Windows could not render the window (PrintWindow failed).");
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        var crop = new Rectangle(0, 0, width, height);
        if (elementBounds is { IsEmpty: false } bounds)
        {
            crop = Rectangle.Intersect(crop, new Rectangle(bounds.X - rect.Left, bounds.Y - rect.Top, bounds.Width, bounds.Height));
            if (crop.IsEmpty)
            {
                throw new NetwrightException(ErrorCodes.NotSupported, "The element is outside its window's visible area.", "Scroll it into view first.");
            }
        }

        var scale = Math.Min(1.0, (double)maxSize / Math.Max(crop.Width, crop.Height));
        var outWidth = Math.Max(1, (int)Math.Round(crop.Width * scale));
        var outHeight = Math.Max(1, (int)Math.Round(crop.Height * scale));

        using var output = new Bitmap(outWidth, outHeight, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(full, new Rectangle(0, 0, outWidth, outHeight), crop, GraphicsUnit.Pixel);
        }

        using var stream = new MemoryStream();
        output.Save(stream, ImageFormat.Png);
        return new CapturedImage(stream.ToArray(), outWidth, outHeight, crop.Width, crop.Height);
    }
}
