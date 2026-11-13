using System.Diagnostics;
using System.Text.Json;
using System.Windows.Interop;
using ASPAssistant.Core;
using Microsoft.Web.WebView2.Core;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private HwndSource? _sklandGuidesSource;
    private CoreWebView2Controller? _sklandGuidesController;
    private SklandGuideQueue? _sklandGuidesQueue;
    private JsonElement _sklandGuidesResources;
    private bool _sklandGuidesLoggedIn;
    private string? _sklandGuidesFirstPage;
    private readonly Dictionary<string, int> _sklandGuidePages = [];
    private SklandGuideQueue GuideQueue => _sklandGuidesQueue ??= new(CloseSklandGuides, TimeProvider.System, SynchronizationContext.Current);

    public Task<SklandGuidePage> SklandGuidesAsync(SklandGuideQuery query)
    {
        var generation = _sklandLogoutGeneration;
        return GuideQueue.RunAsync(async () =>
        {
            var watch = Stopwatch.StartNew();
            SklandGuidePage? page = null;
            var pageNumber = string.IsNullOrEmpty(query.PageToken) ? 1 : _sklandGuidePages.GetValueOrDefault(query.PageToken, 2);
            try
            {
                if (generation != _sklandLogoutGeneration) throw new SklandException("森空岛已退出登录，请重新读取攻略列表。");
                if (_sklandLoginActive) throw new SklandException("森空岛窗口已经开着");
                if (_sklandGuidesController is null) await OpenSklandGuidesAsync();
                var request = SklandParser.GuideQuery(query, _sklandGuidesResources, Catalog);
                if (!_sklandGuidesLoggedIn && (query.Section != "all" || query.Sort == "latest" || !string.IsNullOrEmpty(query.PageToken)))
                    return page = new([], null, true);

                // 首屏只在查询完全一致时复用；其他查询至多多一次页面自己的首屏请求。
                var first = _sklandGuidesFirstPage;
                _sklandGuidesFirstPage = null;
                if (first is not null && query.Section == "all" && query.Sort != "latest"
                    && string.IsNullOrEmpty(query.PageToken) && request.Parameters.Count == 4)
                    page = SklandParser.ParseGuidePage(first, query.PageToken, !_sklandGuidesLoggedIn, decoded: false);
                else
                {
                    var result = await GuideScriptAsync("""
                        const request = ARGUMENT;
                        return await window.__assistantGuideService[request.method](request.parameters);
                        """, request);
                    page = SklandParser.ParseGuidePage(result.GetRawText(), query.PageToken, !_sklandGuidesLoggedIn);
                }
                if (pageNumber == 1) _sklandGuidePages.Clear();
                if (page.PageToken is { } token) _sklandGuidePages[token] = pageNumber + 1;
                return page;
            }
            catch (Exception ex)
            {
                CloseSklandGuides();
                if (ex is SklandException or ArgumentException) throw;
                // 不记录网络错误原文，避免服务回显账号或凭据。
                throw new SklandException($"森空岛攻略列表读不出来，稍后再试。\n{ex.GetType().Name}: HRESULT 0x{ex.HResult:X8}");
            }
            finally
            {
                var section = query.Section is "all" or "collected" or "published" ? query.Section : "invalid";
                var sort = query.Sort == "latest" ? "latest" : "recommended";
                AppLogger.Info("SklandGuides", $"section={section} sort={sort} keyword={!string.IsNullOrWhiteSpace(query.Keyword)} " +
                    $"filters={(query.Core is null ? 0 : 1) + (query.Extras?.Length ?? 0) + (query.StrategyId is null ? 0 : 1)} " +
                    $"page={pageNumber} count={page?.Items.Length ?? 0} elapsedMs={watch.ElapsedMilliseconds} loginRequired={page?.LoginRequired.ToString() ?? "unknown"}");
            }
        });
    }

    private void CloseSklandGuides()
    {
        _sklandGuidesController?.Close();
        _sklandGuidesController = null;
        _sklandGuidesSource?.Dispose();
        _sklandGuidesSource = null;
        _sklandGuidesFirstPage = null;
        _sklandGuidePages.Clear();
    }

    private async Task OpenSklandGuidesAsync()
    {
        _sklandGuidesSource = new HwndSource(new HwndSourceParameters("SklandGuides")
        { Width = 480, Height = 800, WindowStyle = unchecked((int)0x80000000) });
        var environment = await SklandEnvironmentAsync();
        _sklandGuidesController = await environment.CreateCoreWebView2ControllerAsync(_sklandGuidesSource.Handle);
        _sklandGuidesController.Bounds = new System.Drawing.Rectangle(0, 0, 480, 800);
        _sklandGuidesController.IsVisible = true;
        var core = _sklandGuidesController.CoreWebView2;
        ConfigureSkland(core, false);
        var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPage = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Navigation(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess) navigated.TrySetResult();
            else navigated.TrySetException(new SklandException($"打不开森空岛，检查一下网络再试。\nWebErrorStatus: {args.WebErrorStatus}"));
        }
        async void Response(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
        {
            if (firstPage.Task.IsCompleted || !IsSklandResponse(args.Request.Uri, "index")) return;
            try
            {
                if (args.Response.StatusCode != 200) throw new SklandException(
                    $"森空岛攻略列表读不出来，稍后再试。\nHTTP {args.Response.StatusCode}");
                using var stream = await args.Response.GetContentAsync();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                using var document = JsonDocument.Parse(json);
                var code = document.RootElement.GetProperty("code").GetInt32();
                if (code != 0) throw new SklandException($"森空岛攻略列表读不出来，稍后再试。\nServiceCode: {code}");
                firstPage.TrySetResult(json);
            }
            catch (Exception ex) { firstPage.TrySetException(ex); }
        }
        core.NavigationCompleted += Navigation;
        core.WebResourceResponseReceived += Response;
        try
        {
            core.Navigate("https://game.skland.com/arknights/stronghold-guide");
            await navigated.Task.WaitAsync(TimeSpan.FromSeconds(30));
            _sklandGuidesFirstPage = await firstPage.Task.WaitAsync(TimeSpan.FromSeconds(30));
            _sklandGuidesResources = await GuideScriptAsync(GuideInitializeScript, null);
            _sklandGuidesLoggedIn = _sklandGuidesResources.GetProperty("loggedIn").GetBoolean();
        }
        finally
        {
            core.NavigationCompleted -= Navigation;
            core.WebResourceResponseReceived -= Response;
        }
    }

    private const string GuideInitializeScript = """
        const loaded = performance.getEntriesByType('resource').map(e => e.name);
        let service;
        for (const url of [...new Set(loaded)].filter(url => /^https:\/\/assets\.skland\.com\/_static_assets\/game-tools\/dist-[^/]+\.js$/.test(url))) {
            const module = await import(url);
            if (typeof module.t?.getGuideList === 'function') { service = module.t; break; }
        }
        if (!service) throw new Error('GuideServiceUnavailable');
        // 首屏返回后资源应已就绪；只读取已有缓存，不另发资源请求。
        const resources = service.staticInfoCache.get(service.getCacheKey());
        if (!resources || !service.currentSeasonId || service.currentSeasonId === 'current') throw new Error('GuideResourceUnavailable');
        const entryUrl = loaded.find(url => /^https:\/\/assets\.skland\.com\/_static_assets\/game-tools\/entry_index-[^/]+\.js$/.test(url));
        if (!entryUrl) throw new Error('GuideAuthUnavailable');
        const auth = (await import(entryUrl)).n;
        await auth.ready();
        window.__assistantGuideService = service;
        return { seasonId: service.currentSeasonId, factions: Object.values(resources.factionMap),
                 strategyIds: resources.strategyIds, loggedIn: !!auth.isLogin };
        """;

    private async Task<JsonElement> GuideScriptAsync(string body, object? argument)
    {
        var core = _sklandGuidesController!.CoreWebView2;
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid().ToString("N");
        void Message(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (!SklandNavigationAllowed(args.Source)) return;
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("assistantGuideRequest", out var key) || key.GetString() != id) return;
            if (root.TryGetProperty("error", out var error))
                completion.TrySetException(new SklandException("森空岛攻略列表读不出来，稍后再试。\nServiceCode: " + error.GetInt64()));
            else completion.TrySetResult(root.GetProperty("result").Clone());
        }
        core.WebMessageReceived += Message;
        try
        {
            // 参数先独立序列化，再作为数据传入固定函数；不替换正文中的用户字符串。
            var script = "(async (ARGUMENT) => { try { const result = await (async () => {" + body +
                "})(); chrome.webview.postMessage({assistantGuideRequest:'" + id + "',result}); } catch(e) {" +
                "chrome.webview.postMessage({assistantGuideRequest:'" + id + "',error:Number.isSafeInteger(e?.code)?e.code:-1}); } })(" +
                JsonSerializer.Serialize(argument, JsonOptions) + ");";
            await core.ExecuteScriptAsync(script);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { core.WebMessageReceived -= Message; }
    }
}
