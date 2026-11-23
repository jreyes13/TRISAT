using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Trisat.Linux.ViewModels;

namespace Trisat.Linux;

public partial class MainWindow : Window, IDesktopInteractionHost
{
    private readonly MainViewModel _viewModel;
    private bool _isFullscreen;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(desktopHost: this);
        DataContext = _viewModel;
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void OnToggleFullscreenClick(object? sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            _isFullscreen = false;
            WindowState = WindowState.Maximized;
            FullscreenButton.Content = "Fullscreen (F11)";
        }
        else
        {
            _isFullscreen = true;
            WindowState = WindowState.FullScreen;
            FullscreenButton.Content = "Exit Fullscreen (Esc)";
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosing(e);
    }

    public async Task SetClipboardTextAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    public async Task<string?> PickOpenFileAsync(string title, string[] patterns)
    {
        var storage = StorageProvider;
        if (!storage.CanOpen) return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(title) { Patterns = patterns }
            ]
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string[] patterns)
    {
        var storage = StorageProvider;
        if (!storage.CanSave) return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices =
            [
                new FilePickerFileType(title) { Patterns = patterns }
            ]
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var storage = StorageProvider;
        if (!storage.CanPickFolder) return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
