using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace TibidleMultiManager;

public partial class MainWindow : Window
{
    private const string GameUrl = "https://play.tibidle.com/";
    private readonly SettingsStore store;
    private readonly AppSettings settings;
    private readonly BrowserSession?[] sessions = new BrowserSession?[4];
    private readonly Grid[] containers = new Grid[4];
    private readonly Grid[] panes = new Grid[4];
    private readonly Button[] paneHeaders = new Button[4];
    private readonly Button[] tabs = new Button[4];
    private readonly string[] statuses = Enumerable.Repeat("Preparando…", 4).ToArray();
    private readonly bool[] initializing = new bool[4];
    private readonly List<Window> popupWindows = [];
    private CoreWebView2Environment? environment;
    private int selected;
    private bool closing;
    private Point dragStart;
    private Button? dragCandidate;
    private const string TabDragFormat = "TibidleMultiManager.Tab";

    public MainWindow() : this(new SettingsStore(SettingsStore.DefaultRoot)) { }

    public MainWindow(SettingsStore settingsStore)
    {
        store = settingsStore;
        InitializeComponent();
        settings = store.Load();
        for (var i = 0; i < 4; i++)
        {
            var index = i;
            tabs[i] = new Button { Content = settings.Accounts[i].Name, MinWidth = 110, Margin = new Thickness(0, 0, 6, 0), ToolTip = $"Conta {i + 1} • Ctrl+{i + 1}" };
            tabs[i].Click += (_, _) => SelectTab(index);
            ConfigureTabDrag(tabs[i], index);
            Tabs.Children.Add(tabs[i]);
            containers[i] = new Grid();
            panes[i] = new Grid();
            panes[i].RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panes[i].RowDefinitions.Add(new RowDefinition());
            paneHeaders[i] = new Button { Content = settings.Accounts[i].Name, Padding = new Thickness(8, 3, 8, 3), ToolTip = "Selecionar esta conta para os controles de nome, som e recarregar" };
            paneHeaders[i].Click += (_, _) => SelectTab(index);
            Grid.SetRow(containers[i], 1);
            panes[i].Children.Add(paneHeaders[i]);
            panes[i].Children.Add(containers[i]);
            BrowserHost.Children.Add(panes[i]);
            ShowMessage(i, "Preparando sua conta…");
        }
        for (var i = 0; i < 4; i++)
        {
            var index = i;
            var command = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(command, (_, _) => SelectTab(settings.TabOrder[index])));
            InputBindings.Add(new KeyBinding(command, (Key)((int)Key.D1 + i), ModifierKeys.Control));
        }
        ApplyTabOrder();
        SelectTab(settings.SelectedTab);
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(store.Root, "BrowserData"));
            var firstTab = selected;
            for (var offset = 0; offset < 4 && !closing; offset++)
                await InitializeSession((firstTab + offset) % 4);
        }
        catch (Exception ex)
        {
            if (closing) return;
            for (var i = 0; i < 4; i++) ShowMessage(i, "Não foi possível iniciar o navegador.\nVerifique o Microsoft Edge WebView2 Runtime e reabra o aplicativo.\n\n" + ex.Message);
            Status.Text = "Falha ao iniciar WebView2";
            MessageBox.Show("O navegador não pôde iniciar. Instale ou repare o Microsoft Edge WebView2 Runtime:\nhttps://developer.microsoft.com/microsoft-edge/webview2/\n\n" + ex.Message, Title);
        }
    }

    private async Task InitializeSession(int index)
    {
        if (environment is null || closing || initializing[index]) return;
        initializing[index] = true;
        try
        {
            sessions[index]?.Dispose();
            containers[index].Children.Clear();
            var session = new BrowserSession(settings.Accounts[index]);
            sessions[index] = session;
            containers[index].Children.Add(session.View);
            await session.InitializeAsync(environment);
            if (closing) return;
            var core = session.View.CoreWebView2;
            core.NavigationStarting += (_, _) => SetStatus(index, "Carregando…");
            core.NavigationCompleted += (_, args) => SetStatus(index, args.IsSuccess ? "Página carregada" : $"Falha de conexão ({args.WebErrorStatus}) — use Recarregar");
            core.ProcessFailed += (_, args) =>
            {
                SetStatus(index, "Navegador interrompido — use Recarregar");
                if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                {
                    // Recreate the environment on the next application launch after a browser-wide crash.
                    ShowMessage(index, "O navegador foi interrompido. Feche e reabra o aplicativo para recuperar as contas.");
                }
            };
            core.NewWindowRequested += async (_, args) => await OpenPopup(session.Account, args);
            session.View.GotKeyboardFocus += (_, _) => { if (selected != index) SelectTab(index); };
            session.View.PreviewKeyDown += (_, args) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Control && args.Key >= Key.D1 && args.Key <= Key.D4)
                {
                    SelectTab(settings.TabOrder[(int)args.Key - (int)Key.D1]);
                    args.Handled = true;
                }
            };
            core.Navigate(GameUrl);
        }
        catch (Exception ex)
        {
            if (!closing)
            {
                sessions[index]?.Dispose();
                sessions[index] = null;
                ShowMessage(index, "Não foi possível carregar esta conta.\nClique em Recarregar para tentar novamente.\n\n" + ex.Message);
                SetStatus(index, "Falha ao iniciar esta conta");
            }
        }
        finally { initializing[index] = false; if (!closing) RefreshControls(); }
    }

    private async Task OpenPopup(AccountSettings account, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (closing || environment is null || !Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && args.Uri != "about:blank")) return;
        using var deferral = args.GetDeferral();
        var browser = new BrowserSession(account);
        var popup = new Window
        {
            Title = $"{account.Name} — {uri.Host}", Owner = this,
            Width = 600, Height = 780, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background, Content = browser.View
        };
        popupWindows.Add(popup);
        popup.Closed += (_, _) => { popupWindows.Remove(popup); browser.Dispose(); };
        try
        {
            popup.Show();
            await browser.InitializeAsync(environment);
            if (closing || !popup.IsVisible) return;
            browser.View.CoreWebView2.WindowCloseRequested += (_, _) => popup.Close();
            browser.View.CoreWebView2.SourceChanged += (_, _) =>
            {
                if (Uri.TryCreate(browser.View.CoreWebView2.Source, UriKind.Absolute, out var source))
                    popup.Title = $"{account.Name} — {source.Host}";
            };
            args.NewWindow = browser.View.CoreWebView2;
        }
        catch (Exception ex)
        {
            if (!closing && popup.IsVisible)
                MessageBox.Show(this, "Não foi possível abrir a janela de autenticação.\n\n" + ex.Message, Title);
            popup.Close();
        }
        finally { deferral.Complete(); }
    }

    private void SelectTab(int index)
    {
        selected = index;
        settings.SelectedTab = index;
        BrowserLayout.Apply(BrowserHost, settings.TabOrder.Select(i => panes[i]).ToArray(), settings.GridView, settings.TabOrder.IndexOf(index));
        for (var i = 0; i < 4; i++)
        {
            tabs[i].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(i == index ? "#413620" : "#191D25"));
            tabs[i].BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(i == index ? "#D3AC55" : "#343945"));
            paneHeaders[i].Visibility = settings.GridView ? Visibility.Visible : Visibility.Collapsed;
            paneHeaders[i].Background = tabs[i].Background;
            paneHeaders[i].BorderBrush = tabs[i].BorderBrush;
        }
        AccountName.Text = settings.Accounts[index].Name;
        RefreshControls();
    }

    private void ToggleLayout(object sender, RoutedEventArgs e)
    {
        settings.GridView = !settings.GridView;
        SelectTab(selected);
        SaveSettings();
    }

    private void ApplyTabOrder()
    {
        Tabs.Children.Clear();
        for (var position = 0; position < settings.TabOrder.Count; position++)
        {
            var account = settings.TabOrder[position];
            Tabs.Children.Add(tabs[account]);
            tabs[account].ToolTip = $"Arraste para reorganizar • Ctrl+{position + 1}";
        }
    }

    private void ConfigureTabDrag(Button tab, int account)
    {
        tab.AllowDrop = true;
        tab.PreviewMouseLeftButtonDown += (_, e) => { dragCandidate = tab; dragStart = e.GetPosition(this); };
        tab.PreviewMouseLeftButtonUp += (_, _) => dragCandidate = null;
        tab.PreviewMouseMove += (_, e) =>
        {
            if (dragCandidate != tab || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(this);
            if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragCandidate = null;
            tab.ReleaseMouseCapture();
            try { DragDrop.DoDragDrop(tab, new DataObject(TabDragFormat, tab), DragDropEffects.Move); }
            finally { foreach (var button in tabs) button.Opacity = 1; }
            e.Handled = true;
        };
        tab.PreviewDragOver += (_, e) =>
        {
            var source = e.Data.GetData(TabDragFormat) as Button;
            e.Effects = source is not null && tabs.Contains(source) ? DragDropEffects.Move : DragDropEffects.None;
            tab.Opacity = e.Effects == DragDropEffects.Move ? 0.65 : 1;
            e.Handled = true;
        };
        tab.DragLeave += (_, _) => tab.Opacity = 1;
        tab.PreviewDrop += (_, e) =>
        {
            tab.Opacity = 1;
            var source = e.Data.GetData(TabDragFormat) as Button;
            var sourceAccount = source is null ? -1 : Array.IndexOf(tabs, source);
            if (sourceAccount >= 0 && sourceAccount != account)
            {
                TabOrder.Move(settings.TabOrder, sourceAccount, account);
                ApplyTabOrder();
                SelectTab(selected);
                SaveSettings();
                e.Effects = DragDropEffects.Move;
            }
            e.Handled = true;
        };
    }

    private void RefreshControls()
    {
        LayoutButton.Content = settings.GridView ? "▣  Tela normal" : "▦  4 telas";
        Status.Text = $"{settings.Accounts[selected].Name}  •  {statuses[selected]}";
        SoundButton.Content = settings.Accounts[selected].Muted ? "Som: desligado" : "Som: ligado";
        SoundButton.IsEnabled = sessions[selected]?.Ready == true;
        ReloadButton.IsEnabled = environment is not null && !initializing[selected];
    }

    private void SetStatus(int index, string text)
    {
        statuses[index] = text;
        if (!closing && index == selected) RefreshControls();
    }

    private void ShowMessage(int index, string text)
    {
        containers[index].Children.Clear();
        containers[index].Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray, FontSize = 16, TextAlignment = TextAlignment.Center, Margin = new Thickness(30) });
    }

    private void SaveName(object sender, RoutedEventArgs e)
    {
        var name = AccountName.Text.Trim();
        if (name.Length == 0) { AccountName.Text = settings.Accounts[selected].Name; return; }
        settings.Accounts[selected].Name = name;
        tabs[selected].Content = name;
        paneHeaders[selected].Content = name;
        SaveSettings();
        RefreshControls();
    }

    private void NameKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) SaveName(sender, e); }

    private void ToggleSound(object sender, RoutedEventArgs e)
    {
        var session = sessions[selected];
        if (session?.Ready != true) return;
        session.Account.Muted = !session.Account.Muted;
        session.View.CoreWebView2.IsMuted = session.Account.Muted;
        SaveSettings();
        RefreshControls();
    }

    private async void Reload(object sender, RoutedEventArgs e)
    {
        var index = selected;
        try
        {
            if (sessions[index]?.Ready == true) sessions[index]!.View.Reload();
            else await InitializeSession(index);
        }
        catch (Exception) { await InitializeSession(index); }
    }

    private void SaveSettings()
    {
        try { store.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, "Não foi possível salvar os nomes e preferências.\n\n" + ex.Message, Title); }
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        closing = true;
        SaveSettings();
        foreach (var popup in popupWindows.ToArray()) popup.Close();
        foreach (var session in sessions) session?.Dispose();
    }
}
