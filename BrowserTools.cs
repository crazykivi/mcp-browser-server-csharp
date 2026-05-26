using System.ComponentModel;
using ModelContextProtocol.Server;
using PuppeteerSharp;

namespace BrowserMcpServer;

[McpServerToolType]
public class BrowserTools(BrowserService browser)
{
    [McpServerTool, Description("Открывает страницу по URL. Используйте перед другими действиями.")]
    public async Task<string> Navigate([Description("URL страницы")] string url)
    {
        try
        {
            var page = await browser.GetPageAsync();
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                Timeout = 60000
            });
            await Task.Delay(500);
            return $"OK: Открыта страница {page.Url}\nЗаголовок: {await page.GetTitleAsync()}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Делает скриншот страницы или элемента. Возвращает путь к файлу.")]
    public async Task<string> Screenshot(
        [Description("Имя файла (без расширения)")] string name,
        [Description("CSS-селектор элемента (опционально)")] string? selector = null,
        [Description("Ширина в пикселях (опционально)")] int? width = null,
        [Description("Высота в пикселях (опционально)")] int? height = null)
    {
        try
        {
            var page = await browser.GetPageAsync();
            var path = Path.Combine(Path.GetTempPath(), $"{name}.png");
            
            if (!string.IsNullOrEmpty(selector))
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element == null) return $"ERROR: Элемент '{selector}' не найден";
                await element.ScreenshotAsync(path, new ElementScreenshotOptions
                {
                    Type = ScreenshotType.Png
                });
            }
            else
            {
                if (width.HasValue && height.HasValue)
                    await page.SetViewportAsync(new ViewPortOptions { Width = width.Value, Height = height.Value });
                await page.ScreenshotAsync(path, new ScreenshotOptions
                {
                    Type = ScreenshotType.Png
                });
            }
            return $"OK: Скриншот сохранён: {path}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Кликает элемент. Поддерживает CSS-селектор, текст, атрибут, XPath.")]
    public async Task<string> Click(
        [Description("CSS-селектор ИЛИ текст для поиска")] string target,
        [Description("Тип поиска: 'css', 'text', 'attr', 'xpath'")] string mode = "css",
        [Description("Доп. параметр: индекс или имя атрибута")] string? extra = null,
        [Description("Таймаут в мс")] int timeout = 10000)
    {
        try
        {
            var page = await browser.GetPageAsync();

            if (mode == "text")
            {
                var idx = string.IsNullOrEmpty(extra) ? 0 : int.Parse(extra);
                var escapedTarget = target.Replace("\\", "\\\\").Replace("'", "\\'").Replace("`", "\\`");

                var result = await page.EvaluateExpressionAsync<bool>($@"
                    (function() {{
                        const searchText = '{escapedTarget}';
                        const index = {idx};
                        const candidates = Array.from(document.querySelectorAll('*'))
                            .filter(el => {{
                                const text = el.innerText?.trim();
                                if (!text) return false;
                                const matches = text === searchText || text.includes(searchText);
                                if (!matches) return false;
                                const rect = el.getBoundingClientRect();
                                const style = window.getComputedStyle(el);
                                return rect.width > 0 && rect.height > 0 
                                     && style.visibility !== 'hidden' 
                                     && style.display !== 'none'
                                     && !el.hasAttribute('disabled');
                            }});
                        if (candidates[index]) {{
                            candidates[index].scrollIntoView({{behavior: 'smooth', block: 'center'}});
                            candidates[index].click();
                            return true;
                        }}
                        return false;
                    }})()
                ");
                return result
                    ? $"OK: Клик по тексту '{target}'{(idx > 0 ? $" (индекс {idx})" : "")}"
                    : $"ERROR: Не найден элемент с текстом '{target}'";
            }

            if (mode == "attr")
            {
                if (string.IsNullOrEmpty(extra))
                    return "ERROR: Для mode='attr' укажите имя атрибута в параметре extra";

                var escapedTarget = target.Replace("\\", "\\\\").Replace("'", "\\'").Replace("`", "\\`");
                var escapedAttr = extra.Replace("\\", "\\\\").Replace("'", "\\'").Replace("`", "\\`");

                var result = await page.EvaluateExpressionAsync<bool>($@"
                    (function() {{
                        const attr = '{escapedAttr}';
                        const value = '{escapedTarget}';
                        const el = document.querySelector(`[${{attr}}='${{value}}']`);
                        if (el) {{
                            el.scrollIntoView({{behavior: 'smooth', block: 'center'}});
                            el.click();
                            return true;
                        }}
                        return false;
                    }})()
                ");
                return result
                    ? $"OK: Клик по [{extra}='{target}']"
                    : $"ERROR: Не найден элемент с [{extra}='{target}']";
            }

            if (mode == "xpath")
            {
                var result = await page.EvaluateExpressionAsync<bool>($@"
                    (function() {{
                        const xpath = '{target}';
                        const result = document.evaluate(xpath, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null);
                        const el = result.singleNodeValue;
                        if (el && el.click) {{
                            el.scrollIntoView({{behavior: 'smooth', block: 'center'}});
                            el.click();
                            return true;
                        }}
                        return false;
                    }})()
                ");
                return result
                    ? $"OK: Клик по XPath '{target}'"
                    : $"ERROR: Не найден элемент по XPath '{target}'";
            }

            await page.WaitForSelectorAsync(target, new WaitForSelectorOptions
            {
                Timeout = timeout,
                Visible = true
            });

            if (!string.IsNullOrEmpty(extra) && int.TryParse(extra, out var index))
            {
                var escapedTarget = target.Replace("\\", "\\\\").Replace("'", "\\'").Replace("`", "\\`");

                var result = await page.EvaluateExpressionAsync<bool>($@"
                    (function() {{
                        const els = document.querySelectorAll(`{escapedTarget}`);
                        if (els[{index}]) {{
                            els[{index}].scrollIntoView({{behavior: 'smooth', block: 'center'}});
                            els[{index}].click();
                            return true;
                        }}
                        return false;
                    }})()
                ");
                return result
                    ? $"OK: Клик по '{target}' (индекс {index})"
                    : $"ERROR: Элемент '{target}' с индексом {index} не найден";
            }

            await page.ClickAsync(target);
            await Task.Delay(300);
            return $"OK: Клик по '{target}'";
        }
        catch (TimeoutException)
        {
            return $"ERROR: Таймаут ({timeout} мс) - элемент '{target}' не найден или не виден";
        }
        catch (FormatException) when (mode == "text" && !string.IsNullOrEmpty(extra))
        {
            return "ERROR: Параметр 'extra' для mode='text' должен быть числом (индекс)";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Наводит курсор на элемент по CSS-селектору.")]
    public async Task<string> Hover([Description("CSS-селектор")] string selector)
    {
        try
        {
            var page = await browser.GetPageAsync();
            await page.HoverAsync(selector);
            return $"OK: Hover на '{selector}'";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Вводит текст в поле ввода по CSS-селектору.")]
    public async Task<string> Fill(
        [Description("CSS-селектор поля")] string selector,
        [Description("Текст для ввода")] string value)
    {
        try
        {
            var page = await browser.GetPageAsync();
            await page.WaitForSelectorAsync(selector, new WaitForSelectorOptions { Visible = true, Timeout = 10000 });
            await page.TypeAsync(selector, value);
            await Task.Delay(200);
            return $"OK: Введено '{value}' в '{selector}'";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Выбирает значение в <select> по CSS-селектору.")]
    public async Task<string> Select(
        [Description("CSS-селектор <select>")] string selector,
        [Description("Значение для выбора")] string value)
    {
        try
        {
            var page = await browser.GetPageAsync();
            await page.SelectAsync(selector, value);
            return $"OK: Выбрано '{value}' в '{selector}'";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Выполняет JavaScript в контексте страницы. Возвращает результат.")]
    public async Task<string> Evaluate([Description("JavaScript код")] string script)
    {
        try
        {
            var page = await browser.GetPageAsync();

            var trimmedScript = script.Trim();
            if (!trimmedScript.StartsWith("(function") && !trimmedScript.StartsWith("(()"))
            {
                script = $"(function() {{ {script} }})()";
            }

            var result = await page.EvaluateExpressionAsync<object>(script);

            string resultStr;
            if (result == null)
            {
                resultStr = "null";
            }
            else if (result is bool b)
            {
                resultStr = b.ToString().ToLower();
            }
            else if (result is string s)
            {
                resultStr = s;
            }
            else if (result is IConvertible)
            {
                resultStr = result.ToString()!;
            }
            else
            {
                resultStr = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            }

            return $"OK: {resultStr}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Извлекает структурированный контент страницы для LLM.")]
    public async Task<string> GetContent(
        [Description("Максимальная длина текста")] int maxLength = 3000,
        [Description("Включать ссылки")] bool includeLinks = true,
        [Description("Включать заголовки")] bool includeHeaders = true)
    {
        try
        {
            var page = await browser.GetPageAsync();
            var title = await page.GetTitleAsync();
            var url = page.Url;

            if (title.Contains("CAPTCHA", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Проверка", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
            {
                return $"CAPTCHA: Обнаружена проверка на {url}. Решите вручную, затем повторите вызов.";
            }

            var script = $@"
                (function() {{
                    const content = {{
                        title: document.title,
                        url: window.location.href,
                        headers: [],
                        text: [],
                        links: [],
                        inputs: [],
                        buttons: []
                    }};

                    document.querySelectorAll('h1, h2, h3, h4, h5, h6').forEach(el => {{
                        const text = el.innerText.trim();
                        if (text) content.headers.push({{tag: el.tagName, text: text}});
                    }});

                    const contentTargets = document.querySelectorAll('main, article, section, .content, p, li, td, span');
                    const textParts = Array.from(contentTargets)
                        .map(el => el.innerText.trim())
                        .filter(t => t.length > 3 && t.length < 500);
                    content.text = textParts.slice(0, 50);

                    if ({includeLinks.ToString().ToLower()}) {{
                        document.querySelectorAll('a[href]').forEach(a => {{
                            const text = a.innerText.trim();
                            if (text && a.href) content.links.push({{text: text, href: a.href}});
                        }});
                        content.links = content.links.slice(0, 50);
                    }}

                    document.querySelectorAll('input, textarea').forEach(el => {{
                        content.inputs.push({{
                            type: el.type,
                            name: el.name,
                            placeholder: el.placeholder,
                            value: el.value
                        }});
                    }});

                    document.querySelectorAll('button, [role=""button""]').forEach(el => {{
                        content.buttons.push({{
                            text: el.innerText.trim(),
                            type: el.type
                        }});
                    }});

                    const fullText = content.text.join('\n').substring(0, {maxLength});
                    return JSON.stringify({{...content, fullText}});
                }})()
            ";

            var json = await page.EvaluateExpressionAsync<string>(script);
            return $"[CONTENT]\n{json}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Извлекает все ссылки со страницы.")]
    public async Task<string> GetLinks()
    {
        try
        {
            var page = await browser.GetPageAsync();
            var links = await page.EvaluateExpressionAsync<string[]>($@"
                (function() {{
                    return Array.from(document.querySelectorAll('a[href]'))
                        .map(a => `${{a.innerText.trim()}} | ${{a.href}}`)
                        .filter(l => l.split('|')[0].length > 0)
                        .slice(0, 100);
                }})()
            ");

            return $"Links on {page.Url}:\n" + string.Join("\n", links);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Возвращает HTML-код страницы (обрезанный).")]
    public async Task<string> GetHtml([Description("Максимальная длина")] int maxLength = 20000)
    {
        try
        {
            var page = await browser.GetPageAsync();
            var html = await page.GetContentAsync();
            var truncated = html.Length > maxLength ? html.Substring(0, maxLength) + "\n\n[...обрезано...]" : html;
            return truncated;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Возвращает текущий URL страницы.")]
    public async Task<string> GetUrl()
    {
        try
        {
            var page = await browser.GetPageAsync();
            return page.Url;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Перезагружает текущую страницу.")]
    public async Task<string> Reload()
    {
        try
        {
            var page = await browser.GetPageAsync();
            await page.ReloadAsync(
                waitUntil: new[] { WaitUntilNavigation.Networkidle2 },
                timeout: 60000);
            await Task.Delay(500);
            return $"OK: Страница перезагружена: {page.Url}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Прокручивает страницу.")]
    public async Task<string> Scroll(
        [Description("Направление: 'up', 'down', 'top', 'bottom'")] string direction = "down",
        [Description("Количество пикселей")] int pixels = 500)
    {
        try
        {
            var page = await browser.GetPageAsync();

            var script = direction switch
            {
                "up" => $"window.scrollBy(0, -{pixels})",
                "down" => $"window.scrollBy(0, {pixels})",
                "top" => "window.scrollTo(0, 0)",
                "bottom" => "window.scrollTo(0, document.body.scrollHeight)",
                _ => $"window.scrollBy(0, {pixels})"
            };

            await page.EvaluateExpressionAsync(script);
            await Task.Delay(300);

            var scrollPos = await page.EvaluateExpressionAsync<int>("window.scrollY");
            return $"OK: Прокрутка '{direction}', позиция: {scrollPos}px";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Ждёт появления элемента на странице.")]
    public async Task<string> WaitForElement(
        [Description("CSS-селектор")] string selector,
        [Description("Таймаут в мс")] int timeout = 10000,
        [Description("Должен быть видимым")] bool visible = true)
    {
        try
        {
            var page = await browser.GetPageAsync();
            await page.WaitForSelectorAsync(selector, new WaitForSelectorOptions
            {
                Timeout = timeout,
                Visible = visible
            });
            return $"OK: Элемент '{selector}' найден";
        }
        catch (TimeoutException)
        {
            return $"ERROR: Таймаут ({timeout} мс) - элемент '{selector}' не найден";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Показывает список всех открытых вкладок.")]
    public async Task<string> GetTabs()
    {
        try
        {
            var pages = browser.GetAllPages();
            if (pages.Count == 0) return "Нет открытых вкладок";

            var list = new List<string>();
            foreach (var (id, page) in pages)
            {
                var title = await page.GetTitleAsync();
                var url = page.Url;
                var active = page == browser.ActivePage ? " [ACTIVE]" : "";
                list.Add($"[{id}]{active} {title} | {url}");
            }
            return "Открытые вкладки:\n" + string.Join("\n", list);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Переключается на вкладку по ID.")]
    public async Task<string> SwitchTab([Description("ID вкладки из GetTabs()")] int tabId)
    {
        try
        {
            var pages = browser.GetAllPages();
            if (!pages.TryGetValue(tabId, out var page))
                return $"ERROR: Вкладка с ID {tabId} не найдена. Используй GetTabs() для просмотра доступных.";

            browser.SetActivePage(page);
            await page.BringToFrontAsync();
            return $"OK: Переключён на вкладку [{tabId}] {await page.GetTitleAsync()} | {page.Url}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Открывает новую вкладку.")]
    public async Task<string> NewTab([Description("URL для открытия (опционально)")] string? url = null)
    {
        try
        {
            var page = await browser.CreateNewPageAsync();
            browser.SetActivePage(page);

            if (!string.IsNullOrEmpty(url))
            {
                await page.GoToAsync(url, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                    Timeout = 60000
                });
            }

            var id = browser.GetPageId(page);
            return $"OK: Новая вкладка [{id}] создана{(!string.IsNullOrEmpty(url) ? $", открыта {url}" : "")}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Закрывает вкладку по ID.")]
    public async Task<string> CloseTab([Description("ID вкладки (опционально)")] int? tabId = null)
    {
        try
        {
            IPage? pageToClose = null;

            if (tabId.HasValue)
            {
                var pages = browser.GetAllPages();
                if (!pages.TryGetValue(tabId.Value, out var p))
                    return $"ERROR: Вкладка с ID {tabId.Value} не найдена";
                pageToClose = p;
            }
            else
            {
                pageToClose = browser.ActivePage;
            }

            if (pageToClose == null) return "ERROR: Нет активной вкладки для закрытия";

            var id = browser.GetPageId(pageToClose);
            var title = await pageToClose.GetTitleAsync();

            await browser.ClosePageAsync(pageToClose);

            return $"OK: Вкладка [{id}] '{title}' закрыта";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Возвращает структуру DOM для анализа страницы.")]
    public async Task<string> GetDomStructure(
        [Description("Максимальная глубина")] int maxDepth = 3,
        [Description("Максимальное количество элементов")] int maxElements = 100)
    {
        try
        {
            var page = await browser.GetPageAsync();

            var script = $@"
                (function() {{
                    function buildTree(node, depth) {{
                        if (depth > {maxDepth} || node.nodeType !== 1) return null;
                        
                        const el = {{
                            tag: node.tagName?.toLowerCase(),
                            id: node.id || null,
                            class: node.className || null,
                            text: node.innerText?.trim().substring(0, 50) || null,
                            children: []
                        }};

                        if (['SCRIPT', 'STYLE', 'META', 'LINK', 'HEAD'].includes(node.tagName)) {{
                            return null;
                        }}

                        const interactive = ['A', 'BUTTON', 'INPUT', 'SELECT', 'TEXTAREA', 'FORM'];
                        if (interactive.includes(node.tagName)) {{
                            el.interactive = true;
                        }}

                        let count = 0;
                        for (const child of node.children) {{
                            if (count >= {maxElements}) break;
                            const childTree = buildTree(child, depth + 1);
                            if (childTree) {{
                                el.children.push(childTree);
                                count++;
                            }}
                        }}

                        return el;
                    }}

                    const root = buildTree(document.body, 0);
                    return JSON.stringify(root);
                }})()
            ";

            var result = await page.EvaluateExpressionAsync<string>(script);
            return $"[DOM]\n{result}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Находит интерактивные элементы на странице.")]
    public async Task<string> GetInteractiveElements()
    {
        try
        {
            var page = await browser.GetPageAsync();

            var result = await page.EvaluateExpressionAsync<string>($@"
                (function() {{
                    const elements = [];
                    
                    document.querySelectorAll('button, [role=""button""], a[href], input, select, textarea, [onclick]')
                        .forEach((el, idx) => {{
                            if (idx > 100) return;
                            const rect = el.getBoundingClientRect();
                            if (rect.width === 0 || rect.height === 0) return;
                            
                            const style = window.getComputedStyle(el);
                            if (style.visibility === 'hidden' || style.display === 'none') return;
                            
                            elements.push({{
                                index: idx,
                                tag: el.tagName.toLowerCase(),
                                id: el.id || null,
                                class: el.className || null,
                                text: el.innerText?.trim().substring(0, 100) || el.value?.toString().substring(0, 100) || null,
                                type: el.type || null,
                                selector: el.id ? `#${{el.id}}` : 
                                         el.className ? `.${{el.className.split(' ')[0]}}` : 
                                         el.tagName.toLowerCase()
                            }});
                        }});
                    
                    return JSON.stringify(elements);
                }})()
            ");

            return $"[INTERACTIVE]\n{result}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Управляет cookies страницы.")]
    public async Task<string> ManageCookies(
        [Description("Действие: 'get', 'set', 'clear'")] string action,
        [Description("JSON с cookies для set")] string? cookiesJson = null)
    {
        try
        {
            if (action == "get")
            {
                var cookies = await browser.GetCookiesAsync();
                return $"OK: {Newtonsoft.Json.JsonConvert.SerializeObject(cookies)}";
            }

            if (action == "clear")
            {
                await browser.ClearCookiesAsync();
                return "OK: Cookies очищены";
            }

            if (action == "set" && !string.IsNullOrEmpty(cookiesJson))
            {
                var cookies = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<CookieParam>>(cookiesJson);
                if (cookies != null)
                {
                    await browser.SetCookiesAsync(cookies);
                    return "OK: Cookies установлены";
                }
            }

            return "ERROR: Неверные параметры";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Управляет localStorage страницы.")]
    public async Task<string> ManageLocalStorage(
        [Description("Действие: 'get', 'set', 'clear', 'keys'")] string action,
        [Description("Ключ для get/set")] string? key = null,
        [Description("Значение для set")] string? value = null)
    {
        try
        {
            if (action == "keys")
            {
                var page = await browser.GetPageAsync();
                var keys = await page.EvaluateExpressionAsync<string[]>("Object.keys(localStorage)");
                return $"OK: {Newtonsoft.Json.JsonConvert.SerializeObject(keys)}";
            }

            if (action == "get" && !string.IsNullOrEmpty(key))
            {
                var result = await browser.GetLocalStorageAsync(key);
                return $"OK: {result ?? "null"}";
            }

            if (action == "set" && !string.IsNullOrEmpty(key) && value != null)
            {
                await browser.SetLocalStorageAsync(key, value);
                return $"OK: {key} = {value}";
            }

            if (action == "clear")
            {
                await browser.ClearLocalStorageAsync();
                return "OK: localStorage очищен";
            }

            return "ERROR: Неверные параметры";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Делает скриншот с выделением интерактивных элементов для vision.")]
    public async Task<string> ScreenshotWithMarkers(
        [Description("Имя файла")] string name,
        [Description("Возвращать base64")] bool base64 = true)
    {
        try
        {
            var page = await browser.GetPageAsync();

            await page.EvaluateExpressionAsync($@"
                (function() {{
                    const style = document.createElement('style');
                    style.id = 'mcp-markers-style';
                    style.textContent = `
                        .mcp-marker {{
                            position: absolute;
                            border: 2px solid red;
                            background: rgba(255, 0, 0, 0.2);
                            pointer-events: none;
                            z-index: 99999;
                        }}
                        .mcp-label {{
                            position: absolute;
                            background: red;
                            color: white;
                            font-size: 12px;
                            padding: 2px 4px;
                            border-radius: 3px;
                            z-index: 100000;
                        }}
                    `;
                    document.head.appendChild(style);

                    document.querySelectorAll('button, [role=""button""], a[href], input, select, textarea')
                        .forEach((el, idx) => {{
                            if (idx > 50) return;
                            const rect = el.getBoundingClientRect();
                            if (rect.width === 0 || rect.height === 0) return;
                            
                            const marker = document.createElement('div');
                            marker.className = 'mcp-marker';
                            marker.style.left = rect.left + 'px';
                            marker.style.top = rect.top + 'px';
                            marker.style.width = rect.width + 'px';
                            marker.style.height = rect.height + 'px';
                            document.body.appendChild(marker);
                            
                            const label = document.createElement('div');
                            label.className = 'mcp-label';
                            label.textContent = idx;
                            label.style.left = rect.left + 'px';
                            label.style.top = (rect.top - 20) + 'px';
                            document.body.appendChild(label);
                            
                            el.dataset.mcpIndex = idx;
                        }});
                }})()
            ");

            await Task.Delay(500);

            var imageData = await page.ScreenshotDataAsync(new ScreenshotOptions
            {
                FullPage = true,
                Type = ScreenshotType.Png
            });

            await page.EvaluateExpressionAsync(@"
                (function() {
                    document.querySelectorAll('.mcp-marker, .mcp-label').forEach(el => el.remove());
                    const style = document.getElementById('mcp-markers-style');
                    if (style) style.remove();
                })()
            ");

            if (base64)
            {
                var base64String = Convert.ToBase64String(imageData);
                return $"OK: image/png;base64,{base64String}";
            }
            else
            {
                var path = Path.Combine(Path.GetTempPath(), $"{name}.png");
                await File.WriteAllBytesAsync(path, imageData);
                return $"OK: Скриншот сохранён: {path}";
            }
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Отправляет клавиши (Enter, Tab, и т.д.).")]
    public async Task<string> PressKey(
        [Description("Клавиша: 'Enter', 'Tab', 'Escape', 'ArrowDown', и т.д.")] string key)
    {
        try
        {
            var page = await browser.GetPageAsync();
            await page.Keyboard.PressAsync(key);
            await Task.Delay(200);
            return $"OK: Нажата клавиша '{key}'";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    [McpServerTool, Description("Ждёт указанное время в мс.")]
    public async Task<string> Wait([Description("Время в мс")] int milliseconds)
    {
        await Task.Delay(milliseconds);
        return $"OK: Ожидание {milliseconds} мс завершено";
    }

    [McpServerTool, Description("Возвращает информацию о странице (заголовок, URL, статус).")]
    public async Task<string> PageInfo()
    {
        try
        {
            var page = await browser.GetPageAsync();
            var title = await page.GetTitleAsync();
            var url = page.Url;

            var info = await page.EvaluateExpressionAsync<string>($@"
                (function() {{
                    return JSON.stringify({{
                        title: document.title,
                        url: window.location.href,
                        readyState: document.readyState,
                        hasForms: document.querySelectorAll('form').length > 0,
                        hasInputs: document.querySelectorAll('input, textarea').length,
                        hasButtons: document.querySelectorAll('button, [role=""button""]').length,
                        hasLinks: document.querySelectorAll('a[href]').length,
                        scrollHeight: document.body.scrollHeight,
                        scrollY: window.scrollY
                    }});
                }})()
            ");

            return $"[PAGE_INFO]\n{info}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }
}