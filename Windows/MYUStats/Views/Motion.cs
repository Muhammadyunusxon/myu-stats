using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace MYUStats.Views;

/// <summary>The pill's motion: quick eases with one soft overshoot, like the macOS springs.</summary>
internal static class Motion
{
    /// <summary>A ring coming out of the edge as the pill unfolds; each one trails the one before it,
    /// capped so long stacks stay quick.</summary>
    public static void Unfold(FrameworkElement element, int order, Vector3 from)
    {
        var delay = TimeSpan.FromMilliseconds(60 + Math.Min(order * 45, 180));
        Animate(element, delay, from, 0.4f, TimeSpan.FromMilliseconds(380));
    }

    /// <summary>A card opening beside its ring.</summary>
    public static void Appear(FrameworkElement element, Vector3 from) =>
        Animate(element, TimeSpan.Zero, from, 0.9f, TimeSpan.FromMilliseconds(320));

    /// <summary>New content fading in where the old was, when the card moves to another ring.</summary>
    public static void Crossfade(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(160);
        visual.StartAnimation("Opacity", fade);
    }

    private static void Animate(FrameworkElement element, TimeSpan delay, Vector3 from, float fromScale, TimeSpan duration)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        // Before the first layout the actual size is still zero; the declared one is close enough.
        var width = element.ActualWidth > 0 ? element.ActualWidth : double.IsNaN(element.Width) ? 0 : element.Width;
        var height = element.ActualHeight > 0 ? element.ActualHeight : double.IsNaN(element.Height) ? 0 : element.Height;
        visual.CenterPoint = new Vector3((float)width / 2, (float)height / 2, 0);
        var overshoot = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.3f, 1.08f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(200);
        Delay(fade, delay);
        visual.StartAnimation("Opacity", fade);

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, from);
        slide.InsertKeyFrame(1, Vector3.Zero, overshoot);
        slide.Duration = duration;
        Delay(slide, delay);
        visual.StartAnimation("Translation", slide);

        var grow = compositor.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(0, new Vector3(fromScale, fromScale, 1));
        grow.InsertKeyFrame(1, Vector3.One, overshoot);
        grow.Duration = duration;
        Delay(grow, delay);
        visual.StartAnimation("Scale", grow);
    }

    private static void Delay(KeyFrameAnimation animation, TimeSpan delay)
    {
        animation.DelayTime = delay;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
    }

    /// <summary>Unit vector pointing out through the screen edge, scaled.</summary>
    public static Vector3 Outward(ScreenEdge edge, float distance) => edge switch
    {
        ScreenEdge.Right => new Vector3(distance, 0, 0),
        ScreenEdge.Left => new Vector3(-distance, 0, 0),
        ScreenEdge.Top => new Vector3(0, -distance, 0),
        _ => new Vector3(0, distance, 0),
    };
}
