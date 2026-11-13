using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using PixiEditor.Helpers.Extensions;
using PixiEditor.Extensions.CommonApi.UserPreferences.Settings.PixiEditor;
using PixiEditor.Helpers;
using PixiEditor.Models.Controllers;
using PixiEditor.Models.Services.NewsFeed;
using PixiEditor.Models.Structures;
using PixiEditor.Models.UserData;
using PixiEditor.OperatingSystem;
using PixiEditor.ViewModels;
using PixiEditor.ViewModels.SubViewModels;

namespace PixiEditor.Views.Windows;

/// <summary>
/// The start screen: recent files, the ways of making a new one, and the news panel.
/// </summary>
/// <remarks>
/// A control rather than a window so it can be both: <see cref="HelloTherePopup"/> shows it at
/// startup, and the + button in the title bar opens it as a tab. Anything only a window can answer
/// - how wide to be, when to close - it raises as an event for whatever is hosting it.
/// </remarks>
internal partial class HelloThereView : UserControl
{
    /// <summary>Width of the news panel, and so how much a host grows or shrinks when it is toggled.</summary>
    public const int NewsPanelWidth = 300;

    public static readonly StyledProperty<FileViewModel> FileViewModelProperty =
        AvaloniaProperty.Register<HelloThereView, FileViewModel>(nameof(FileViewModel));

    public static readonly StyledProperty<bool> RecentlyOpenedEmptyProperty =
        AvaloniaProperty.Register<HelloThereView, bool>(nameof(RecentlyOpenedEmpty));

    public static readonly StyledProperty<bool> IsFetchingNewsProperty =
        AvaloniaProperty.Register<HelloThereView, bool>(nameof(IsFetchingNews), defaultValue: default(bool));

    public static readonly StyledProperty<bool> ShowAllBetaExamplesProperty =
        AvaloniaProperty.Register<HelloThereView, bool>(nameof(ShowAllBetaExamples));

    public static readonly StyledProperty<bool> NewsPanelCollapsedProperty =
        AvaloniaProperty.Register<HelloThereView, bool>(nameof(NewsPanelCollapsed), defaultValue: false);

    public static readonly StyledProperty<bool> FailedFetchingNewsProperty =
        AvaloniaProperty.Register<HelloThereView, bool>(nameof(FailedFetchingNews), defaultValue: false);

    private readonly bool newsDisabled;

    private bool hasImageInClipboard;

    static HelloThereView()
    {
        NewsPanelCollapsedProperty.Changed.Subscribe(NewsPanelCollapsedChangedCallback);
    }

    public HelloThereView()
        : this(ViewModelMain.Current?.FileSubViewModel)
    {
    }

    public HelloThereView(FileViewModel? fileViewModel)
    {
        DataContext = this;
        FileViewModel = fileViewModel;

        OpenFileCommand = new AsyncRelayCommand(OpenFile);
        OpenNewFileCommand = new AsyncRelayCommand(OpenNewFile);
        OpenRecentCommand = new RelayCommand<string>(OpenRecent);
        SetShowAllBetaExamplesCommand = new RelayCommand<bool>(SetShowAllBetaExamples);
        OpenInExplorerCommand = new RelayCommand<string>(OpenInExplorer, CanOpenInExplorer);
        NewFromClipboardCommand = new RelayCommand(NewFromClipboard, CanOpenFromClipboard);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());

        if (RecentlyOpened is not null)
        {
            RecentlyOpenedEmpty = RecentlyOpened.Count == 0;
            RecentlyOpened.CollectionChanged += RecentlyOpened_CollectionChanged;
        }

        newsDisabled = PixiEditorSettings.StartupWindow.DisableNewsPanel.Value;
        NewsProvider = new NewsProvider();

        CheckHasClipboardInImage();

        Loaded += OnLoaded;

        InitializeComponent();

        NewsPanelCollapsed = PixiEditorSettings.StartupWindow.NewsPanelCollapsed.Value;

        if (newsDisabled || NewsPanelCollapsed)
        {
            CollapseNewsColumn(true);
        }
    }

    /// <summary>Raised once something has been opened, so a host that is a window can close itself.</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// The same thing as a command, for the example buttons, which take one. It stood in for the
    /// window's own close command when this was a window.
    /// </summary>
    public RelayCommand CloseCommand { get; }

    /// <summary>
    /// Raised when the news panel is shown or hidden, with the width the content gained or lost, so
    /// a window host can follow it. A tab host ignores it and simply reflows.
    /// </summary>
    public event Action<int>? NewsPanelWidthChanged;

    public RecentlyOpenedCollection? RecentlyOpened => FileViewModel?.RecentlyOpened;

    public ObservableRangeCollection<News> News { get; set; } = new();

    public static string VersionText => $"v{VersionHelpers.GetCurrentAssemblyVersionString()}";

    public AsyncRelayCommand OpenFileCommand { get; set; }

    public AsyncRelayCommand OpenNewFileCommand { get; set; }

    public RelayCommand NewFromClipboardCommand { get; set; }

    public RelayCommand<string> OpenRecentCommand { get; set; }

    public RelayCommand<string> OpenInExplorerCommand { get; set; }

    public RelayCommand<bool> SetShowAllBetaExamplesCommand { get; set; }

    public bool NewsDisabled => newsDisabled;

    /// <summary>How many recent files there are, which is what a window host sizes itself around.</summary>
    public int RecentCount => RecentlyOpened?.Count ?? 0;

    /// <summary>The space the news panel is taking up right now.</summary>
    public int NewsWidth => newsDisabled || NewsPanelCollapsed ? 0 : NewsPanelWidth;

    public bool ShowDonateButton => // Steam doesn't allow external donations :(
