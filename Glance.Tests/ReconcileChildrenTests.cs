using System;
using System.Linq;
using Glance.ViewModels;
using Xunit;

namespace Glance.Tests;

// 按Id复用、移动、增删
public class ReconcileChildrenTests
{
    private static TreeItemViewModel NewDir() =>
        new(Artifacts.Dir(@"D:\ws"), new FakeArtifactRepo());

    [Fact]
    public void 新建目录带一个占位节点()
    {
        var dir = NewDir();
        Assert.Single(dir.Children);
        Assert.True(dir.Children[0].IsPlaceholder);
    }

    [Fact]
    public void 同标识复用同一实例()
    {
        var dir = NewDir();
        var a = Artifacts.File(@"D:\ws\a.cs");
        var b = Artifacts.File(@"D:\ws\b.cs");

        dir.ReconcileChildren(new[] { a, b });
        var firstA = dir.Children.First(c => c.Name == "a.cs");
        var firstB = dir.Children.First(c => c.Name == "b.cs");

        dir.ReconcileChildren(new[] { a, b });

        Assert.Same(firstA, dir.Children.First(c => c.Name == "a.cs"));
        Assert.Same(firstB, dir.Children.First(c => c.Name == "b.cs"));
        Assert.Equal(2, dir.ChildCount);
    }

    [Fact]
    public void 新增项插入_删除项移除()
    {
        var dir = NewDir();
        var a = Artifacts.File(@"D:\ws\a.cs");
        var b = Artifacts.File(@"D:\ws\b.cs");
        var c = Artifacts.File(@"D:\ws\c.cs");

        dir.ReconcileChildren(new[] { a, b });
        dir.ReconcileChildren(new[] { a, b, c });
        Assert.Equal(3, dir.Children.Count);

        dir.ReconcileChildren(new[] { a });
        Assert.Single(dir.Children);
        Assert.Equal("a.cs", dir.Children[0].Name);
        Assert.Equal(1, dir.ChildCount);
    }

    [Fact]
    public void 顺序变化走移动_实例不变()
    {
        var dir = NewDir();
        var a = Artifacts.File(@"D:\ws\a.cs");
        var b = Artifacts.File(@"D:\ws\b.cs");

        dir.ReconcileChildren(new[] { a, b });
        var firstA = dir.Children.First(c => c.Name == "a.cs");
        var firstB = dir.Children.First(c => c.Name == "b.cs");

        dir.ReconcileChildren(new[] { b, a });

        Assert.Same(firstB, dir.Children[0]);
        Assert.Same(firstA, dir.Children[1]);
    }

    [Fact]
    public void 同路径由文件变目录时重建实例()
    {
        var dir = NewDir();
        var asFile = Artifacts.File(@"D:\ws\target");
        dir.ReconcileChildren(new[] { asFile });
        var fileNode = dir.Children[0];

        dir.ReconcileChildren(new[] { Artifacts.Dir(@"D:\ws\target") });

        Assert.NotSame(fileNode, dir.Children[0]);
        Assert.True(dir.Children[0].IsDir);
    }

    [Fact]
    public void 空集合补占位节点()
    {
        var dir = NewDir();
        dir.ReconcileChildren(new[] { Artifacts.File(@"D:\ws\a.cs") });
        dir.ReconcileChildren(Array.Empty<Glance.Models.IArtifact>());

        Assert.Single(dir.Children);
        Assert.True(dir.Children[0].IsPlaceholder);
        Assert.Equal(0, dir.ChildCount);
    }

    [Fact]
    public void 内容变化时可读大小跟着更新()
    {
        var dir = NewDir();
        var a = Artifacts.File(@"D:\ws\a.cs", size: 100);
        dir.ReconcileChildren(new[] { a });
        var node = dir.Children[0];
        Assert.Equal("100 B", node.SizeDisplay);

        dir.ReconcileChildren(new[] { Artifacts.File(@"D:\ws\a.cs", size: 2048) });
        Assert.Same(node, dir.Children[0]);
        Assert.Equal("2 KB", node.SizeDisplay);
    }
}
