using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Glance.Models;
using Glance.Processing;
using Glance.Utils;
using Glance.ViewModels;
using Glance.Views;
using Xunit;
using Xunit.Abstractions;

namespace Glance.Tests;

// 验证真实操作探针（文件打开首帧/滚动卡顿）在应用内真正触发
[Collection("Avalonia")]
public class ProbeTests
{
    private readonly ITestOutputHelper _output;
    private static bool Native => VmHarness.NativeParserAvailable();

    public ProbeTests(ITestOutputHelper output) => _output = output;

    private static string SynthRust(int target)
    {
        const string block =
@"
use std::collections::HashMap;
fn main() {
    let x = 42;
    println!(""{}"", x);
}
";
        var sb = new StringBuilder(block.Length * (target / block.Length + 1));
        while (sb.Length < target) sb.Append(block);
        return sb.ToString();
    }

    [Fact]
    public Task 打开文件_首帧信号触发()
    {
        if (!Native) return Task.CompletedTask;

        return AvaloniaTest.RunAsync(async () =>
        {
            var repo = new ScriptedRepo();
            var fs = new FakeFileSystem();
            var vm = new MainViewModel(fs, repo, new UniffiCodeParser());
            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show();

            Trace.Enabled = true;
            Trace.Drain(); // 清空基线

            try
            {
                var content = SynthRust(128 * 1024);
                var path = VmHarness.P("probe_128k.rs");
                repo.Text(path, content);
                await vm.SelectItem(VmHarness.Item(repo, path, content.Length));

                // 等首帧信号（DispatcherPriority.Render跑完）
                await Task.Delay(300);

                var lines = Trace.Drain();
                foreach (var l in lines) _output.WriteLine(l);

                Assert.Contains(lines, l => l.Contains("select.start"));
                Assert.Contains(lines, l => l.Contains("select.end"));
                Assert.Contains(lines, l => l.Contains("select.painted"));
            }
            finally
            {
                window.Close();
                vm.Dispose();
            }
        });
    }

    [Fact]
    public Task 大文件打开_首帧延迟可测()
    {
        if (!Native) return Task.CompletedTask;

        return AvaloniaTest.RunAsync(async () =>
        {
            var repo = new ScriptedRepo();
            var fs = new FakeFileSystem();
            var vm = new MainViewModel(fs, repo, new UniffiCodeParser());
            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show();

            Trace.Enabled = true;
            Trace.Drain();

            try
            {
                var content = SynthRust(256 * 1024);
                var path = VmHarness.P("probe_256k.rs");
                repo.Text(path, content);
                await vm.SelectItem(VmHarness.Item(repo, path, content.Length));
                await Task.Delay(300);

                var lines = Trace.Drain();
                foreach (var l in lines) _output.WriteLine(l);

                // 全链路延迟=painted.total_ms-start.total_ms（均自同一Base起算）
                var start = Array.Find(lines, l => l.Contains("select.start"));
                var painted = Array.Find(lines, l => l.Contains("select.painted"));
                Assert.NotNull(start);
                Assert.NotNull(painted);
                var total = ParseTotal(painted!) - ParseTotal(start!);
                _output.WriteLine($"[PROBE] 256KB 打开全链路(含首帧) = {total} ms");
                Assert.True(total > 0, "全链路延迟必须可读");
            }
            finally
            {
                window.Close();
                vm.Dispose();
            }
        });
    }

    private static double ParseTotal(string line)
    {
        var i = line.IndexOf("total_ms=", StringComparison.Ordinal);
        if (i < 0) return -1;
        var s = line.Substring(i + "total_ms=".Length);
        var sp = s.IndexOf(' ');
        if (sp > 0) s = s.Substring(0, sp);
        return double.TryParse(s, out var v) ? v : -1;
    }

