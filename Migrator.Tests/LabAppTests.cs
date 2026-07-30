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
        var expected = new[] { "/login", "/list", "/helper", "/wait", "/smoke", "/unsupported" };
        Assert.Equal(expected, LabAppPageCatalog.PageRoutes);

        foreach (var route in expected)
        {
            var response = LabAppPageCatalog.Resolve(route);
            var html = System.Text.Encoding.UTF8.GetString(response.Body);
            Assert.Equal(200, response.StatusCode);
            Assert.Contains("id=\"lab-event-log\"", html);
            Assert.Contains("window.__migratorLab", html);
        }
    }

    [Fact]
    public void Pages_ContainTheSelectorsUsedBySourceFixtures()
    {
        var requiredIds = new Dictionary<string, string[]>
        {
            ["/login"] = new[] { "username", "password", "login", "result" },
            ["/list"] = new[] { "items" },
            ["/helper"] = new[] { "helper-button", "helper-status" },
            ["/wait"] = new[] { "wait-button", "wait-status" },
            ["/smoke"] = new[] { "smoke-button", "smoke-status" },
            ["/unsupported"] = new[] { "unsupported-button", "unsupported-status", "script-target" }
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

        Assert.Contains("migrator-lab-app/v0", health);
        Assert.Contains("setTimeout", wait);
        Assert.Contains("250", wait);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
