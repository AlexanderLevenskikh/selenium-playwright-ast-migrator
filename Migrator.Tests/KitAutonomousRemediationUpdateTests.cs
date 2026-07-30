using Xunit;

namespace Migrator.Tests;

public sealed class KitAutonomousRemediationUpdateTests
{
    [Fact]
    [Trait("Layer", "Scenario")]
    public void KitUpdate_UpgradesStockSingleRepairContractWithoutOverwritingCustomTicketContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-remediation-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "LegacyTests"));
        File.WriteAllText(Path.Combine(root, "LegacyTests", "Sample.cs"), "public class Sample {}\n");

        try
        {
            var init = CliTestRunner.Run("kit init --workspace migration --source ./LegacyTests", root, TimeSpan.FromMinutes(2));
            Assert.False(init.TimedOut, init.StdErr);
            Assert.Equal(0, init.ExitCode);

            var ticketPath = Path.Combine(root, "migration", "current-ticket.md");
            File.WriteAllText(
                ticketPath,
                "# Custom project note\n\n" +
                "Complete at most one bounded, source-backed repair for this ticket, rerun the complete configured source scope, and then stop with evidence. " +
                "Stop earlier only when the repair is unsafe, required input/tooling is missing, or the repeated full run shows no progress.\n");


            var rootAgentsPath = Path.Combine(root, "AGENTS.md");
            File.WriteAllText(
                rootAgentsPath,
                "# Custom root rule\n\n" +
                "6. Fix one highest-payoff root cause at a time and rerun the complete standard flow.\n" +
                "7. Do not stop after routine POM/config analysis to ask whether to continue. When one safe, agent-executable remediation is available under `migration/**`, perform it in the same invocation, rerun the complete standard flow, and then report the result. Ask only for a human product decision or explicit authorization to write outside the migration workspace.\n");

            var handoffPath = Path.Combine(root, "migration", "state", "handoff.md");
            File.WriteAllText(
                handoffPath,
                "# Custom handoff note\n\n" +
                "- Do not continue indefinitely: apply at most one bounded repair before a complete rerun and handoff.\n" +
                "- Do not hand off a routine agent-executable repair as an opt-in question; complete the allowed bounded repair first.\n");

            var safetyPath = Path.Combine(root, "migration", "state", "safety-checklist.md");
            File.WriteAllText(
                safetyPath,
                "# Custom safety note\n\n" +
                "- [ ] The agent stopped after the full run or after one bounded repair plus a complete rerun.\n");

            var policyPath = Path.Combine(root, "migration", "state", "harness-policy.json");
            File.WriteAllText(policyPath, "{\n  \"maxRepairPassesPerRun\": 1\n}\n");

            var update = CliTestRunner.Run("kit update --workspace migration --source ./LegacyTests --backup", root, TimeSpan.FromMinutes(2));
            Assert.False(update.TimedOut, update.StdErr);
            Assert.Equal(0, update.ExitCode);

            var ticket = File.ReadAllText(ticketPath);
            Assert.Contains("# Custom project note", ticket);
            Assert.Contains("five-cycle invocation budget", ticket, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Complete at most one bounded", ticket, StringComparison.OrdinalIgnoreCase);


            var rootAgents = File.ReadAllText(rootAgentsPath);
            Assert.Contains("# Custom root rule", rootAgents);
            Assert.Contains("up to five cycles", rootAgents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Fix one highest-payoff root cause at a time", rootAgents, StringComparison.OrdinalIgnoreCase);

            var handoff = File.ReadAllText(handoffPath);
            Assert.Contains("# Custom handoff note", handoff);
            Assert.Contains("AUTONOMOUS_CYCLE_BUDGET_REACHED", handoff);
            Assert.DoesNotContain("apply at most one bounded repair", handoff, StringComparison.OrdinalIgnoreCase);

            var safety = File.ReadAllText(safetyPath);
            Assert.Contains("# Custom safety note", safety);
            Assert.Contains("five-cycle budget", safety, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("after one bounded repair plus", safety, StringComparison.OrdinalIgnoreCase);

            var contract = File.ReadAllText(Path.Combine(root, "migration", "AGENT_CONTRACT.md"));
            Assert.Contains("up to five cycles", contract, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No further automated migration work remains", contract, StringComparison.Ordinal);

            var policy = File.ReadAllText(policyPath);
            Assert.Contains("\"maxRepairPassesPerRun\": 5", policy);
            Assert.Contains("\"maxAutonomousRemediationCyclesPerInvocation\": 5", policy);
            Assert.Contains("AUTONOMOUS_CYCLE_BUDGET_REACHED", policy);
            Assert.Contains("kit-overwrite:", update.StdOut, StringComparison.Ordinal);
            Assert.Contains("upgrade-standard-mode-state:", update.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
