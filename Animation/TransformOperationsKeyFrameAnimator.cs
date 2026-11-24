using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace AgentBuddy.Animation;

/// <summary>
/// Lets style keyframes animate RenderTransform with CSS-like values such as
/// "translateY(14px) scale(0.98)". Avalonia 12 no longer registers an animator
/// for <see cref="ITransform"/> keyframes, so without this the app crashes with
/// "No animator registered for the property RenderTransform".
/// </summary>
public sealed class TransformOperationsKeyFrameAnimator : InterpolatingAnimator<ITransform>
{
    private static readonly TransformOperations Identity = TransformOperations.Parse("none");

    public static void Register() =>
        Avalonia.Animation.Animation.RegisterCustomAnimator<ITransform, TransformOperationsKeyFrameAnimator>();

    public override ITransform Interpolate(double progress, ITransform oldValue, ITransform newValue)
    {
        var from = oldValue as TransformOperations ?? Identity;
        var to = newValue as TransformOperations ?? Identity;
        return TransformOperations.Interpolate(from, to, progress);
    }
}
