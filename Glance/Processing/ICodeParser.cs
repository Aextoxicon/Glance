namespace Glance.Processing;

public interface ICodeParser
{
    CodeParseResult ParseCode(string source, string extension);
}
