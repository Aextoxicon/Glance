namespace Glance.Processing;

public static class FileProcessor
{
    // 文件名→grammar 的路由已在 Rust 侧（resolve_grammar：文件名优先、扩展名兜底），
    // 这里只承接处理层策略（阈值、缓存等）。

    public static ParsedCode Process(string content, string filename, ICodeParser parser)
    {
        return parser.ParseCode(content, filename);
    }
}
