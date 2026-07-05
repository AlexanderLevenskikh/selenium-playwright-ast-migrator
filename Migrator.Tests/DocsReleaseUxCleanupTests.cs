using Xunit;

namespace Migrator.Tests;

public class DocsReleaseUxCleanupTests
{
    [Fact]
    public void QuickStartAndAgentDocs_PromoteStartPilotAndExplicitAgentHandoff()
    {
        var quickStart = File.ReadAllText(FindRepositoryFile("docs/quick-start.md"));
        var agentEnv = File.ReadAllText(FindRepositoryFile("docs/agent-environments.md"));
        var agentEnvRu = File.ReadAllText(FindRepositoryFile("docs/agent-environments.ru.md"));
        var workflow = File.ReadAllText(FindRepositoryFile("docs/user-guide/migration-workflow.md"));

        Assert.Contains("selenium-pw-migrator start --input", quickStart);
        Assert.Contains("selenium-pw-migrator pilot --input", quickStart);
        Assert.Contains("selected-input/", quickStart);
        Assert.Contains("Do not route non-OpenCode agents through `bootstrap-opencode --opencode-install ci` as the main path", quickStart);

        Assert.Contains("kit bootstrap-agent --agent codex", agentEnv);
        Assert.Contains("kit bootstrap-agent --agent generic", agentEnv);
        Assert.Contains("Legacy compatibility mode", agentEnv);
        Assert.Contains("prefer `kit bootstrap-agent --agent codex`", agentEnv);

        Assert.Contains("kit bootstrap-agent --agent codex", agentEnvRu);
        Assert.Contains("kit bootstrap-agent --agent generic", agentEnvRu);
        Assert.Contains("Legacy compatibility mode", agentEnvRu);
        Assert.Contains("Для новых non-OpenCode setup используйте", agentEnvRu);

        Assert.Contains("selected-input", workflow);
        Assert.Contains("The generated next commands must not run on the full suite", workflow);
    }

    [Fact]
    public void InitWizardDocs_MarkInitAsLegacyManualScaffoldAfterStart()
    {
        var initWizard = File.ReadAllText(FindRepositoryFile("docs/init-wizard.md"));

        Assert.Contains("`start` is the default product-repo onboarding command", initWizard);
        Assert.Contains("Use `init --wizard` only when you deliberately want the older manual scaffold/config wizard", initWizard);
        Assert.Contains("| Product repo onboarding | `start` |", initWizard);
        Assert.Contains("| Representative first slice | `pilot` |", initWizard);
        Assert.Contains("| Codex/generic/CI handoff | `kit bootstrap-agent` |", initWizard);
        Assert.Contains("The generated pilot next commands must use `selected-input/`, not the full Selenium suite", initWizard);
    }

    [Fact]
    public void UserGuidesAndReadmes_DoNotPresentCiInstallModeAsPrimaryNonOpenCodePath()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var readmeRu = File.ReadAllText(FindRepositoryFile("README.ru.md"));
        var userGuide = File.ReadAllText(FindRepositoryFile("USER_GUIDE.md"));
        var userGuideRu = File.ReadAllText(FindRepositoryFile("USER_GUIDE.ru.md"));

        Assert.Contains("kit bootstrap-agent --agent codex", readme);
        Assert.Contains("kit bootstrap-agent --agent generic", readme);
        Assert.Contains("Legacy compatibility; prefer bootstrap-agent", readme);
        Assert.DoesNotContain("Codex/CI/manual agents; no OpenCode config", readme);

        Assert.Contains("kit bootstrap-agent --agent codex", readmeRu);
        Assert.Contains("kit bootstrap-agent --agent generic", readmeRu);
        Assert.Contains("Legacy compatibility; для non-OpenCode агентов предпочитай bootstrap-agent", readmeRu);
        Assert.DoesNotContain("используй `--opencode-install ci`, чтобы поставить workspace без OpenCode config", readmeRu);

        Assert.Contains("bootstrap-opencode --opencode-install ci` remains supported as a legacy compatibility mode", userGuide);
        Assert.Contains("new non-OpenCode setups should use `bootstrap-agent`", userGuide);
        Assert.Contains("`bootstrap-opencode --opencode-install ci` остаётся legacy compatibility mode", userGuideRu);
        Assert.Contains("новые non-OpenCode setup должны использовать `bootstrap-agent`", userGuideRu);
    }

    [Fact]
    public void DashboardDocs_PointAtRunsLatestAndDashboardLatest()
    {
        var quickStart = File.ReadAllText(FindRepositoryFile("docs/quick-start.md"));
        var reportServe = File.ReadAllText(FindRepositoryFile("docs/report-serve-dashboard.md"));
        var userGuide = File.ReadAllText(FindRepositoryFile("USER_GUIDE.md"));
        var userGuideRu = File.ReadAllText(FindRepositoryFile("USER_GUIDE.ru.md"));

        Assert.Contains("report serve --input migration/runs/latest --static-only --out migration/dashboard/latest", quickStart);
        Assert.Contains("report serve --input migration/runs/latest --static-only --out migration/dashboard/latest", reportServe);
        Assert.Contains("report serve --input migration/runs/latest --static-only --out migration/dashboard/latest", userGuide);
        Assert.Contains("report serve --input migration/runs/latest --static-only --out migration/dashboard/latest", userGuideRu);
    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
