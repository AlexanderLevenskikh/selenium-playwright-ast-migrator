using System.Text;

namespace Migrator.Lab.LabApp;

public sealed record LabAppResponse(int StatusCode, string ContentType, byte[] Body)
{
    public static LabAppResponse Html(string html) => new(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    public static LabAppResponse Json(string json) => new(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
    public static LabAppResponse Text(string text, string contentType = "text/plain; charset=utf-8") => new(200, contentType, Encoding.UTF8.GetBytes(text));
    public static LabAppResponse NotFound(string path) => new(404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes($"Unknown LabApp route: {path}\n"));
}

public static class LabAppPageCatalog
{
    public static readonly string[] PageRoutes =
    {
        "/login",
        "/edit",
        "/list",
        "/table",
        "/form",
        "/locator",
        "/helper",
        "/pom",
        "/modal",
        "/async",
        "/setup",
        "/wait",
        "/wait-negative",
        "/custom-wait",
        "/control-flow",
        "/parameterized",
        "/smoke",
        "/unsupported",
        "/actions",
        "/complex",
        "/dynamic"
    };

    public static LabAppResponse Resolve(string path)
    {
        return path switch
        {
            "/" => LabAppResponse.Html(BuildIndex()),
            "/health" => LabAppResponse.Json("{\"status\":\"ok\",\"schemaVersion\":\"migrator-lab-app/v1\"}\n"),
            "/login" => LabAppResponse.Html(BuildLogin()),
            "/edit" => LabAppResponse.Html(BuildEdit()),
            "/list" => LabAppResponse.Html(BuildList()),
            "/table" => LabAppResponse.Html(BuildTable()),
            "/form" => LabAppResponse.Html(BuildForm()),
            "/locator" => LabAppResponse.Html(BuildLocator()),
            "/helper" => LabAppResponse.Html(BuildHelper()),
            "/pom" => LabAppResponse.Html(BuildPom()),
            "/modal" => LabAppResponse.Html(BuildModal()),
            "/async" => LabAppResponse.Html(BuildAsync()),
            "/setup" => LabAppResponse.Html(BuildSetup()),
            "/wait" => LabAppResponse.Html(BuildWait()),
            "/wait-negative" => LabAppResponse.Html(BuildWaitNegative()),
            "/custom-wait" => LabAppResponse.Html(BuildCustomWait()),
            "/control-flow" => LabAppResponse.Html(BuildControlFlow()),
            "/parameterized" => LabAppResponse.Html(BuildParameterized()),
            "/smoke" => LabAppResponse.Html(BuildSmoke()),
            "/unsupported" => LabAppResponse.Html(BuildUnsupported()),
            "/actions" => LabAppResponse.Html(BuildActions()),
            "/complex" => LabAppResponse.Html(BuildComplex()),
            "/frame-content" => LabAppResponse.Html(BuildFrameContent()),
            "/popup-content" => LabAppResponse.Html(BuildPopupContent()),
            "/download/sample.txt" => LabAppResponse.Text("migrator-lab-download\n", "application/octet-stream"),
            "/dynamic" => LabAppResponse.Html(BuildDynamic()),
            _ => LabAppResponse.NotFound(path)
        };
    }

    static string BuildIndex()
    {
        var links = string.Join(Environment.NewLine, PageRoutes.Select(route => $"<li><a href=\"{route}\">{route}</a></li>"));
        return BuildDocument("Migrator LabApp", $"<h1>Migrator LabApp v1</h1><ul>{links}</ul>", "");
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

    static string BuildEdit() => BuildDocument(
        "Edit",
        """
        <main>
          <label>Name <input id="edit-name" class="name" value="old"></label>
          <button id="edit-save" type="button">Save</button>
          <div id="edit-status">idle</div>
        </main>
        """,
        """
        document.getElementById('edit-save').addEventListener('click', () => {
          document.getElementById('edit-status').textContent = 'saved';
          labEmit('edit:save');
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

    static string BuildTable() => BuildDocument(
        "Table",
        """
        <main>
          <table id="data"><tbody>
            <tr class="data-row"><td>1</td><td>alpha</td></tr>
            <tr class="data-row"><td>2</td><td>beta</td></tr>
            <tr class="data-row"><td>3</td><td>gamma</td></tr>
          </tbody></table>
        </main>
        """,
        "labEmit('table:ready');");

    static string BuildForm() => BuildDocument(
        "Form",
        """
        <main>
          <label><input id="terms" type="checkbox" checked> Terms</label>
          <label><input id="blocked" type="radio" disabled> Blocked choice</label>
          <div id="form-status">ready</div>
        </main>
        """,
        "labEmit('form:ready');");

    static string BuildLocator() => BuildDocument(
        "Locator",
        """
        <main>
          <button id="locator-primary" type="button">Primary</button>
          <button id="locator-secondary" type="button">Secondary</button>
          <div id="locator-status">idle</div>
        </main>
        """,
        """
        document.getElementById('locator-primary').addEventListener('click', () => {
          document.getElementById('locator-status').textContent = 'primary';
          labEmit('locator:primary');
        });
        document.getElementById('locator-secondary').addEventListener('click', () => {
          document.getElementById('locator-status').textContent = 'secondary';
          labEmit('locator:secondary');
        });
        """);

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

    static string BuildPom() => BuildDocument(
        "Page object",
        """
        <main>
          <input id="pom-user" autocomplete="off">
          <input id="pom-password" type="password">
          <button id="pom-login" type="button">Login</button>
          <div id="dashboard-status" hidden>idle</div>
        </main>
        """,
        """
        document.getElementById('pom-login').addEventListener('click', () => {
          labEmit('pom:login');
          const status = document.getElementById('dashboard-status');
          status.textContent = 'ready';
          status.hidden = false;
          labEmit('pom:dashboard');
        });
        """);

    static string BuildModal() => BuildDocument(
        "Modal composition",
        """
        <main>
          <button id="modal-open" type="button">Open</button>
          <section id="modal" hidden>
            <button id="modal-save" type="button">Save</button>
          </section>
          <div id="modal-status">idle</div>
        </main>
        """,
        """
        document.getElementById('modal-open').addEventListener('click', () => {
          document.getElementById('modal').hidden = false;
          labEmit('modal:open');
        });
        document.getElementById('modal-save').addEventListener('click', () => {
          document.getElementById('modal-status').textContent = 'saved';
          labEmit('modal:save');
        });
        """);

    static string BuildAsync() => BuildDocument(
        "Async lift",
        """
        <main>
          <button id="async-button" type="button">Run</button>
          <div id="async-status">idle</div>
        </main>
        """,
        """
        document.getElementById('async-button').addEventListener('click', () => {
          document.getElementById('async-status').textContent = 'done';
          labEmit('async:click');
        });
        """);

    static string BuildSetup() => BuildDocument(
        "Setup",
        """
        <main>
          <button id="setup-prepare" type="button">Prepare</button>
          <button id="setup-test" type="button">Test</button>
          <div id="setup-status">idle</div>
        </main>
        """,
        """
        document.getElementById('setup-prepare').addEventListener('click', () => {
          document.getElementById('setup-status').textContent = 'prepared';
          labEmit('setup:prepare');
        });
        document.getElementById('setup-test').addEventListener('click', () => {
          document.getElementById('setup-status').textContent = 'done';
          labEmit('setup:test');
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
        labEmit('wait:start');
        setTimeout(() => {
          waitButton.hidden = false;
          labEmit('wait:visible');
        }, 250);
        waitButton.addEventListener('click', () => {
          document.getElementById('wait-status').textContent = 'clicked';
          labEmit('wait:click');
        });
        """);

    static string BuildWaitNegative() => BuildDocument(
        "Negative wait",
        """
        <main>
          <div id="negative-spinner">loading</div>
          <button id="negative-save" type="button" disabled>Save</button>
          <div id="negative-status">waiting</div>
        </main>
        """,
        """
        const spinner = document.getElementById('negative-spinner');
        const save = document.getElementById('negative-save');
        labEmit('negative:start');
        setTimeout(() => {
          spinner.hidden = true;
          save.disabled = false;
          labEmit('negative:gone');
        }, 250);
        save.addEventListener('click', () => {
          document.getElementById('negative-status').textContent = 'saved';
          labEmit('negative:click');
        });
        """);

    static string BuildCustomWait() => BuildDocument(
        "Custom wait",
        """
        <main>
          <button id="custom-save" type="button" disabled>Save</button>
          <div id="custom-status">waiting</div>
        </main>
        """,
        """
        const save = document.getElementById('custom-save');
        setTimeout(() => {
          save.disabled = false;
          labEmit('custom:enabled');
        }, 250);
        save.addEventListener('click', () => {
          document.getElementById('custom-status').textContent = 'done';
          labEmit('custom:click');
        });
        """);

    static string BuildControlFlow() => BuildDocument(
        "Control flow",
        """
        <main>
          <button class="control-item" type="button">alpha</button>
          <button class="control-item" type="button">beta</button>
          <button class="control-item" type="button">gamma</button>
          <div id="control-status">idle</div>
        </main>
        """,
        """
        document.querySelectorAll('.control-item').forEach(button => {
          button.addEventListener('click', () => {
            const value = button.textContent.trim();
            document.getElementById('control-status').textContent = value;
            labEmit('control:' + value);
          });
        });
        """);

    static string BuildParameterized() => BuildDocument(
        "Parameterized",
        """
        <main>
          <button id="parameter-one" type="button">one</button>
          <button id="parameter-two" type="button">two</button>
          <div id="parameter-status">idle</div>
        </main>
        """,
        """
        ['one', 'two'].forEach(value => {
          document.getElementById('parameter-' + value).addEventListener('click', () => {
            document.getElementById('parameter-status').textContent = value;
            labEmit('parameter:' + value);
          });
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

    static string BuildActions() => BuildDocument(
        "Actions API",
        """
        <main>
          <button id="actions-target" type="button">Complex action</button>
          <button id="actions-neighbour" type="button">Neighbour</button>
          <div id="actions-status">idle</div>
        </main>
        """,
        """
        document.getElementById('actions-target').addEventListener('click', () => labEmit('actions:complex'));
        document.getElementById('actions-neighbour').addEventListener('click', () => {
          document.getElementById('actions-status').textContent = 'ok';
          labEmit('actions:neighbour-click');
        });
        """);

    static string BuildComplex() => BuildDocument(
        "Frames popup upload download",
        """
        <main>
          <iframe id="lab-frame" name="lab-frame" src="/frame-content"></iframe>
          <a id="popup-link" href="/popup-content" target="_blank">Open popup</a>
          <input id="upload-input" type="file">
          <a id="download-link" href="/download/sample.txt" download>Download</a>
          <button id="complex-neighbour" type="button">Neighbour</button>
          <div id="complex-status">idle</div>
        </main>
        """,
        """
        document.getElementById('upload-input').addEventListener('change', () => labEmit('complex:upload'));
        document.getElementById('complex-neighbour').addEventListener('click', () => {
          document.getElementById('complex-status').textContent = 'ok';
          labEmit('complex:neighbour-click');
        });
        """);

    static string BuildFrameContent() => BuildDocument(
        "Frame content",
        "<main><div id=\"frame-status\">inside-frame</div></main>",
        "");

    static string BuildPopupContent() => BuildDocument(
        "Popup content",
        "<main><div id=\"popup-status\">popup-ready</div></main>",
        "");

    static string BuildDynamic() => BuildDocument(
        "Dynamic",
        """
        <main>
          <button id="dynamic-target" type="button">Dynamic action</button>
          <button id="dynamic-neighbour" type="button">Neighbour</button>
          <div id="dynamic-status">idle</div>
        </main>
        """,
        """
        document.getElementById('dynamic-target').addEventListener('click', () => labEmit('dynamic:raw'));
        document.getElementById('dynamic-neighbour').addEventListener('click', () => {
          document.getElementById('dynamic-status').textContent = 'ok';
          labEmit('dynamic:neighbour-click');
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
            main { display: grid; gap: 0.75rem; max-width: 40rem; }
            label { display: grid; gap: 0.25rem; }
            input, button { font: inherit; padding: 0.5rem; }
            table { border-collapse: collapse; }
            td { border: 1px solid #aaa; padding: 0.4rem; }
            iframe { width: 100%; min-height: 8rem; }
            #lab-event-log { margin-top: 2rem; white-space: pre-wrap; }
          </style>
        </head>
        <body>
          {{body}}
          <pre id="lab-event-log">[]</pre>
          <script>
            window.__migratorLab = { events: [] };
            window.labSnapshot = function() {
              const dom = {};
              document.querySelectorAll('[id]').forEach(element => {
                const style = window.getComputedStyle(element);
                dom[element.id] = {
                  text: element.textContent || '',
                  value: 'value' in element ? String(element.value || '') : '',
                  visible: !element.hidden && style.display !== 'none' && style.visibility !== 'hidden',
                  enabled: !('disabled' in element) || !element.disabled,
                  checked: 'checked' in element && Boolean(element.checked)
                };
              });
              return dom;
            };
            window.labEmit = function(name) {
              window.__migratorLab.events.push(name);
              document.getElementById('lab-event-log').textContent = JSON.stringify(window.__migratorLab.events);
              const payload = JSON.stringify({ event: name, path: window.location.pathname, dom: window.labSnapshot() });
              if (navigator.sendBeacon) {
                navigator.sendBeacon('/__lab/events', payload);
              } else {
                fetch('/__lab/events', { method: 'POST', body: payload, keepalive: true }).catch(() => {});
              }
            };
            {{pageScript}}
          </script>
        </body>
        </html>
        """;
}
