namespace Glance.Processing;

public abstract record CodeParseResult;

public sealed record Code(
    string Language,
    string Content,
    HighlightIndex Highlights,
    IReadOnlyList<OutlineNode> Outline
) : CodeParseResult;

public sealed record PlainText(
    string Language,
    string Content
) : CodeParseResult;
