using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Netwright.Engine.Uia;

namespace Netwright.Engine.Capture;

/// <param name="Png">The image.</param>
/// <param name="Width">Width of the image.</param>
/// <param name="Height">Height of the image.</param>
/// <param name="SourceWidth">Width of the captured area in screen pixels, before scaling.</param>
/// <param name="SourceHeight">Height of the captured area in screen pixels, before scaling.</param>
/// <param name="Origin">Top-left corner of the captured area in screen pixels.</param>
public sealed record CapturedImage(byte[] Png, int Width, int Height, int SourceWidth, int SourceHeight, Point Origin);

/// <summary>What to cut from a window capture and how large to make it.</summary>
/// <param name="Crop">The captured area, relative to the window.</param>
/// <param name="Width">Width of the output image.</param>
/// <param name="Height">Height of the output image.</param>
/// <param name="Origin">Top-left corner of the captured area in screen pixels.</param>
internal readonly record struct CapturePlan(Rectangle Crop, int Width, int Height, Point Origin);

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

        var plan = Plan(new Rectangle(rect.Left, rect.Top, width, height), elementBounds, maxSize);

        using var output = new Bitmap(plan.Width, plan.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(full, new Rectangle(0, 0, plan.Width, plan.Height), plan.Crop, GraphicsUnit.Pixel);
        }

        using var stream = new MemoryStream();
        output.Save(stream, ImageFormat.Png);
        return new CapturedImage(stream.ToArray(), plan.Width, plan.Height, plan.Crop.Width, plan.Crop.Height, plan.Origin);
    }

    /// <summary>
    /// The part of the window to keep (the element clipped to the window, or the whole window), the
    /// output size that fits <paramref name="maxSize"/>, and where the kept part starts on the screen.
    /// Image pixel (x, y) is at screen point origin + (x, y) * crop size / output size.
    /// </summary>
    /// <param name="window">The window in screen pixels.</param>
    /// <param name="elementBounds">The element in screen pixels, or null for the whole window.</param>
    /// <param name="maxSize">Longest side of the output image.</param>
    internal static CapturePlan Plan(Rectangle window, Rectangle? elementBounds, int maxSize)
    {
        var crop = new Rectangle(0, 0, window.Width, window.Height);
        if (elementBounds is { IsEmpty: false } bounds)
        {
            crop = Rectangle.Intersect(crop, new Rectangle(bounds.X - window.Left, bounds.Y - window.Top, bounds.Width, bounds.Height));
            if (crop.IsEmpty)
            {
                throw new NetwrightException(ErrorCodes.NotSupported, "The element is outside its window's visible area.", "Scroll it into view first.");
            }
        }

        var scale = Math.Min(1.0, (double)maxSize / Math.Max(crop.Width, crop.Height));
        return new CapturePlan(
            crop,
            Math.Max(1, (int)Math.Round(crop.Width * scale)),
            Math.Max(1, (int)Math.Round(crop.Height * scale)),
            new Point(window.Left + crop.X, window.Top + crop.Y));
    }
}
