using System.Text;

namespace Migrator.Lab.LabApp;

public sealed record LabAppResponse(int StatusCode, string ContentType, byte[] Body)
{
    public static LabAppResponse Html(string html) => new(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    public static LabAppResponse Json(string json) => new(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
    public static LabAppResponse NotFound(string path) => new(404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes($"Unknown LabApp route: {path}\n"));
}

public static class LabAppPageCatalog
{
    public static readonly string[] PageRoutes =
    {
        "/login",
        "/list",
        "/helper",
        "/wait",
        "/smoke",
        "/unsupported"
    };

    public static LabAppResponse Resolve(string path)
    {
        return path switch
        {
            "/" => LabAppResponse.Html(BuildIndex()),
            "/health" => LabAppResponse.Json("{\"status\":\"ok\",\"schemaVersion\":\"migrator-lab-app/v0\"}\n"),
            "/login" => LabAppResponse.Html(BuildLogin()),
            "/list" => LabAppResponse.Html(BuildList()),
            "/helper" => LabAppResponse.Html(BuildHelper()),
            "/wait" => LabAppResponse.Html(BuildWait()),
            "/smoke" => LabAppResponse.Html(BuildSmoke()),
            "/unsupported" => LabAppResponse.Html(BuildUnsupported()),
            _ => LabAppResponse.NotFound(path)
        };
    }

    static string BuildIndex()
    {
        var links = string.Join(Environment.NewLine, PageRoutes.Select(route => $"<li><a href=\"{route}\">{route}</a></li>"));
        return BuildDocument("Migrator LabApp", $"<h1>Migrator LabApp v0</h1><ul>{links}</ul>", "");
    }

    static string BuildLogin() => BuildDocument(
        "Login",
        """
        <main>
          <label>Username <input id="username" autocomplete="off"></label>
          <label>Password <input id="password" type="password"></label>
          <button id="login" type="button">Sign in</button>
          <div id="result" hidden></div>
        </main>
        """,
        """
        document.getElementById('login').addEventListener('click', () => {
          labEmit('auth:attempt');
          const user = document.getElementById('username').value;
          const password = document.getElementById('password').value;
          if (user === 'john' && password === 'secret') {
            const result = document.getElementById('result');
            result.textContent = 'ok';
            result.hidden = false;
            labEmit('auth:success');
          }
        });
        """);

    static string BuildList() => BuildDocument(
        "List",
        """
        <main>
          <ul id="items">
            <li class="item">alpha</li>
            <li class="item">beta</li>
            <li class="item">gamma</li>
          </ul>
        </main>
        """,
        "labEmit('list:ready');");

    static string BuildHelper() => BuildDocument(
        "Helper",
        """
        <main>
          <button id="helper-button" type="button">Run helper</button>
          <div id="helper-status">idle</div>
        </main>
        """,
        """
        document.getElementById('helper-button').addEventListener('click', () => {
          document.getElementById('helper-status').textContent = 'done';
          labEmit('helper:click');
        });
        """);

    static string BuildWait() => BuildDocument(
        "Wait",
        """
        <main>
          <button id="wait-button" type="button" hidden>Continue</button>
          <div id="wait-status">waiting</div>
        </main>
        """,
        """
        const waitButton = document.getElementById('wait-button');
        setTimeout(() => {
          waitButton.hidden = false;
          labEmit('wait:visible');
        }, 250);
        waitButton.addEventListener('click', () => {
          document.getElementById('wait-status').textContent = 'clicked';
          labEmit('wait:click');
        });
        """);

    static string BuildSmoke() => BuildDocument(
        "Smoke",
        """
        <main>
          <button id="smoke-button" type="button">Run smoke</button>
          <div id="smoke-status">idle</div>
        </main>
        """,
        """
        document.getElementById('smoke-button').addEventListener('click', () => {
          document.getElementById('smoke-status').textContent = 'ok';
          labEmit('smoke:click');
        });
        """);

    static string BuildUnsupported() => BuildDocument(
        "Unsupported",
        """
        <main>
          <button id="unsupported-button" type="button">Run neighbour action</button>
          <div id="unsupported-status">idle</div>
          <div id="script-target">script-pending</div>
        </main>
        """,
        """
        document.getElementById('unsupported-button').addEventListener('click', () => {
          document.getElementById('unsupported-status').textContent = 'ok';
          labEmit('unsupported:neighbour-click');
        });
        """);

    static string BuildDocument(string title, string body, string pageScript) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{title}}</title>
          <style>
            body { font-family: system-ui, sans-serif; margin: 2rem; }
            main { display: grid; gap: 0.75rem; max-width: 32rem; }
            label { display: grid; gap: 0.25rem; }
            input, button { font: inherit; padding: 0.5rem; }
            #lab-event-log { margin-top: 2rem; white-space: pre-wrap; }
          </style>
        </head>
        <body>
          {{body}}
          <pre id="lab-event-log">[]</pre>
          <script>
            window.__migratorLab = { events: [] };
            window.labEmit = function(name) {
              window.__migratorLab.events.push(name);
              document.getElementById('lab-event-log').textContent = JSON.stringify(window.__migratorLab.events);
            };
            {{pageScript}}
          </script>
        </body>
        </html>
        """;
}
