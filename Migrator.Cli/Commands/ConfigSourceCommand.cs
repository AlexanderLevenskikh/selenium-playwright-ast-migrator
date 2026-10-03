using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Migrator.Core;

internal static class ConfigSourceCommand
{
    public static int Run(string inputPath, string outPath, string format, string[] configPaths)
    {
        Directory.CreateDirectory(outPath);

        if (configPaths.Length == 0)
        {
            Console.Error.WriteLine("config-source requires at least one --config adapter-config.json layer");
            return 2;
        }

        var files = ProfileMatchCommand.CollectProfileInputFiles(inputPath);
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"No C# source files were found under '{inputPath}'. Check --input before running config-source.");
            return 1;
        }

        var layers = new System.Collections.Generic.List<(string Path, ProjectAdapterConfig Config)>();
        foreach (var path in configPaths)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Config not found: {path}");
                return 2;
            }

            try
            {
                layers.Add((path, ConfigValidator.ValidateJson(File.ReadAllText(path), path)));
            }
            catch (ConfigValidationError cvex)
            {
                Console.Error.WriteLine($"Config error in {path}:");
                foreach (var err in cvex.Errors)
                    Console.Error.WriteLine(err);
                return 2;
            }
        }

        var merged = ProjectAdapterConfigMerger.Merge(layers.Select(l => l.Config));
        var sourceFiles = files.Select(f => (Path: f.Path, Text: f.Text)).ToArray();
        var report = ConfigSourceReportBuilder.Build(inputPath, merged, sourceFiles);

        WriteJson(report, Path.Combine(outPath, "config-source.json"));
        if (format is "markdown" or "both" or "md")
            WriteMarkdown(report, Path.Combine(outPath, "config-source.md"));

        Console.WriteLine($"config-source: {report.Summary.TotalKeys} config keys checked against source, " +
                          $"{report.Summary.UsedKeys} used ({report.Summary.CoveragePercent}%), " +
                          $"{report.Summary.UnusedKeys} unused.");
        return 0;
    }

    static void WriteJson(ConfigSourceReport report, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    static void WriteMarkdown(ConfigSourceReport report, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Config-to-source coverage report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");
        sb.AppendLine($"Input: `{report.InputPath}`");
        sb.AppendLine();
        sb.AppendLine("Every source-side key in the adapter config was located in the actual Selenium source. A key with **0 occurrences** cannot affect this project — either it is mistyped against the real code or it is dead for this project. The config system does not fail on such keys, so this report is the only place the gap is visible.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Total config keys: **{report.Summary.TotalKeys}**");
        sb.AppendLine($"- Keys with source occurrences: **{report.Summary.UsedKeys}**");
        sb.AppendLine($"- Keys with no source occurrence (unused/mistyped): **{report.Summary.UnusedKeys}**");
        sb.AppendLine($"- Coverage: **{report.Summary.CoveragePercent}%**");
        sb.AppendLine();
        sb.AppendLine("## Per-key usage");
        sb.AppendLine();
        sb.AppendLine("| Section | Key | Used | Occurrences | Example (file:line) |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var usage in report.Usages.OrderBy(u => u.Section, StringComparer.Ordinal).ThenBy(u => u.Key, StringComparer.Ordinal))
        {
            var example = usage.ExampleFile == null ? "—" : $"{Path.GetFileName(usage.ExampleFile)}:{usage.ExampleLine}";
            sb.AppendLine($"| {Escape(usage.Section)} | `{Escape(usage.Key)}` | {(usage.Used ? "yes" : "**no**")} | {usage.Occurrences} | {example} |");
        }
        File.WriteAllText(path, sb.ToString());
    }

    static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
