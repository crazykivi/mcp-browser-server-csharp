using PuppeteerSharp;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace BrowserMcpServer;

public class BrowserService : IAsyncDisposable
{
    private IBrowser? _browser;
    private IPage? _activePage;
    private readonly Dictionary<int, IPage> _pages = new();
    private int _nextPageId = 1;
    private bool _disposed;
    private readonly bool _headless;
    private readonly string? _proxy;
    private readonly string _userAgent;

    public BrowserService(bool headless = false, string? proxy = null)
    {
        _headless = headless;
        _proxy = proxy;
        _userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    }

    public IPage ActivePage => _activePage ?? throw new InvalidOperationException("No active page");

    public async Task<IPage> GetPageAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BrowserService));
        if (_browser == null || _browser.IsClosed) await LaunchBrowserAsync();
        if (_activePage == null || _activePage.IsClosed) _activePage = await CreateNewPageAsync();
        return _activePage;
    }

    public async Task<IPage> CreateNewPageAsync()
    {
        if (_browser == null || _browser.IsClosed) await LaunchBrowserAsync();
        var page = await _browser!.NewPageAsync();
        await page.SetUserAgentAsync(_userAgent);
        await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080, DeviceScaleFactor = 1 });
        var id = _nextPageId++;
        _pages[id] = page;
        page.Close += (s, e) => _pages.Remove(id);
        return page;
    }

    public IReadOnlyDictionary<int, IPage> GetAllPages() => _pages.AsReadOnly();

    public void SetActivePage(IPage page) => _activePage = page;

    public int GetPageId(IPage page) => _pages.FirstOrDefault(p => p.Value == page).Key;

    public async Task<bool> ClosePageAsync(IPage page)
    {
        if (_pages.Values.Contains(page))
        {
            var id = _pages.First(p => p.Value == page).Key;
            _pages.Remove(id);
            if (!page.IsClosed) await page.CloseAsync();
            if (_activePage == page) _activePage = _pages.Values.FirstOrDefault();
            return true;
        }
        return false;
    }

    public async Task ClearCookiesAsync()
    {
        var page = await GetPageAsync();
        await page.DeleteCookieAsync(await page.GetCookiesAsync());
    }

    public async Task SetCookiesAsync(IEnumerable<CookieParam> cookies)
    {
        var page = await GetPageAsync();
        await page.SetCookieAsync(cookies.ToArray());
    }

    public async Task<IEnumerable<CookieParam>> GetCookiesAsync()
    {
        var page = await GetPageAsync();
        return await page.GetCookiesAsync();
    }

    public async Task<string> GetLocalStorageAsync(string key)
    {
        var page = await GetPageAsync();
        return await page.EvaluateExpressionAsync<string>($@"
            (function() {{
                return localStorage.getItem('{key}');
            }})()
        ");
    }

    public async Task SetLocalStorageAsync(string key, string value)
    {
        var page = await GetPageAsync();
        await page.EvaluateExpressionAsync($@"
            (function() {{
                localStorage.setItem('{key}', '{value}');
            }})()
        ");
    }

    public async Task ClearLocalStorageAsync()
    {
        var page = await GetPageAsync();
        await page.EvaluateExpressionAsync("localStorage.clear()");
    }

    private async Task LaunchBrowserAsync()
    {
        var debugUrl = "http://localhost:9222/json/version";
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(3);
            var response = await http.GetStringAsync(debugUrl);
            var json = JObject.Parse(response);
            var wsUrl = json["webSocketDebuggerUrl"]?.ToString();
            if (!string.IsNullOrEmpty(wsUrl))
            {
                _browser = await Puppeteer.ConnectAsync(new ConnectOptions { BrowserWSEndpoint = wsUrl });
                return;
            }
        }
        catch
        {
        }

        var fetcher = new BrowserFetcher();
        await fetcher.DownloadAsync();

        var launchOptions = new LaunchOptions
        {
            Headless = _headless,
            Args = new[]
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--disable-gpu",
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-background-networking",
                "--disable-renderer-backgrounding",
                "--disable-background-timer-throttling",
                "--disable-blink-features=AutomationControlled",
                "--window-size=1920,1080"
            }
        };

        if (!string.IsNullOrEmpty(_proxy))
        {
            launchOptions.Args = launchOptions.Args.Append($"--proxy-server={_proxy}").ToArray();
        }

        _browser = await Puppeteer.LaunchAsync(launchOptions);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            if (_browser != null && !_browser.IsClosed) await _browser.CloseAsync();
        }
        catch { }
        finally
        {
            _browser?.Dispose();
            _pages.Clear();
            _activePage = null;
            _browser = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}