#if STEAM
        false;
#else
        true;
#endif

    public FileViewModel FileViewModel
    {
        get => GetValue(FileViewModelProperty);
        set => SetValue(FileViewModelProperty, value);
    }

    public bool NewsPanelCollapsed
    {
        get => GetValue(NewsPanelCollapsedProperty);
        set => SetValue(NewsPanelCollapsedProperty, value);
    }

    public bool IsFetchingNews
    {
        get => GetValue(IsFetchingNewsProperty);
        set => SetValue(IsFetchingNewsProperty, value);
    }

    public bool ShowAllBetaExamples
    {
        get => GetValue(ShowAllBetaExamplesProperty);
        set => SetValue(ShowAllBetaExamplesProperty, value);
    }

    public bool FailedFetchingNews
    {
        get => GetValue(FailedFetchingNewsProperty);
        set => SetValue(FailedFetchingNewsProperty, value);
    }

    public bool RecentlyOpenedEmpty
    {
        get => GetValue(RecentlyOpenedEmptyProperty);
        set => SetValue(RecentlyOpenedEmptyProperty, value);
    }

    private NewsProvider NewsProvider { get; set; }

    /// <summary>Re-checks the clipboard, which a window host does whenever it is activated.</summary>
    public void RefreshClipboard() => CheckHasClipboardInImage();

    private static void NewsPanelCollapsedChangedCallback(AvaloniaPropertyChangedEventArgs<bool> e)
    {
        HelloThereView view = (HelloThereView)e.Sender;

        if (view.newsDisabled)
            return;

        view.CollapseNewsColumn(e.NewValue.Value);
        view.NewsPanelWidthChanged?.Invoke(e.NewValue.Value ? -NewsPanelWidth : NewsPanelWidth);

        PixiEditorSettings.StartupWindow.NewsPanelCollapsed.Value = e.NewValue.Value;
    }

    private void CollapseNewsColumn(bool collapsed)
    {
        if (grid is null)
            return;

        grid.ColumnDefinitions.Last<ColumnDefinition>().Width =
            new GridLength(collapsed ? 0 : NewsPanelWidth);
    }

    private void SetShowAllBetaExamples(bool value) => ShowAllBetaExamples = value;

    private void RecentlyOpened_CollectionChanged(object sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RecentlyOpenedEmpty = RecentCount == 0;
    }

    private async Task OpenFile()
    {
        Leave();
        await FileViewModel.OpenFromOpenFileDialog();
    }

    private async Task OpenNewFile()
    {
        Leave();
        await FileViewModel.CreateFromNewFileDialog();
    }

    private void NewFromClipboard()
    {
        FileViewModel.OpenFromClipboard();
        Leave();
    }

    private void OpenRecent(string parameter)
    {
        Leave();
        FileViewModel.OpenRecent(parameter);
    }

    /// <summary>Brings the editor forward and asks the host to get out of the way.</summary>
    private void Leave()
    {
        Application.Current.ForDesktopMainWindow(mainWindow => mainWindow.Activate());
        CloseRequested?.Invoke();
    }

    private bool CanOpenFromClipboard() => hasImageInClipboard;

    private void OpenInExplorer(string parameter) => IOperatingSystem.Current.OpenFolder(parameter);

    private bool CanOpenInExplorer(string parameter) => File.Exists(parameter);

    private void CheckHasClipboardInImage()
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            hasImageInClipboard = await ClipboardController.IsImageInClipboard();
        }).ContinueWith(_ => { Dispatcher.UIThread.Invoke(NewFromClipboardCommand.NotifyCanExecuteChanged); });
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // TODO: Fetching news freezes input when no internet is present
        if (newsDisabled) return;

        try
        {
            IsFetchingNews = true;
            var news = await NewsProvider.FetchNewsAsync();
            if (news is not null)
            {
                IsFetchingNews = false;
                News.Clear();
                News.AddRange(news);
                if (NewsPanelCollapsed && News.Any(x => x.IsNew))
                {
                    NewsPanelCollapsed = false;
                }
            }
            else
            {
                IsFetchingNews = false;
                FailedFetchingNews = true;
            }
        }
        catch (Exception ex)
        {
            IsFetchingNews = false;
            FailedFetchingNews = true;
            await CrashHelper.SendExceptionInfoAsync(ex);
        }
    }
}
