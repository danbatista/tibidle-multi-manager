using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using TibidleMultiManager;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--check-data-root"))
        {
            var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tibidle-multi-manager");
            Assert(SettingsStore.DefaultRoot == expected, "Browser and settings data live outside package-virtualized AppData");
            return;
        }
        var app = new Application();
        if (args.Contains("--order"))
        {
            var root = Path.Combine(Path.GetTempPath(), "TibidleOrder-" + Guid.NewGuid().ToString("N"));
            var store = new SettingsStore(root);
            var settings = store.Load();
            settings.Accounts[0].Name = "First profile";
            settings.SelectedTab = 0;
            TabOrder.Move(settings.TabOrder, 0, 3);
            Assert(settings.TabOrder.SequenceEqual(new[] { 1, 2, 3, 0 }), "Move first tab to last position");
            TabOrder.Move(settings.TabOrder, 0, 1);
            Assert(settings.TabOrder.SequenceEqual(new[] { 0, 1, 2, 3 }), "Move last tab back to first position");
            TabOrder.Move(settings.TabOrder, 2, 0);
            store.Save(settings);
            var loaded = store.Load();
            Assert(loaded.TabOrder.SequenceEqual(new[] { 2, 0, 1, 3 }), "Order survives reopening settings");
            Assert(loaded.Accounts[0].Name == "First profile" && loaded.Accounts[0].ProfileName == "Account1" && loaded.SelectedTab == 0, "Reorder preserves profile identity, name and selected account");
            File.WriteAllText(Path.Combine(root, "settings.json"), "{\"TabOrder\":[2,2,-1,7]}");
            Assert(store.Load().TabOrder.SequenceEqual(new[] { 2, 0, 1, 3 }), "Invalid order retains valid entries and restores missing accounts");
            File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
            Assert(store.Load().TabOrder.SequenceEqual(new[] { 0, 1, 2, 3 }), "Older settings keep original account order");
            return;
        }
        if (args.Contains("--layout"))
        {
            var host = new Grid();
            var panes = Enumerable.Range(0, 4).Select(_ => new Grid()).ToArray();
            var contents = panes.Select(p => { var child = new TextBox { Text = "session retained" }; p.Children.Add(child); return child; }).ToArray();
            foreach (var pane in panes) host.Children.Add(pane);
            BrowserLayout.Apply(host, panes, true, 0);
            host.Measure(new Size(1600, 900)); host.Arrange(new Rect(0, 0, 1600, 900));
            Assert(panes.All(p => p.Visibility == Visibility.Visible), "Grid shows all four sessions");
            var positions = panes.Select(p => p.TranslatePoint(new Point(), host)).ToArray();
            Assert(positions.Distinct().Count() == 4 && positions[1].X > positions[0].X && positions[2].Y > positions[0].Y, "Four non-overlapping quadrants");
            Assert(panes.All(p => p.ActualWidth > 700 && p.ActualWidth <= 800 && p.ActualHeight > 400 && p.ActualHeight <= 450), "Each quadrant uses one quarter of the area");
            BrowserLayout.Apply(host, panes, false, 2);
            host.Measure(new Size(1600, 900)); host.Arrange(new Rect(0, 0, 1600, 900));
            Assert(panes[2].Visibility == Visibility.Visible && panes.Count(p => p.Visibility == Visibility.Visible) == 1, "Normal mode restores selected account only");
            Assert(panes[2].ActualWidth == 1600 && panes[2].ActualHeight == 900, "Normal mode fills available area");
            BrowserLayout.Apply(host, panes, true, 3);
            Assert(panes.Select((p, i) => ReferenceEquals(p.Children[0], contents[i])).All(x => x), "Switching layouts retains existing content");
            var root = Path.Combine(Path.GetTempPath(), "TibidleLayout-" + Guid.NewGuid().ToString("N"));
            var store = new SettingsStore(root);
            var preferences = store.Load(); preferences.GridView = true; store.Save(preferences);
            Assert(store.Load().GridView, "Layout preference survives settings reload");
            return;
        }
        if (args.Contains("--game-smoke"))
        {
            RunGameSmoke(app);
            return;
        }
        var panel = new Grid();
        var window = new Window { Content = panel, Width = 800, Height = 600, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Top = -10000 };
        window.Loaded += async (_, _) =>
        {
            var root = Path.Combine(Path.GetTempPath(), "TibidleChecks-" + Guid.NewGuid().ToString("N"));
            var sessions = new List<BrowserSession>();
            try
            {
                var store = new SettingsStore(root);
                var settings = store.Load();
                Assert(settings.Accounts.Count == 4, "Exactly four accounts");
                Assert(settings.Accounts.Select(x => x.ProfileName).Distinct().Count() == 4, "Stable distinct profiles");
                var profile = settings.Accounts[0].ProfileName;
                settings.Accounts[0].Name = "Meu druida";
                settings.Accounts[0].Muted = true;
                store.Save(settings);
                var saved = store.Load();
                Assert(saved.Accounts[0].ProfileName == profile && saved.Accounts[0].Name == "Meu druida" && saved.Accounts[0].Muted, "Rename and mute persist without changing profile");
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "BrowserData"));
                var site = Path.Combine(root, "site");
                Directory.CreateDirectory(site);
                File.WriteAllText(Path.Combine(site, "index.html"), "<!doctype html><title>Storage check</title>");
                for (var i = 0; i < 4; i++)
                {
                    var session = new BrowserSession(saved.Accounts[i]);
                    sessions.Add(session);
                    panel.Children.Add(session.View);
                    await session.InitializeAsync(environment);
                    session.View.CoreWebView2.SetVirtualHostNameToFolderMapping("profile.test", site, CoreWebView2HostResourceAccessKind.DenyCors);
                    await Navigate(session, "https://profile.test/index.html");
                    Assert(await session.View.ExecuteScriptAsync("localStorage.getItem('account')") == "null", "New profile has isolated storage");
                    var cookies = await session.View.CoreWebView2.CookieManager.GetCookiesAsync("https://profile.test");
                    Assert(cookies.Count == 0, "New profile has isolated cookies");
                    await session.View.ExecuteScriptAsync($"localStorage.setItem('account','account-{i}'); document.cookie='account=account-{i}; Max-Age=86400; Secure; SameSite=Lax; Path=/'");
                }
                for (var i = 0; i < 4; i++)
                {
                    var session = sessions[i];
                    Assert(await session.View.ExecuteScriptAsync("localStorage.getItem('account')") == $"\"account-{i}\"", "Other accounts did not overwrite storage");
                    var cookies = await session.View.CoreWebView2.CookieManager.GetCookiesAsync("https://profile.test");
                    Assert(cookies.Single(c => c.Name == "account").Value == $"account-{i}", "Other accounts did not overwrite cookie");
                }
                Assert(sessions[0].View.CoreWebView2.IsMuted, "Muted setting applied to browser");
                panel.Children.Remove(sessions[0].View);
                sessions[0].Dispose();
                var reopened = new BrowserSession(saved.Accounts[0]);
                sessions[0] = reopened;
                panel.Children.Add(reopened.View);
                await reopened.InitializeAsync(environment);
                reopened.View.CoreWebView2.SetVirtualHostNameToFolderMapping("profile.test", site, CoreWebView2HostResourceAccessKind.DenyCors);
                await Navigate(reopened, "https://profile.test/index.html");
                Assert(await reopened.View.ExecuteScriptAsync("localStorage.getItem('account')") == "\"account-0\"", "Recreated browser retains session storage");
                Assert((await reopened.View.CoreWebView2.CookieManager.GetCookiesAsync("https://profile.test")).Single(c => c.Name == "account").Value == "account-0", "Recreated browser retains cookie");
                Console.WriteLine("PASS: four real browser profiles, cookie/localStorage isolation, persistence, rename and mute.");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { foreach (var session in sessions) session.Dispose(); app.Shutdown(); }
        };
        app.Run(window);
    }

    private static void RunGameSmoke(Application app)
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "TibidleShell-" + Guid.NewGuid().ToString("N"));
        var window = new MainWindow(new SettingsStore(testRoot)) { WindowState = WindowState.Normal, Left = -10000, Top = -10000, ShowInTaskbar = false };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var host = (Grid)window.FindName("BrowserHost");
                var tabs = (StackPanel)window.FindName("Tabs");
                var deadline = DateTime.UtcNow.AddSeconds(60);
                Microsoft.Web.WebView2.Wpf.WebView2[] views;
                do
                {
                    await Task.Delay(500);
                    views = host.Children.OfType<Grid>().SelectMany(p => p.Children.OfType<Grid>()).SelectMany(x => x.Children.OfType<Microsoft.Web.WebView2.Wpf.WebView2>()).ToArray();
                } while ((views.Length != 4 || views.Any(x => x.CoreWebView2 is null || x.Source?.Host != "play.tibidle.com")) && DateTime.UtcNow < deadline);
                Assert(views.Length == 4 && views.All(x => x.CoreWebView2 is not null), "Main window initializes four real browsers");
                Assert(views.Select(x => x.CoreWebView2.Profile.ProfileName).Distinct().Count() == 4, "Main window uses four distinct profiles");
                await Task.Delay(10000);
                for (var i = 0; i < 4; i++)
                {
                    ((Button)tabs.Children[i]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(((Grid)host.Children[i]).Visibility == Visibility.Visible, "Tab selects its browser");
                    Assert(host.Children.OfType<Grid>().Count(x => x.Visibility == Visibility.Visible) == 1, "Only selected tab is visible");
                    Assert(views[i].Source?.Host == "play.tibidle.com", "Game URL loaded");
                    Console.WriteLine("Game document: " + await views[i].ExecuteScriptAsync("JSON.stringify({title:document.title,state:document.readyState,text:document.body.innerText.slice(0,160)})"));
                }
                var navigations = 0;
                for (var i = 0; i < views.Length; i++)
                {
                    await views[i].ExecuteScriptAsync($"window.__layoutCheck = 'retained-{i}'");
                    views[i].CoreWebView2.NavigationStarting += (_, _) => navigations++;
                }
                var layoutButton = (Button)window.FindName("LayoutButton");
                layoutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert(host.Children.OfType<Grid>().All(p => p.Visibility == Visibility.Visible), "Shell button displays all four browsers");
                Assert(new SettingsStore(testRoot).Load().GridView, "Shell saves layout choice");
                ((Button)tabs.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(host.Children.OfType<Grid>().All(p => p.Visibility == Visibility.Visible), "Account selection preserves grid view");
                layoutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert(((Grid)host.Children[1]).Visibility == Visibility.Visible && host.Children.OfType<Grid>().Count(p => p.Visibility == Visibility.Visible) == 1, "Shell restores selected browser in normal mode");
                for (var i = 0; i < views.Length; i++)
                    Assert(await views[i].ExecuteScriptAsync("window.__layoutCheck") == $"\"retained-{i}\"", "Layout switch preserves live page state");
                Assert(navigations == 0, "Layout switch does not navigate or reload");
                var firstTab = (Button)tabs.Children[0];
                var lastTab = (Button)tabs.Children[3];
                // WPF's event constructor is internal; create the routed drop event in the test harness.
                var drop = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, new object[] { new DataObject("TibidleMultiManager.Tab", firstTab), DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, lastTab, new Point() }, null)!;
                drop.RoutedEvent = DragDrop.PreviewDropEvent;
                lastTab.RaiseEvent(drop);
                Assert(ReferenceEquals(tabs.Children[3], firstTab), "Dropping a tab moves it to the target position");
                Assert(new SettingsStore(testRoot).Load().TabOrder.SequenceEqual(new[] { 1, 2, 3, 0 }), "Drop saves new order immediately");
                layoutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert(Grid.GetRow((Grid)host.Children[0]) == 1 && Grid.GetColumn((Grid)host.Children[0]) == 1, "Moved account occupies bottom-right quadrant");
                Assert(Grid.GetRow((Grid)host.Children[1]) == 0 && Grid.GetColumn((Grid)host.Children[1]) == 0, "Second account occupies first quadrant");
                for (var i = 0; i < views.Length; i++)
                    Assert(await views[i].ExecuteScriptAsync("window.__layoutCheck") == $"\"retained-{i}\"", "Reorder preserves live page state and profile");
                Assert(navigations == 0, "Drop does not reload any account");
                var restored = new MainWindow(new SettingsStore(testRoot));
                Assert(((Button)((StackPanel)restored.FindName("Tabs")).Children[0]).Content?.ToString() == "Conta 2", "Reopened window restores saved tab order");
                restored.Close();
                ((Button)tabs.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(500);
                var screenshot = Path.Combine(Path.GetTempPath(), "Tibidle-game-smoke.png");
                using (var output = File.Create(screenshot))
                    await views[0].CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                Console.WriteLine("Screenshot: " + screenshot);
                Console.WriteLine("PASS: real game shell smoke check.");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { window.Close(); app.Shutdown(); }
        };
        app.Run(window);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    private static async Task Navigate(BrowserSession session, string url)
    {
        var loaded = new TaskCompletionSource();
        void Completed(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess) loaded.TrySetResult();
            else loaded.TrySetException(new Exception(e.WebErrorStatus.ToString()));
        }
        session.View.CoreWebView2.NavigationCompleted += Completed;
        try { session.View.CoreWebView2.Navigate(url); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { session.View.CoreWebView2.NavigationCompleted -= Completed; }
    }
}
