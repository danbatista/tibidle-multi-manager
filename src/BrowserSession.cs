using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TibidleMultiManager;

public sealed class BrowserSession : IDisposable
{
    public AccountSettings Account { get; }
    public WebView2 View { get; }
    public bool Ready => View.CoreWebView2 is not null;

    public BrowserSession(AccountSettings account)
    {
        Account = account;
        View = new WebView2
        {
            CreationProperties = new CoreWebView2CreationProperties
            {
                ProfileName = account.ProfileName,
                IsInPrivateModeEnabled = false
            },
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(15, 17, 22)
        };
    }

    public async Task InitializeAsync(CoreWebView2Environment environment)
    {
        await View.EnsureCoreWebView2Async(environment);
        View.CoreWebView2.IsMuted = Account.Muted;
        View.CoreWebView2.Settings.IsStatusBarEnabled = false;
        View.CoreWebView2.Settings.IsZoomControlEnabled = true;
    }

    public void Dispose() => View.Dispose();
}
