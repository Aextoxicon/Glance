using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Material.Icons.Avalonia;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Glance.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Glance.Tests;

public class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new MaterialIconStyles(null));
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

// 无头会话按程序集共享；所有触碰 Avalonia 的代码都必须经它调度到 UI 线程
public static class AvaloniaTest
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AvaloniaTest).Assembly);

    public static Task Run(Action action) =>
        Session.Dispatch(action, System.Threading.CancellationToken.None);

    public static async Task RunAsync(Func<Task> body) =>
        await Session.Dispatch<object?>(async () =>
        {
            await body();
            return null;
        }, System.Threading.CancellationToken.None);
}
