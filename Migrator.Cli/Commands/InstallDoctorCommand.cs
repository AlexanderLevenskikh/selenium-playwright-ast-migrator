using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class InstallDoctorCommand
{
    const string ToolCommand = "selenium-pw-migrator";
    const string DotNetPackageId = "SeleniumPlaywrightMigrator";
    const string NpmPackageName = "selenium-pw-migrator";

    public static int RunInstallDoctor(string outPath, string format)
    {
        var report = CreateInstallReport();
        Directory.CreateDirectory(outPath);

        if (format == "text" || format == "both")
            File.WriteAllText(Path.Combine(outPath, "install-doctor-report.md"), BuildMarkdown(report), new UTF8Encoding(false));
        if (format == "json" || format == "both")
            File.WriteAllText(Path.Combine(outPath, "install-doctor-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));

        PrintConsoleSummary(report, outPath);
        return 0;
    }

    public static InstallDoctorReport CreateInstallReport()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = !string.IsNullOrWhiteSpace(informationalVersion)
            ? informationalVersion
            : assembly.GetName().Version?.ToString() ?? "unknown";
        var executable = Environment.ProcessPath ?? assembly.Location;
        var baseDirectory = AppContext.BaseDirectory;
        var manifest = ReadStandaloneVersionManifest(baseDirectory);
        var packageRoot = FindNpmPackageRoot(baseDirectory) ?? FindNpmPackageRoot(Path.GetDirectoryName(executable) ?? baseDirectory);
        var commandCandidates = FindCommandCandidates(ToolCommand).ToArray();
        var distribution = FirstNonEmpty(
            Environment.GetEnvironmentVariable("MIGRATOR_DISTRIBUTION"),
            GetAssemblyMetadata(assembly, "Distribution"),
            manifest is null ? null : "standalone");
        var channel = DetectChannel(executable, baseDirectory, distribution, manifest, packageRoot, commandCandidates);
        var updateCommand = RecommendedUpdateCommand(channel, packageRoot);
        var installCommand = RecommendedInstallCommand(channel);
        var notes = BuildNotes(channel, executable, commandCandidates).ToArray();

        return new InstallDoctorReport(
            SchemaVersion: "install-doctor/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            CommandName: ToolCommand,
            Version: version,
            Channel: channel,
            Distribution: distribution ?? "unknown",
            ExecutablePath: executable,
            BaseDirectory: baseDirectory,
            RuntimeIdentifier: RuntimeInformation.RuntimeIdentifier,
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            NpmPackageRoot: packageRoot,
            PathCandidates: commandCandidates,
            RecommendedInstallCommand: installCommand,
            RecommendedUpdateCommand: updateCommand,
            Notes: notes);
    }

    public static string RecommendedUpdateCommandOnly()
    {
        return CreateInstallReport().RecommendedUpdateCommand;
    }

    static void PrintConsoleSummary(InstallDoctorReport report, string outPath)
    {
        Console.WriteLine("=== Install Doctor ===");
        Console.WriteLine($"Command:      {report.CommandName}");
        Console.WriteLine($"Version:      {report.Version}");
        Console.WriteLine($"Channel:      {report.Channel}");
        Console.WriteLine($"Distribution: {report.Distribution}");
        Console.WriteLine($"Executable:   {report.ExecutablePath}");
        Console.WriteLine($"Base dir:     {report.BaseDirectory}");
        Console.WriteLine($"Runtime:      {report.RuntimeIdentifier}");
        Console.WriteLine($"Framework:    {report.FrameworkDescription}");
        if (!string.IsNullOrWhiteSpace(report.NpmPackageRoot))
            Console.WriteLine($"npm package:  {report.NpmPackageRoot}");
        Console.WriteLine();
        Console.WriteLine("PATH resolution:");
        if (report.PathCandidates.Length == 0)
        {
            Console.WriteLine($"  No {ToolCommand} executable was found on PATH.");
        }
        else
        {
            for (var i = 0; i < report.PathCandidates.Length; i++)
                Console.WriteLine($"  {i + 1}. {report.PathCandidates[i]}");
        }
        Console.WriteLine();
        Console.WriteLine("Install/update:");
        Console.WriteLine($"  Install: {report.RecommendedInstallCommand}");
        Console.WriteLine($"  Update:  {report.RecommendedUpdateCommand}");
        if (report.Notes.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Notes:");
            foreach (var note in report.Notes)
                Console.WriteLine($"  - {note}");
        }
        Console.WriteLine();
        Console.WriteLine($"Report: {Path.GetFullPath(outPath)}");
    }

    static string BuildMarkdown(InstallDoctorReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Install Doctor Report");
        sb.AppendLine();
        sb.AppendLine($"Generated at: `{report.GeneratedAtUtc:O}`");
        sb.AppendLine();
        sb.AppendLine("## Current command");
        sb.AppendLine();
        sb.AppendLine($"- Command: `{report.CommandName}`");
        sb.AppendLine($"- Version: `{report.Version}`");
        sb.AppendLine($"- Channel: `{report.Channel}`");
        sb.AppendLine($"- Distribution: `{report.Distribution}`");
        sb.AppendLine($"- Executable: `{report.ExecutablePath}`");
        sb.AppendLine($"- Base directory: `{report.BaseDirectory}`");
        sb.AppendLine($"- Runtime: `{report.RuntimeIdentifier}`");
        sb.AppendLine($"- Framework: `{report.FrameworkDescription}`");
        if (!string.IsNullOrWhiteSpace(report.NpmPackageRoot))
            sb.AppendLine($"- npm package root: `{report.NpmPackageRoot}`");
        sb.AppendLine();
        sb.AppendLine("## PATH candidates");
        sb.AppendLine();
        if (report.PathCandidates.Length == 0)
        {
            sb.AppendLine($"No `{ToolCommand}` executable was found on `PATH`.");
        }
        else
        {
            for (var i = 0; i < report.PathCandidates.Length; i++)
                sb.AppendLine($"{i + 1}. `{report.PathCandidates[i]}`");
        }
        sb.AppendLine();
        sb.AppendLine("## Install/update");
        sb.AppendLine();
        sb.AppendLine($"Install command: `{report.RecommendedInstallCommand}`");
        sb.AppendLine($"Update command: `{report.RecommendedUpdateCommand}`");
        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        if (report.Notes.Length == 0)
            sb.AppendLine("No warnings detected.");
        else
            foreach (var note in report.Notes)
                sb.AppendLine($"- {note}");
        return sb.ToString();
    }

    static IEnumerable<string> BuildNotes(string channel, string executable, string[] commandCandidates)
    {
        if (commandCandidates.Length > 1)
            yield return $"Multiple {ToolCommand} entries are on PATH. The first shell match may shadow another install.";
        if (channel.Contains("dotnet", StringComparison.OrdinalIgnoreCase) && commandCandidates.Any(p => p.Contains("node", StringComparison.OrdinalIgnoreCase) || p.Contains("npm", StringComparison.OrdinalIgnoreCase)))
            yield return "A npm shim also appears on PATH. Use PATH order or uninstall the older channel if commands look inconsistent.";
        if (channel == "unknown")
            yield return "Channel could not be inferred from the current executable. Use the explicit install/update commands below.";
        if (executable.Contains("Debug", StringComparison.OrdinalIgnoreCase) || executable.Contains("Release", StringComparison.OrdinalIgnoreCase))
            yield return "This looks like a source/build output, not a packaged install.";
    }

    static string DetectChannel(string executable, string baseDirectory, string? distribution, StandaloneManifest? manifest, string? npmPackageRoot, string[] commandCandidates)
    {
        var combined = string.Join("|", new[] { executable, baseDirectory, distribution ?? "", npmPackageRoot ?? "" }.Concat(commandCandidates));
        if (!string.IsNullOrWhiteSpace(npmPackageRoot))
            return "npm";
        if (manifest is not null || combined.Contains(".selenium-pw-migrator", StringComparison.OrdinalIgnoreCase))
            return "standalone";
        if (combined.Contains(".dotnet", StringComparison.OrdinalIgnoreCase) || combined.Contains(".store", StringComparison.OrdinalIgnoreCase) || (distribution?.Contains("dotnet-tool", StringComparison.OrdinalIgnoreCase) ?? false))
            return "dotnet-tool";
        if (combined.Contains("Migrator.Cli", StringComparison.OrdinalIgnoreCase) && (combined.Contains("bin/", StringComparison.OrdinalIgnoreCase) || combined.Contains("bin\\", StringComparison.OrdinalIgnoreCase)))
            return "source";
        return "unknown";
    }

    static string RecommendedInstallCommand(string channel) => channel switch
    {
        "npm" => "npm install -g selenium-pw-migrator@preview",
        "standalone" => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "iwr https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1 -OutFile $env:TEMP\\install-standalone.ps1; & $env:TEMP\\install-standalone.ps1"
            : "curl -fsSL https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.sh | bash",
        "dotnet-tool" => "dotnet tool install --global SeleniumPlaywrightMigrator --source https://api.nuget.org/v3/index.json --prerelease",
        "source" => "dotnet restore && dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --help",
        _ => "npm install -g selenium-pw-migrator@preview"
    };

    static string RecommendedUpdateCommand(string channel, string? npmPackageRoot) => channel switch
    {
        "npm" => "npm update -g selenium-pw-migrator",
        "standalone" => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "iwr https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1 -OutFile $env:TEMP\\install-standalone.ps1; & $env:TEMP\\install-standalone.ps1"
            : "curl -fsSL https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.sh | bash",
        "dotnet-tool" => "dotnet tool update --global SeleniumPlaywrightMigrator --source https://api.nuget.org/v3/index.json --prerelease",
        "source" => "git pull && dotnet restore && dotnet build",
        _ => "npm update -g selenium-pw-migrator"
    };

    static IEnumerable<string> FindCommandCandidates(string commandName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathExt = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { string.Empty };
        var names = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? pathExt.Select(ext => commandName + ext.ToLowerInvariant()).Concat(new[] { commandName }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : new[] { commandName };
        var seen = new HashSet<string>(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate) && seen.Add(Path.GetFullPath(candidate)))
                    yield return Path.GetFullPath(candidate);
            }
        }
    }

    static string? FindNpmPackageRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            var packageJson = Path.Combine(dir.FullName, "package.json");
            if (File.Exists(packageJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(packageJson));
                    if (doc.RootElement.TryGetProperty("name", out var name) && name.GetString() == NpmPackageName)
                        return dir.FullName;
                }
                catch
                {
                    // Keep searching parent directories.
                }
            }
            dir = dir.Parent;
        }
        return null;
    }

    static StandaloneManifest? ReadStandaloneVersionManifest(string baseDirectory)
    {
        var manifestPath = Path.Combine(baseDirectory, "standalone-manifest.json");
        if (!File.Exists(manifestPath))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            return new StandaloneManifest(
                Runtime: root.TryGetProperty("runtime", out var runtime) ? runtime.GetString() : null,
                GeneratedAtUtc: root.TryGetProperty("generatedAtUtc", out var generated) ? generated.GetString() : null);
        }
        catch
        {
            return null;
        }
    }

    static string? GetAssemblyMetadata(Assembly assembly, string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase))
        ?.Value;

    static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    sealed record StandaloneManifest(string? Runtime, string? GeneratedAtUtc);
}

internal sealed record InstallDoctorReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string CommandName,
    string Version,
    string Channel,
    string Distribution,
    string ExecutablePath,
    string BaseDirectory,
    string RuntimeIdentifier,
    string FrameworkDescription,
    string? NpmPackageRoot,
    string[] PathCandidates,
    string RecommendedInstallCommand,
    string RecommendedUpdateCommand,
    string[] Notes);
