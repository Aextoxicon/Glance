namespace Glance.Processing;

public record HighlightToken(
    int StartByte,
    int EndByte,
    string Kind
);
