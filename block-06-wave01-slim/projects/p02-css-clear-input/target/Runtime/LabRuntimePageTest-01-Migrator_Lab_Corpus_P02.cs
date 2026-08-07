using Microsoft.Playwright;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Migrator.Lab.Corpus.P02;

public abstract class PageTest : Microsoft.Playwright.NUnit.PageTest
{
    bool labTracingStarted;

    [SetUp]
    public async Task MigratorLabNavigateAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("MIGRATOR_LAB_APP_URL")
            ?? throw new InvalidOperationException("MIGRATOR_LAB_APP_URL is not set.");
        var route = Environment.GetEnvironmentVariable("MIGRATOR_LAB_TARGET_ROUTE") ?? "/";

        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });
        labTracingStarted = true;
        await Page.GotoAsync(new Uri(new Uri(baseUrl, UriKind.Absolute), route).AbsoluteUri);
    }

    [TearDown]
    public async Task MigratorLabCaptureFailureAsync()
    {
        if (!labTracingStarted)
            return;

        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        if (!failed)
        {
            await Context.Tracing.StopAsync();
            return;
        }

        var root = Environment.GetEnvironmentVariable("MIGRATOR_LAB_RUNTIME_ARTIFACTS")
            ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "migrator-lab-runtime-artifacts");
        Directory.CreateDirectory(root);
        var testName = SanitizeFileName(TestContext.CurrentContext.Test.Name);

        try
        {
            await Page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(root, testName + ".png"),
                FullPage = true
            });
        }
        finally
        {
            await Context.Tracing.StopAsync(new TracingStopOptions
            {
                Path = Path.Combine(root, testName + ".zip")
            });
        }
    }

    static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
}