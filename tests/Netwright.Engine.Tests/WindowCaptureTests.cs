using System.Drawing;
using Netwright.Engine.Capture;

namespace Netwright.Engine.Tests;

public class WindowCaptureTests
{
    [Fact]
    public void An_element_capture_starts_at_the_element_and_is_scaled_to_fit()
    {
        var window = new Rectangle(400, 160, 1900, 1100);
        var element = new Rectangle(412, 180, 1791, 1025);

        var plan = WindowCapture.Plan(window, element, 1280);

        Assert.Equal(new Rectangle(12, 20, 1791, 1025), plan.Crop);
        Assert.Equal(new Point(412, 180), plan.Origin);
        Assert.Equal((1280, 733), (plan.Width, plan.Height));
    }

    [Fact]
    public void A_window_capture_starts_at_the_window_corner_and_keeps_its_size_when_small()
    {
        var plan = WindowCapture.Plan(new Rectangle(-8, 30, 640, 480), null, 1280);

        Assert.Equal(new Rectangle(0, 0, 640, 480), plan.Crop);
        Assert.Equal(new Point(-8, 30), plan.Origin);
        Assert.Equal((640, 480), (plan.Width, plan.Height));
    }

    [Fact]
    public void An_element_partly_outside_its_window_is_clipped_and_the_origin_moves_with_the_clip()
    {
        var plan = WindowCapture.Plan(new Rectangle(100, 100, 800, 600), new Rectangle(50, 650, 200, 100), 1280);

        Assert.Equal(new Rectangle(0, 550, 150, 50), plan.Crop);
        Assert.Equal(new Point(100, 650), plan.Origin);
        Assert.Equal((150, 50), (plan.Width, plan.Height));
    }

    [Fact]
    public void An_image_pixel_maps_back_to_the_screen_through_origin_and_scale()
    {
        // The example from the screenshot text: 640x320 (scaled from 1280x640), origin 412,180 on screen.
        var plan = WindowCapture.Plan(new Rectangle(0, 0, 2560, 1440), new Rectangle(412, 180, 1280, 640), 640);

        var screen = new Point(plan.Origin.X + (106 * plan.Crop.Width / plan.Width), plan.Origin.Y + (7 * plan.Crop.Height / plan.Height));

        Assert.Equal((640, 320), (plan.Width, plan.Height));
        Assert.Equal(new Point(624, 194), screen);
    }

    [Fact]
    public void An_element_outside_its_window_cannot_be_captured()
    {
        var ex = Assert.Throws<NetwrightException>(() => WindowCapture.Plan(new Rectangle(0, 0, 800, 600), new Rectangle(900, 0, 50, 50), 1280));

        Assert.Equal(ErrorCodes.NotSupported, ex.Code);
    }
}
