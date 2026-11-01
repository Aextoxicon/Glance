using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Glance.ViewModels;
using Glance.Views;

namespace Glance;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var view = param switch
        {
            MainViewModel => (Control)new MainView(),
            _ => null
        };

        return view ?? new TextBlock { Text = "Not Found: " + param.GetType().FullName };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
