using System;
using System.Collections.Generic;

namespace Glance.Processing;

public sealed class CadSummary
{
    private readonly IReadOnlyList<string> _typeNames;
    private readonly IReadOnlyList<uint> _entityCounts;

    public CadSummary(
        string format,
        IReadOnlyList<string> typeNames,
        IReadOnlyList<uint> entityCounts,
        IReadOnlyList<string> layerNames,
        IReadOnlyList<string> diagnostics,
        string version)
    {
        Format = format;
        _typeNames = typeNames;
        _entityCounts = entityCounts;
        LayerNames = layerNames;
        Diagnostics = diagnostics;
        Version = version;
    }

    public string Format { get; }
    public IReadOnlyList<string> LayerNames { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public string Version { get; }
    public int EntityTypeCount => _typeNames.Count;

    public uint TotalEntityCount
    {
        get
        {
            uint sum = 0;
            foreach (var c in _entityCounts) sum += c;
            return sum;
        }
    }

    public IReadOnlyList<EntityStat> Stats()
    {
        var r = new List<EntityStat>(_typeNames.Count);
        for (var i = 0; i < _typeNames.Count; i++)
        {
            var count = i < _entityCounts.Count ? _entityCounts[i] : 0u;
            r.Add(new EntityStat(_typeNames[i], count));
        }
        return r;
    }

    public static CadSummary Empty() =>
        new("", Array.Empty<string>(), Array.Empty<uint>(), Array.Empty<string>(), Array.Empty<string>(), "");
}

public readonly record struct EntityStat(string TypeName, uint Count);
