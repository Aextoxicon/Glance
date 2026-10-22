using System.Linq;
using Glance.Utils;
using Xunit;

namespace Glance.Tests;

public class GitIgnoreTests
{
    [Fact]
    public void 解析跳过注释取反通配符绝对路径与点开头()
    {
        var g = GitIgnore.Parse("""
            # 注释
            !keep.txt
            *.log
            bin?/
            /root-only
            .vs/
            """);

        Assert.Empty(g.DirOnly);
        Assert.Empty(g.DirOrFile);
    }

    [Fact]
    public void 尾斜杠目录进目录集_无斜杠进通用集()
    {
        var g = GitIgnore.Parse("build/\ndist");

        Assert.Contains("build", g.DirOnly);
        Assert.DoesNotContain("build", g.DirOrFile);
        Assert.Contains("dist", g.DirOrFile);
        Assert.DoesNotContain("dist", g.DirOnly);
    }

    [Fact]
    public void 双尾斜杠只去一个_剩余含斜杠行被跳过()
    {
        // KMP的removeSuffix("/")语义：只去掉一个，剩"dir/"含'/'→整行忽略
        var g = GitIgnore.Parse("dir//");

        Assert.Empty(g.DirOnly);
        Assert.Empty(g.DirOrFile);
    }

    [Fact]
    public void 含路径分隔符的行被跳过()
    {
        var g = GitIgnore.Parse("src/generated/\nfoo/bar");

        Assert.Empty(g.DirOnly);
        Assert.Empty(g.DirOrFile);
    }

    [Fact]
    public void 过滤剔除点开头与名字命中的文件()
    {
        var g = GitIgnore.Parse("dist\nsecret.txt");
        var items = new[]
        {
            Artifacts.File(@"D:\ws\ok.cs"),
            Artifacts.File(@"D:\ws\.hidden"),
            Artifacts.File(@"D:\ws\dist"),
            Artifacts.File(@"D:\ws\secret.txt"),
        };

        var kept = GitIgnore.Filter(items, g).Select(i => i.Name).ToArray();

        Assert.Equal(new[] { "ok.cs" }, kept);
    }

    [Fact]
    public void 目录集只剔除目录_同名文件保留()
    {
        var g = GitIgnore.Parse("build/");
        var items = new[]
        {
            Artifacts.Dir(@"D:\ws\build"),
            Artifacts.File(@"D:\ws\build"),
        };

        var kept = GitIgnore.Filter(items, g).ToArray();

        Assert.Single(kept);
        Assert.False(kept[0].Payload is Glance.Models.LocalPayload { IsDir: true });
    }

    [Fact]
    public void 空内容返回空过滤集()
    {
        Assert.Empty(GitIgnore.Parse(null).DirOnly);
        Assert.Empty(GitIgnore.Parse("").DirOrFile);
    }
}
