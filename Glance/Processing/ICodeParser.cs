namespace Glance.Processing;

public interface ICodeParser
{
    ParsedCode ParseCode(string source, string filename);
}
