using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Glance.Views;

/// <summary>
/// Minimal Android surface. The code preview editor (AvaloniaEdit) has no Android TFM yet,
/// so the Android build ships this placeholder until the editor is ported to a cross-platform
/// control. File-tree, parsing, and outline logic in the shared ViewModels already compile and
/// run on Android — only the editor presentation is missing.
/// </summary>
public class AndroidPlaceholderView : UserControl
{
    public AndroidPlaceholderView()
    {
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "Glance for Android",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "代码预览编辑器（AvaloniaEdit）暂不支持 Android，尚未接入。文件树与解析逻辑已就绪，待编辑器迁移。",
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }
}
