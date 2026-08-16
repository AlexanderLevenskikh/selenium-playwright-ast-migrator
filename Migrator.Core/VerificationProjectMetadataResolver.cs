using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Migrator.Core;

/// <summary>
/// Best-effort metadata reader used by verify-project when it builds an isolated
/// temporary harness. It deliberately does not try to become a full MSBuild evaluator.
/// When metadata is ambiguous, it fails explicitly instead of turning enumeration order
/// into verification semantics.
/// </summary>
public static class VerificationProjectMetadataResolver
{
    static readonly Regex PropertyReference = new(@"\$\(([^)]+)\)", RegexOptions.Compiled);

    static StringComparer FilePathComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static string ResolveTargetFramework(
        string? configuredTargetFramework,
        string? preferredProject,
        IEnumerable<string> projectReferences,
        string fallback = "net10.0")
    {
        ArgumentNullException.ThrowIfNull(projectReferences);

        if (!string.IsNullOrWhiteSpace(configuredTargetFramework))
            return configuredTargetFramework.Trim();

        if (!string.IsNullOrWhiteSpace(preferredProject) && File.Exists(preferredProject))
        {
            var preferred = ReadTargetFramework(preferredProject);
            if (!string.IsNullOrWhiteSpace(preferred))
                return preferred!;
        }

        var candidates = CanonicalExistingPaths(projectReferences)
            .Select(project => (
                Project: project,
                Framework: ReadTargetFramework(project)))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Framework))
            .Select(candidate => (
                candidate.Project,
                Framework: candidate.Framework!))
            .ToArray();

        var frameworks = candidates
            .Select(candidate => candidate.Framework)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(framework => framework, StringComparer.Ordinal)
            .ToArray();

        if (frameworks.Length == 1)
            return frameworks[0];

        if (frameworks.Length > 1)
        {
            var evidence = string.Join(
                "; ",
                frameworks.Select(framework =>
                {
                    var projects = candidates
                        .Where(candidate => string.Equals(
                            candidate.Framework,
                            framework,
                            StringComparison.Ordinal))
                        .Select(candidate => Path.GetFileName(candidate.Project))
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToArray();

                    return $"{framework}=[{string.Join(",", projects)}]";
                }));

            throw new InvalidOperationException(
                "VERIFY_PROJECT_TARGET_FRAMEWORK_AMBIGUOUS: " +
                $"multiple target frameworks were discovered without an authoritative preferred project: {evidence}. " +
                "Configure Verification.TargetFramework or an explicit entry/preferred project.");
        }

        return fallback;
    }

    public static string? ReadTargetFramework(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            var targetFramework = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "TargetFramework")?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(targetFramework))
                return targetFramework;

            var targetFrameworks = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "TargetFrameworks")?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(targetFrameworks))
                return targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static IEnumerable<PackageReferenceConfig> ReadPackageReferences(
        IEnumerable<string> projectReferences,
        IEnumerable<string> buildFiles)
    {
        ArgumentNullException.ThrowIfNull(projectReferences);
        ArgumentNullException.ThrowIfNull(buildFiles);

        var centralVersions = ReadCentralPackageVersions(buildFiles);

        foreach (var project in CanonicalExistingPaths(projectReferences))
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(project);
            }
            catch
            {
                continue;
            }

            foreach (var packageReference in doc.Descendants().Where(x => x.Name.LocalName == "PackageReference"))
            {
                var include = ((string?)packageReference.Attribute("Include"))?.Trim()
                    ?? ((string?)packageReference.Attribute("Update"))?.Trim();
                if (string.IsNullOrWhiteSpace(include))
                    continue;

                var inlineVersion = ((string?)packageReference.Attribute("VersionOverride"))?.Trim()
                    ?? packageReference.Elements().FirstOrDefault(x => x.Name.LocalName == "VersionOverride")?.Value.Trim()
                    ?? ((string?)packageReference.Attribute("Version"))?.Trim()
                    ?? packageReference.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value.Trim();

                var version = !string.IsNullOrWhiteSpace(inlineVersion)
                    ? inlineVersion
                    : centralVersions.GetValueOrDefault(include!);

                if (!string.IsNullOrWhiteSpace(version))
                    yield return new PackageReferenceConfig { Include = include!, Version = version! };
            }
        }
    }

    public static IReadOnlyDictionary<string, string> ReadCentralPackageVersions(IEnumerable<string> buildFiles)
    {
        ArgumentNullException.ThrowIfNull(buildFiles);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in CanonicalExistingPaths(buildFiles)
                     .Where(path => Path.GetFileName(path).Equals(
                         "Directory.Packages.props",
                         StringComparison.OrdinalIgnoreCase)))
        {
            IReadOnlyList<(string Include, string Version)> fileVersions;
            try
            {
                var doc = XDocument.Load(file);
                var properties = ReadProperties(doc);
                var collected = new List<(string Include, string Version)>();

                foreach (var packageVersion in doc.Descendants().Where(x => x.Name.LocalName == "PackageVersion"))
                {
                    // Conditional PackageVersion items require real MSBuild evaluation. Do not guess.
                    if (packageVersion.Attribute("Condition") != null
                        || packageVersion.Ancestors().Any(x => x.Name.LocalName == "ItemGroup" && x.Attribute("Condition") != null))
                        continue;

                    var include = ((string?)packageVersion.Attribute("Include"))?.Trim()
                        ?? ((string?)packageVersion.Attribute("Update"))?.Trim();
                    var rawVersion = ((string?)packageVersion.Attribute("Version"))?.Trim()
                        ?? packageVersion.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value.Trim();
                    if (string.IsNullOrWhiteSpace(include) || string.IsNullOrWhiteSpace(rawVersion))
                        continue;

                    var resolvedVersion = ResolveProperties(rawVersion!, properties);
                    if (!string.IsNullOrWhiteSpace(resolvedVersion) && !PropertyReference.IsMatch(resolvedVersion))
                        collected.Add((include!, resolvedVersion));
                }

                fileVersions = collected;
            }
            catch
            {
                // Best effort only. Explicit PackageReference versions still work.
                continue;
            }

            foreach (var packageVersion in fileVersions)
            {
                if (result.TryGetValue(packageVersion.Include, out var existing)
                    && !string.Equals(existing, packageVersion.Version, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "VERIFY_PROJECT_CENTRAL_PACKAGE_VERSION_CONFLICT: " +
                        $"package '{packageVersion.Include}' resolves to both '{existing}' and '{packageVersion.Version}' " +
                        "across unconditional Directory.Packages.props entries. " +
                        "Use an explicit verification PackageReference/VersionOverride or real MSBuild evaluation.");
                }

                result[packageVersion.Include] = packageVersion.Version;
            }
        }

        return result;
    }

    static IReadOnlyList<string> CanonicalExistingPaths(IEnumerable<string> paths)
    {
        var normalized = new List<string>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                    normalized.Add(fullPath);
            }
            catch
            {
                // Invalid paths are ignored by this best-effort metadata reader.
            }
        }

        return normalized
            .Distinct(FilePathComparer)
            .OrderBy(path => path, FilePathComparer)
            .ToArray();
    }

    static Dictionary<string, string> ReadProperties(XDocument doc)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertyGroup in doc.Descendants().Where(x => x.Name.LocalName == "PropertyGroup" && x.Attribute("Condition") == null))
        {
            foreach (var property in propertyGroup.Elements().Where(x => x.Attribute("Condition") == null))
            {
                var value = property.Value.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    properties[property.Name.LocalName] = value;
            }
        }
        return properties;
    }

    static string ResolveProperties(string value, IReadOnlyDictionary<string, string> properties)
    {
        var current = value.Trim();
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var changed = false;
            var next = PropertyReference.Replace(current, match =>
            {
                var name = match.Groups[1].Value;
                if (!properties.TryGetValue(name, out var replacement))
                    return match.Value;
                changed = true;
                return replacement;
            });

            current = next;
            if (!changed)
                break;
        }
        return current;
    }
}
