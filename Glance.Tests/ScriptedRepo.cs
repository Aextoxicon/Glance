using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Glance.Models;
using Glance.Processing;
using Glance.Repositories;
using Glance.Utils;
using Glance.ViewModels;

namespace Glance.Tests;

// 按路径排队返回目录内容，可统计读取次数、可挂起单次读取/列举
public sealed class ScriptedRepo : IArtifactRepo
{
    private readonly Dictionary<string, Queue<IReadOnlyList<IArtifact>>> _lists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<IArtifact>> _last = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<string>> _readGates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource> _listGates = new(StringComparer.Ordinal);

    public Dictionary<string, int> ListCalls { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ReadCalls { get; } = new(StringComparer.Ordinal);

    public ScriptedRepo Listing(string path, params IReadOnlyList<IArtifact>[] sequence)
    {
        _lists[path] = new Queue<IReadOnlyList<IArtifact>>(sequence);
        return this;
    }

    public ScriptedRepo Text(string path, string content)
    {
        _text[path] = content;
        return this;
    }

    public TaskCompletionSource<string> GateRead(string path)
    {
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _readGates[path] = gate;
        return gate;
    }

    public TaskCompletionSource GateList(string path)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _listGates[path] = gate;
        return gate;
    }

    public async Task<Result<IReadOnlyList<IArtifact>>> ListAsync(string path)
    {
        ListCalls[path] = ListCalls.TryGetValue(path, out var c) ? c + 1 : 1;

        if (_listGates.TryGetValue(path, out var gate))
        {
            _listGates.Remove(path);
            await gate.Task.ConfigureAwait(false);
        }

        IReadOnlyList<IArtifact> result;
        if (_lists.TryGetValue(path, out var q) && q.Count > 0)
        {
            result = q.Dequeue();
            _last[path] = result;
        }
        else
        {
            result = _last.TryGetValue(path, out var l) ? l : Array.Empty<IArtifact>();
        }
        return new Success<IReadOnlyList<IArtifact>>(result);
    }

    public async Task<Result<string>> TryReadTextAsync(string id)
    {
        ReadCalls[id] = ReadCalls.TryGetValue(id, out var c) ? c + 1 : 1;

        if (_readGates.TryGetValue(id, out var gate))
        {
            _readGates.Remove(id);
            return new Success<string>(await gate.Task.ConfigureAwait(false));
        }

        return _text.TryGetValue(id, out var t)
            ? new Success<string>(t)
            : new Failure<string>(new IOException(id));
    }

    public Task<Result<IReadOnlyList<IArtifact>>> SearchAsync(string query) => ListAsync(query);
    public Task<Result<IArtifact>> GetAsync(string id) =>
        Task.FromResult<Result<IArtifact>>(new Failure<IArtifact>(new IOException(id)));
    public Task<Result<bool>> DeleteAsync(string id) => Task.FromResult<Result<bool>>(new Success<bool>(false));
    public Task<Result<string>> GetContentUriAsync(string id) => Task.FromResult<Result<string>>(new Success<string>(id));
}

public static class VmHarness
{
    public const string Root = @"D:\root";

    public static string P(string name) => Path.Combine(Root, name);

    public static Task RunAsync(Func<MainViewModel, ScriptedRepo, Task> body) =>
        AvaloniaTest.RunAsync(async () =>
        {
            var repo = new ScriptedRepo();
            var vm = new MainViewModel(new FakeFileSystem(), repo, new FakeParser());
            try { await body(vm, repo); }
            finally { vm.Dispose(); }
        });

    public static TreeItemViewModel Item(ScriptedRepo repo, string path, long size = 100) =>
        new(Artifacts.File(path, size), repo);

    public static TreeItemViewModel DirItem(ScriptedRepo repo, string path) =>
        new(Artifacts.Dir(path), repo);

    public static async Task WaitUntil(Func<bool> condition, string because, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException($"等待超时：{because}");
            await Task.Delay(10);
        }
    }

    public static bool NativeParserAvailable()
    {
        if (System.Runtime.InteropServices.NativeLibrary.TryLoad("uniffi_code_parser", out var handle))
        {
            System.Runtime.InteropServices.NativeLibrary.Free(handle);
            return true;
        }
        return false;
    }
}
