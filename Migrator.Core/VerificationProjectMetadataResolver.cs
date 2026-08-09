using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Migrator.Core;

/// <summary>
/// Best-effort metadata reader used by verify-project when it builds an isolated
/// temporary harness. It deliberately does not try to become a full MSBuild evaluator.
/// </summary>
public static class VerificationProjectMetadataResolver
{
    static readonly Regex PropertyReference = new(@"\$\(([^)]+)\)", RegexOptions.Compiled);

    public static string ResolveTargetFramework(
        string? configuredTargetFramework,
        string? preferredProject,
        IEnumerable<string> projectReferences,
        string fallback = "net10.0")
    {
        if (!string.IsNullOrWhiteSpace(configuredTargetFramework))
            return configuredTargetFramework.Trim();

        if (!string.IsNullOrWhiteSpace(preferredProject) && File.Exists(preferredProject))
        {
            var preferred = ReadTargetFramework(preferredProject);
            if (!string.IsNullOrWhiteSpace(preferred))
                return preferred!;
        }

        foreach (var project in projectReferences.Where(File.Exists))
        {
            var targetFramework = ReadTargetFramework(project);
            if (!string.IsNullOrWhiteSpace(targetFramework))
                return targetFramework!;
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
        var centralVersions = ReadCentralPackageVersions(buildFiles);

        foreach (var project in projectReferences.Where(File.Exists))
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
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in buildFiles.Where(x => Path.GetFileName(x).Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase) && File.Exists(x)))
        {
            try
            {
                var doc = XDocument.Load(file);
                var properties = ReadProperties(doc);
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
                        result[include!] = resolvedVersion;
                }
            }
            catch
            {
                // Best effort only. Explicit PackageReference versions still work.
            }
        }

        return result;
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
