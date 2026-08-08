using System.Net;
using Migrator.Lab.LabApp;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Scenario")]
public sealed class LabAppTests
{
    [Fact]
    public void PageCatalog_ContainsAllVerticalSliceRoutesAndClientEventLog()
    {
        var expected = new[]
        {
            "/login", "/edit", "/list", "/table", "/form", "/locator", "/helper", "/pom", "/modal",
            "/async", "/setup", "/wait", "/wait-negative", "/custom-wait", "/dialog-close", "/control-flow", "/parameterized",
            "/smoke", "/unsupported", "/actions", "/complex", "/dynamic"
        };
        Assert.Equal(expected, LabAppPageCatalog.PageRoutes);

        foreach (var route in expected)
        {
            var response = LabAppPageCatalog.Resolve(route);
            var html = System.Text.Encoding.UTF8.GetString(response.Body);
            Assert.Equal(200, response.StatusCode);
            Assert.Contains("id=\"lab-event-log\"", html);
            Assert.Contains("window.__migratorLab", html);
            Assert.Contains("/__lab/events", html);
            Assert.Contains("labSnapshot", html);
            Assert.Contains("sequence: window.__migratorLab.events.length", html);
            Assert.Contains("observedAtUtc: new Date().toISOString()", html);
        }
    }

    [Fact]
    public void Pages_ContainTheSelectorsUsedBySourceFixtures()
    {
        var requiredIds = new Dictionary<string, string[]>
        {
            ["/login"] = new[] { "username", "password", "login", "result" },
            ["/edit"] = new[] { "edit-name", "edit-save", "edit-status" },
            ["/list"] = new[] { "items" },
            ["/table"] = new[] { "data" },
            ["/form"] = new[] { "terms", "blocked", "form-status" },
            ["/locator"] = new[] { "locator-primary", "locator-secondary", "locator-status" },
            ["/helper"] = new[] { "helper-button", "helper-status" },
            ["/pom"] = new[] { "pom-user", "pom-password", "pom-login", "dashboard-status" },
            ["/modal"] = new[] { "modal-open", "modal-save", "modal-status" },
            ["/async"] = new[] { "async-button", "async-status" },
            ["/setup"] = new[] { "setup-prepare", "setup-test", "setup-status" },
            ["/wait"] = new[] { "wait-button", "wait-status" },
            ["/wait-negative"] = new[] { "negative-spinner", "negative-save", "negative-status" },
            ["/custom-wait"] = new[] { "custom-save", "custom-status" },
            ["/dialog-close"] = new[] { "confirm-dialog", "confirm-close", "dialog-final-save", "dialog-status" },
            ["/control-flow"] = new[] { "control-status" },
            ["/parameterized"] = new[] { "parameter-one", "parameter-two", "parameter-status" },
            ["/smoke"] = new[] { "smoke-button", "smoke-status" },
            ["/unsupported"] = new[] { "unsupported-button", "unsupported-status", "script-target" },
            ["/actions"] = new[] { "actions-target", "actions-neighbour", "actions-status" },
            ["/complex"] = new[] { "lab-frame", "popup-link", "upload-input", "download-link", "complex-neighbour", "complex-status" },
            ["/dynamic"] = new[] { "dynamic-neighbour", "dynamic-status" }
        };

        foreach (var (route, ids) in requiredIds)
        {
            var html = System.Text.Encoding.UTF8.GetString(LabAppPageCatalog.Resolve(route).Body);
            foreach (var id in ids)
                Assert.Contains($"id=\"{id}\"", html);
        }
    }

    [Fact]
    public async Task Host_ServesHealthAndDeterministicPagesOnAnEphemeralPort()
    {
        await using var host = await LabAppHost.StartAsync();
        using var client = new HttpClient { BaseAddress = host.BaseUri };

        var health = await client.GetStringAsync("health");
        var wait = await client.GetStringAsync("wait");
        var missing = await client.GetAsync("missing");

        Assert.Contains("migrator-lab-app/v1", health);
        Assert.Contains("setTimeout", wait);
        Assert.Contains("250", wait);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Host_StoresPostedBusinessEventsAndDomSnapshots()
    {
        await using var host = await LabAppHost.StartAsync();
        using var client = new HttpClient { BaseAddress = host.BaseUri };
        var payload = """
        {"event":"auth:success","path":"/login","dom":{"result":{"text":"ok","value":"","visible":true,"enabled":true,"checked":false}}}
        """;

        using var response = await client.PostAsync("__lab/events", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var observation = Assert.Single(host.SnapshotObservations());
        Assert.Equal("auth:success", observation.Event);
        Assert.Equal("ok", observation.Dom["result"].Text);
        Assert.True(observation.Dom["result"].Visible);
        host.ResetObservations();
        Assert.Empty(host.SnapshotObservations());
    }


    [Fact]
    public async Task Host_OrdersBusinessEventsByBrowserSequenceWhenBeaconRequestsArriveOutOfOrder()
    {
        await using var host = await LabAppHost.StartAsync();
        using var client = new HttpClient { BaseAddress = host.BaseUri };

        var successPayload = """
        {"sequence":2,"observedAtUtc":"2026-08-07T17:20:00.002Z","event":"auth:success","path":"/login","dom":{"result":{"text":"ok","value":"","visible":true,"enabled":true,"checked":false},"lab-event-log":{"text":"[\"auth:attempt\",\"auth:success\"]","value":"","visible":true,"enabled":true,"checked":false}}}
        """;
        var attemptPayload = """
        {"sequence":1,"observedAtUtc":"2026-08-07T17:20:00.001Z","event":"auth:attempt","path":"/login","dom":{"result":{"text":"","value":"","visible":false,"enabled":true,"checked":false},"lab-event-log":{"text":"[\"auth:attempt\"]","value":"","visible":true,"enabled":true,"checked":false}}}
        """;

        using var successResponse = await client.PostAsync(
            "__lab/events",
            new StringContent(successPayload, System.Text.Encoding.UTF8, "application/json"));
        using var attemptResponse = await client.PostAsync(
            "__lab/events",
            new StringContent(attemptPayload, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, successResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, attemptResponse.StatusCode);

        var observations = host.SnapshotObservations();
        Assert.Equal(new[] { "auth:attempt", "auth:success" }, observations.Select(item => item.Event).ToArray());
        Assert.Equal(new long[] { 1, 2 }, observations.Select(item => item.Sequence).ToArray());
        Assert.True(observations[0].ObservedAtUtc < observations[1].ObservedAtUtc);
        Assert.False(observations[0].Dom["result"].Visible);
        Assert.True(observations[1].Dom["result"].Visible);
    }

    [Fact]
    public void Catalog_ServesFramePopupAndDownloadInfrastructureForNightlyScenarios()
    {
        Assert.Equal(200, LabAppPageCatalog.Resolve("/frame-content").StatusCode);
        Assert.Equal(200, LabAppPageCatalog.Resolve("/popup-content").StatusCode);
        var download = LabAppPageCatalog.Resolve("/download/sample.txt");
        Assert.Equal(200, download.StatusCode);
        Assert.Equal("application/octet-stream", download.ContentType);
        Assert.Contains("migrator-lab-download", System.Text.Encoding.UTF8.GetString(download.Body));
    }

}
