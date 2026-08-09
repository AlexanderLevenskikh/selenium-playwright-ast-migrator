namespace Migrator.Lab.Execution;

/// <summary>
/// Shared classifier for external-process failures caused by the environment
/// rather than by migrated source code. Keep the marker set deliberately narrow:
/// ordinary compiler/test failures must not be reclassified as infrastructure.
/// </summary>
public static class InfrastructureFailureClassifier
{
    static readonly string[] GeneralInfrastructureMarkers =
    {
        "no .net sdks were found",
        "the command could not be loaded",
        "a compatible installed .net sdk",
        "unable to load the service index",
        "nu1301",
        "cannot connect to proxy",
        "proxyerror",
        "name or service not known",
        "temporary failure in name resolution",
        "connection timed out",
        "network is unreachable",
        "no such host is known",
        "failed to start dotnet process"
    };

    public static bool IsInfrastructureFailure(int exitCode, string? standardOutput, string? standardError)
    {
        if (exitCode == 0)
            return false;

        return ContainsGeneralInfrastructureMarker(
            string.Concat(standardOutput ?? string.Empty, "\n", standardError ?? string.Empty));
    }

    public static bool ContainsGeneralInfrastructureMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return GeneralInfrastructureMarkers.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
