using System.Windows;
using UpdateHub.Core.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace UpdateHub.App.Services;

/// <summary>Applies the user's theme preference and keeps Mica in sync with the system theme when requested.</summary>
public sealed class AppThemeService
{
    private Window? _window;
    private bool _watching;

    public void Attach(Window window)
    {
        _window = window;
    }

    public void Apply(AppTheme theme)
    {
        switch (theme)
        {
            case AppTheme.Light:
                StopWatching();
                ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
                break;
            case AppTheme.Dark:
                StopWatching();
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme(true);
                if (_window is not null && !_watching)
                {
                    SystemThemeWatcher.Watch(_window, WindowBackdropType.Mica, true);
                    _watching = true;
                }

                break;
        }
    }

    private void StopWatching()
    {
        if (_window is not null && _watching)
        {
            SystemThemeWatcher.UnWatch(_window);
            _watching = false;
        }
    }
}
