using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Skia;

namespace Glance.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                // Vulkan优先Egl回退
                .With(new AndroidPlatformOptions
                {
                    RenderingMode = new[] { AndroidRenderingMode.Vulkan, AndroidRenderingMode.Egl }
                })
                .With(new SkiaOptions { MaxGpuResourceSizeBytes = 128 * 1024 * 1024 });
        }
    }
}
