namespace Glance.Processing;

public record OutlineNode(
    string Kind,
    string Name,
    string Detail,
    long StartByte,
    long EndByte,
    IReadOnlyList<OutlineNode> Children
);
