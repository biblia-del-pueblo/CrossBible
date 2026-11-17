using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CrossBible.ViewModels;
using CrossBible.Views;

namespace CrossBible;

/// <summary>Finds the view of a view model. A list, not a search by name (a type that is looked up by its name cannot be kept by compiling ahead of time).</summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        return param switch
        {
            MainViewModel => new MainView(),
            _ => new TextBlock { Text = "Not Found: " + param.GetType().FullName },
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