    [Fact]
    public Task 诊断基准模式_打开文件夹自动展开并逐文件计时()
    {
        if (!Native) return Task.CompletedTask;

        return AvaloniaTest.RunAsync(async () =>
        {
            var repo = new ScriptedRepo();
            var fs = new FakeFileSystem();
            var vm = new MainViewModel(fs, repo, new UniffiCodeParser());
            var view = new MainView { DataContext = vm };
            var window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show();

            using var settings = new SettingsFileScope();
            repo.Listing(VmHarness.Root, new IArtifact[]
            {
                Artifacts.Dir(VmHarness.P("src")),
                Artifacts.Dir(VmHarness.P("tests")),
                Artifacts.File(VmHarness.P("a.rs")),
            });
            repo.Listing(VmHarness.P("src"), new IArtifact[]
            {
                Artifacts.File(VmHarness.P("main.cs")),
                Artifacts.File(VmHarness.P("note.md")),
            });
            repo.Listing(VmHarness.P("tests"), new IArtifact[]
            {
                Artifacts.File(VmHarness.P("t.py")),
            });
            fs.PutName(VmHarness.Root, "Collisions");
            fs.PutName(VmHarness.P("src"), "src");
            fs.PutName(VmHarness.P("tests"), "tests");
            repo.Text(VmHarness.P("a.rs"), "fn main() {}\n");
            repo.Text(VmHarness.P("main.cs"), "class C {}\n");
            repo.Text(VmHarness.P("t.py"), "def f(): pass\n");
            repo.Text(VmHarness.P("note.md"), "# note\n");

            vm.ToggleBenchmarkMode();
            await vm.LoadCore(VmHarness.Root);

            // 自动序列跑完会调用ExportTraceLog，将MessageText置为“日志已导出: ...”
            await VmHarness.WaitUntil(
                () => (vm.MessageText ?? "").StartsWith("日志已导出"),
                "诊断基准序列跑完并自动导出", timeoutMs: 15000);

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glance");
            var file = Directory.GetFiles(dir, "glance_trace_*.log")
                .OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
            Assert.NotNull(file);
            var log = File.ReadAllText(file);
            _output.WriteLine(log);

            foreach (var need in new[] { "bench.start", "bench.expand.start", "bench.expand.end", "bench.end", "select.start", "select.painted" })
                Assert.Contains(need, log);
            // note.md 是明文(IsPlainText)被过滤；逐步展开=根/src/tests三个目录，受高亮文件=a.rs/main.cs/t.py共3个
            Assert.Contains("files=3", log);
        });
    }

    [Fact]
    public Task 全展开_开始到结束信号触发()
    {
        return VmHarness.RunAsync(async (vm, repo, fs) =>
        {
            using var settings = new SettingsFileScope();
            repo.Listing(VmHarness.Root, new IArtifact[]
            {
                Artifacts.Dir(VmHarness.P("src")),
                Artifacts.Dir(VmHarness.P("tests")),
                Artifacts.File(VmHarness.P("a.cs")),
            });
            repo.Listing(VmHarness.P("src"), new IArtifact[]
            {
                Artifacts.Dir(VmHarness.P("src/core")),
                Artifacts.File(VmHarness.P("main.cs")),
            });
            repo.Listing(VmHarness.P("tests"), new IArtifact[]
            {
                Artifacts.File(VmHarness.P("t.cs")),
            });
            repo.Listing(VmHarness.P("src/core"), new IArtifact[]
            {
                Artifacts.File(VmHarness.P("x.cs")),
            });
            fs.PutName(VmHarness.Root, "Collisions");
            fs.PutName(VmHarness.P("src"), "src");
            fs.PutName(VmHarness.P("tests"), "tests");
            fs.PutName(VmHarness.P("src/core"), "core");

            await vm.LoadCore(VmHarness.Root);

            Trace.Enabled = true;
            Trace.Drain();

            await vm.ExpandAll();
            await Task.Delay(100);

            var lines = Trace.Drain();
            foreach (var l in lines) _output.WriteLine(l);

            var start = Array.Find(lines, l => l.Contains("expandall.start"));
            var end = Array.Find(lines, l => l.Contains("expandall.end"));
            Assert.NotNull(start);
            Assert.NotNull(end);
            var total = ParseTotal(end!) - ParseTotal(start!);
            _output.WriteLine($"[PROBE] 全展开(3目录) 总耗时 = {total} ms");
            Assert.True(total >= 0, "全展开延迟必须可读");
            Assert.Contains(lines, l => l.Contains("dirs=3"));
        });
    }
}
