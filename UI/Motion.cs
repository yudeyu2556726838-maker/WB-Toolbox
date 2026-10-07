using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WBToolbox.Native.UI
{
    internal static class Motion
    {
        private static readonly DependencyProperty MotionTransformsProperty = DependencyProperty.RegisterAttached(
            "MotionTransforms",
            typeof(MotionTransforms),
            typeof(Motion),
            new PropertyMetadata(null));

        private static readonly DependencyProperty ContinuousMotionProperty = DependencyProperty.RegisterAttached(
            "ContinuousMotion",
            typeof(ContinuousMotion),
            typeof(Motion),
            new PropertyMetadata(null));

        private sealed class MotionTransforms
        {
            internal ScaleTransform Scale;
            internal TranslateTransform Translate;
        }

        private enum ContinuousMotionKind
        {
            Pulse,
            Float,
            Drift
        }

        private sealed class ContinuousMotion
        {
            internal ContinuousMotionKind Kind;
            internal double Distance;
            internal int Milliseconds;
            internal int DelayMilliseconds;
            internal bool Running;
            internal List<AnimationClock> Clocks;
        }

        internal static void AttachButton(ButtonBase button)
        {
            MotionTransforms transforms = EnsureTransforms(button);
            button.MouseEnter += delegate { AnimateTo(transforms.Translate, TranslateTransform.YProperty, -1, 170); };
            button.MouseLeave += delegate
            {
                AnimateTo(transforms.Translate, TranslateTransform.YProperty, 0, 170);
                AnimateTo(transforms.Scale, ScaleTransform.ScaleXProperty, 1, 170);
                AnimateTo(transforms.Scale, ScaleTransform.ScaleYProperty, 1, 170);
            };
            button.PreviewMouseLeftButtonDown += delegate
            {
                AnimateTo(transforms.Translate, TranslateTransform.YProperty, 1, 90);
                AnimateTo(transforms.Scale, ScaleTransform.ScaleXProperty, 0.98, 90);
                AnimateTo(transforms.Scale, ScaleTransform.ScaleYProperty, 0.98, 90);
            };
            button.PreviewMouseLeftButtonUp += delegate
            {
                AnimateTo(transforms.Translate, TranslateTransform.YProperty, button.IsMouseOver ? -1 : 0, 150);
                AnimateTo(transforms.Scale, ScaleTransform.ScaleXProperty, 1, 150);
                AnimateTo(transforms.Scale, ScaleTransform.ScaleYProperty, 1, 150);
            };
        }

        internal static void SpringIn(FrameworkElement element, bool initial)
        {
            MotionTransforms transforms = EnsureTransforms(element);
            int duration = initial ? 840 : 540;
            double startY = initial ? 18 : 13;
            double startScale = initial ? 0.94 : 0.975;

            element.Opacity = 0;
            transforms.Translate.Y = startY;
            transforms.Scale.ScaleX = startScale;
            transforms.Scale.ScaleY = startScale;

            DoubleAnimationUsingKeyFrames opacity = new DoubleAnimationUsingKeyFrames();
            opacity.Duration = TimeSpan.FromMilliseconds(duration);
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.62)));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)));

            DoubleAnimationUsingKeyFrames y = new DoubleAnimationUsingKeyFrames();
            y.Duration = opacity.Duration;
            y.KeyFrames.Add(new EasingDoubleKeyFrame(startY, KeyTime.FromPercent(0)));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(-2, KeyTime.FromPercent(0.62)));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.82)));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));

            DoubleAnimationUsingKeyFrames scale = new DoubleAnimationUsingKeyFrames();
            scale.Duration = opacity.Duration;
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(startScale, KeyTime.FromPercent(0)));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(initial ? 1.012 : 1.006, KeyTime.FromPercent(0.62)));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(initial ? 0.996 : 0.998, KeyTime.FromPercent(0.82)));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)));

            element.BeginAnimation(UIElement.OpacityProperty, opacity);
            transforms.Translate.BeginAnimation(TranslateTransform.YProperty, y);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
        }

        internal static void Pop(FrameworkElement element)
        {
            MotionTransforms transforms = EnsureTransforms(element);
            DoubleAnimationUsingKeyFrames scale = new DoubleAnimationUsingKeyFrames();
            scale.Duration = TimeSpan.FromMilliseconds(430);
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(0.96, KeyTime.FromPercent(0)));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.025, KeyTime.FromPercent(0.62)));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)));
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
        }

        internal static void Pulse(FrameworkElement element)
        {
            ContinuousMotion motion = new ContinuousMotion
            {
                Kind = ContinuousMotionKind.Pulse
            };
            element.SetValue(ContinuousMotionProperty, motion);
            StartContinuousMotion(element, motion);
        }

        private static void StartPulse(FrameworkElement element, MotionTransforms transforms, ContinuousMotion motion)
        {
            DoubleAnimation opacity = new DoubleAnimation(0.62, 1, TimeSpan.FromMilliseconds(1050));
            opacity.AutoReverse = true;
            opacity.RepeatBehavior = RepeatBehavior.Forever;
            LimitFrameRate(opacity);
            DoubleAnimation scale = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(1050));
            scale.AutoReverse = true;
            scale.RepeatBehavior = RepeatBehavior.Forever;
            LimitFrameRate(scale);
            ApplyContinuousAnimation(element, UIElement.OpacityProperty, opacity, motion);
            ApplyContinuousAnimation(transforms.Scale, ScaleTransform.ScaleXProperty, scale, motion);
            ApplyContinuousAnimation(transforms.Scale, ScaleTransform.ScaleYProperty, scale.Clone(), motion);
        }

        internal static void Float(FrameworkElement element)
        {
            Float(element, 1.4, 1150, 0);
        }

        internal static void Float(FrameworkElement element, double distance, int milliseconds, int delayMilliseconds)
        {
            ContinuousMotion motion = new ContinuousMotion
            {
                Kind = ContinuousMotionKind.Float,
                Distance = distance,
                Milliseconds = milliseconds,
                DelayMilliseconds = delayMilliseconds
            };
            element.SetValue(ContinuousMotionProperty, motion);
            StartContinuousMotion(element, motion);
        }

        private static void StartFloat(FrameworkElement element, MotionTransforms transforms, ContinuousMotion motion)
        {
            double distance = motion.Distance;
            int milliseconds = motion.Milliseconds;
            int delayMilliseconds = motion.DelayMilliseconds;
            DoubleAnimation animation = new DoubleAnimation(-distance, distance, TimeSpan.FromMilliseconds(milliseconds));
            animation.AutoReverse = true;
            animation.RepeatBehavior = RepeatBehavior.Forever;
            animation.BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds);
            animation.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            LimitFrameRate(animation);
            ApplyContinuousAnimation(transforms.Translate, TranslateTransform.YProperty, animation, motion);
        }

        internal static void Drift(FrameworkElement element)
        {
            ContinuousMotion motion = new ContinuousMotion
            {
                Kind = ContinuousMotionKind.Drift
            };
            element.SetValue(ContinuousMotionProperty, motion);
            StartContinuousMotion(element, motion);
        }

        private static void StartDrift(MotionTransforms transforms, ContinuousMotion motion)
        {
            DoubleAnimation x = new DoubleAnimation(-5, 7, TimeSpan.FromSeconds(18));
            x.AutoReverse = true;
            x.RepeatBehavior = RepeatBehavior.Forever;
            x.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            LimitFrameRate(x);
            DoubleAnimation y = new DoubleAnimation(-3, 6, TimeSpan.FromSeconds(18));
            y.AutoReverse = true;
            y.RepeatBehavior = RepeatBehavior.Forever;
            y.EasingFunction = x.EasingFunction;
            LimitFrameRate(y);
            ApplyContinuousAnimation(transforms.Translate, TranslateTransform.XProperty, x, motion);
            ApplyContinuousAnimation(transforms.Translate, TranslateTransform.YProperty, y, motion);
        }

        internal static void PauseContinuousIn(DependencyObject root)
        {
            VisitContinuousMotionTree(root, false);
        }

        internal static void ResumeContinuousIn(DependencyObject root)
        {
            VisitContinuousMotionTree(root, true);
        }

        internal static bool IsContinuousMotionRunning(FrameworkElement element)
        {
            ContinuousMotion motion = element == null
                ? null
                : element.GetValue(ContinuousMotionProperty) as ContinuousMotion;
            return motion != null && motion.Running;
        }

        private static void VisitContinuousMotionTree(DependencyObject root, bool resume)
        {
            if (root == null)
            {
                return;
            }

            FrameworkElement element = root as FrameworkElement;
            ContinuousMotion motion = element == null
                ? null
                : element.GetValue(ContinuousMotionProperty) as ContinuousMotion;
            if (motion != null)
            {
                if (resume)
                {
                    StartContinuousMotion(element, motion);
                }
                else
                {
                    StopContinuousMotion(element, motion);
                }
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                VisitContinuousMotionTree(VisualTreeHelper.GetChild(root, index), resume);
            }
        }

        private static void StartContinuousMotion(FrameworkElement element, ContinuousMotion motion)
        {
            if (motion.Running)
            {
                return;
            }

            if (motion.Clocks != null)
            {
                foreach (AnimationClock clock in motion.Clocks)
                {
                    if (clock.Controller != null)
                    {
                        clock.Controller.Resume();
                    }
                }
                motion.Running = true;
                return;
            }

            MotionTransforms transforms = EnsureTransforms(element);
            motion.Clocks = new List<AnimationClock>();
            motion.Running = true;
            if (motion.Kind == ContinuousMotionKind.Pulse)
            {
                StartPulse(element, transforms, motion);
            }
            else if (motion.Kind == ContinuousMotionKind.Float)
            {
                StartFloat(element, transforms, motion);
            }
            else
            {
                StartDrift(transforms, motion);
            }
        }

        private static void StopContinuousMotion(FrameworkElement element, ContinuousMotion motion)
        {
            if (!motion.Running)
            {
                return;
            }

            motion.Running = false;
            if (motion.Clocks == null)
            {
                return;
            }

            foreach (AnimationClock clock in motion.Clocks)
            {
                if (clock.Controller != null)
                {
                    clock.Controller.Pause();
                }
            }
        }

        private static void ApplyContinuousAnimation(
            DependencyObject target,
            DependencyProperty property,
            AnimationTimeline animation,
            ContinuousMotion motion)
        {
            AnimationClock clock = (AnimationClock)animation.CreateClock(true);
            UIElement visual = target as UIElement;
            if (visual != null)
            {
                visual.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
            }
            else
            {
                ((Animatable)target).ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
            }
            motion.Clocks.Add(clock);
        }

        internal static void LimitFrameRate(Timeline timeline)
        {
            Timeline.SetDesiredFrameRate(timeline, 30);
        }

        internal static void FadeSlideIn(FrameworkElement element)
        {
            MotionTransforms transforms = EnsureTransforms(element);
            element.Opacity = 0;
            transforms.Translate.Y = -8;
            transforms.Scale.ScaleX = 0.97;
            transforms.Scale.ScaleY = 0.97;

            DoubleAnimation opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280));
            opacity.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation y = new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(280));
            y.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 };
            DoubleAnimation scale = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(280));
            scale.EasingFunction = y.EasingFunction;
            element.BeginAnimation(UIElement.OpacityProperty, opacity);
            transforms.Translate.BeginAnimation(TranslateTransform.YProperty, y);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
        }

        internal static void FadeScaleOut(FrameworkElement element, Action completed)
        {
            FadeScaleOut(element, 0.76, completed);
        }

        internal static void FadeScaleOut(FrameworkElement element, double targetScale, Action completed)
        {
            MotionTransforms transforms = EnsureTransforms(element);
            DoubleAnimation opacity = new DoubleAnimation(element.Opacity, 0, TimeSpan.FromMilliseconds(170));
            opacity.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            DoubleAnimation scale = new DoubleAnimation(transforms.Scale.ScaleX, targetScale, TimeSpan.FromMilliseconds(190));
            scale.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            if (completed != null)
            {
                scale.Completed += delegate { completed(); };
            }
            element.BeginAnimation(UIElement.OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, scale, HandoffBehavior.SnapshotAndReplace);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone(), HandoffBehavior.SnapshotAndReplace);
        }

        internal static void BlurImageOut(Image image)
        {
            if (image == null)
            {
                return;
            }
            BlurEffect blur = new BlurEffect
            {
                KernelType = KernelType.Gaussian,
                Radius = 0,
                RenderingBias = RenderingBias.Performance
            };
            image.Effect = blur;
            DoubleAnimation radius = new DoubleAnimation(0, 5, TimeSpan.FromMilliseconds(180));
            radius.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn };
            blur.BeginAnimation(BlurEffect.RadiusProperty, radius, HandoffBehavior.SnapshotAndReplace);
        }

        private static MotionTransforms EnsureTransforms(FrameworkElement element)
        {
            MotionTransforms existing = element.GetValue(MotionTransformsProperty) as MotionTransforms;
            if (existing != null)
            {
                return existing;
            }

            TransformGroup group = new TransformGroup();
            ScaleTransform scale = new ScaleTransform(1, 1);
            TranslateTransform translate = new TranslateTransform();
            group.Children.Add(scale);
            group.Children.Add(translate);
            element.RenderTransform = group;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            MotionTransforms created = new MotionTransforms { Scale = scale, Translate = translate };
            element.SetValue(MotionTransformsProperty, created);
            return created;
        }

        private static void AnimateTo(Animatable target, DependencyProperty property, double value, int milliseconds)
        {
            DoubleAnimation animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds));
            animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
