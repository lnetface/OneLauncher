using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OneLauncher.Converters;
using System;
using System.Collections.Generic;

namespace OneLauncher.Views.Controls;

/// <summary>
/// 统一显示界面图标：普通键读取 Theme.axaml 中随主题切换的位图，
/// JavaLogo/MicrosoftLogo 读取 SKGT 矢量并使用当前前景色绘制，avares 地址用于固定彩色图片。
/// </summary>
public sealed class ThemeIcon : Control
{
    public const string JavaLogoKey = "JavaLogo";
    public const string MicrosoftLogoKey = "MicrosoftLogo";
    private static readonly Dictionary<string, Bitmap> AssetCache = new(StringComparer.Ordinal);

    public static readonly StyledProperty<string?> IconKeyProperty =
        AvaloniaProperty.Register<ThemeIcon, string?>(nameof(IconKey));

    private IDisposable? _resourceSubscription;
    private IImage? _image;

    static ThemeIcon()
    {
        AffectsRender<ThemeIcon>(IconKeyProperty, TextElement.ForegroundProperty);
        IconKeyProperty.Changed.AddClassHandler<ThemeIcon>((icon, _) => icon.SubscribeResource());
    }

    public string? IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeResource();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _resourceSubscription?.Dispose();
        _resourceSubscription = null;
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var vector = GetVector(IconKey);
        if (vector != null)
        {
            var brush = TextElement.GetForeground(this) ?? Brushes.Black;
            var scale = Math.Min(rect.Width, rect.Height) / 24d;
            var offsetX = (rect.Width - 24d * scale) / 2d;
            var offsetY = (rect.Height - 24d * scale) / 2d;
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
            {
                if (IconKey == JavaLogoKey)
                    context.DrawGeometry(null, new Pen(brush, 1.75, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), vector);
                else
                    context.DrawGeometry(brush, null, vector);
            }
            return;
        }

        if (_image != null)
            context.DrawImage(_image, new Rect(_image.Size), rect);
    }

    private static Geometry? GetVector(string? key) => key switch
    {
        JavaLogoKey => VectorIcons.JavaLogo,
        MicrosoftLogoKey => VectorIcons.MicrosoftLogo,
        _ => null
    };

    private void SubscribeResource()
    {
        _resourceSubscription?.Dispose();
        _resourceSubscription = null;
        _image = null;
        if (IconKey?.StartsWith("avares://", StringComparison.Ordinal) == true)
        {
            _image = LoadAsset(IconKey);
        }
        else if (!string.IsNullOrEmpty(IconKey) && GetVector(IconKey) == null && VisualRoot != null)
            _resourceSubscription = this.GetResourceObservable(IconKey).Subscribe(new ResourceObserver(this));
        InvalidateVisual();
    }

    private static Bitmap LoadAsset(string uri)
    {
        if (AssetCache.TryGetValue(uri, out var bitmap))
            return bitmap;
        using var stream = AssetLoader.Open(new Uri(uri));
        bitmap = new Bitmap(stream);
        AssetCache[uri] = bitmap;
        return bitmap;
    }

    private sealed class ResourceObserver(ThemeIcon owner) : IObserver<object?>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void OnNext(object? value)
        {
            owner._image = value as IImage;
            owner.InvalidateVisual();
        }
    }
}
