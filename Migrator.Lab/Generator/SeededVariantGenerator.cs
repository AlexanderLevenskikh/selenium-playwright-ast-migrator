using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Generator;

public sealed record SeededVariantGenerationOptions
{
    public string CorpusRoot { get; init; } = Path.Combine("corpus", "stable", "vertical-slice");
    public string BaseScenarioId { get; init; } = "p01-basic-id-login";
    public string OutputRoot { get; init; } = Path.Combine("artifacts", "lab", "generated");
    public int Seed { get; init; } = 73001;
    public int Count { get; init; } = 6;
    public bool Force { get; init; }
}

public sealed class SeededVariantGenerator
{
    public const string Family = "p30-basic-login-metamorphic";
    public const string GeneratorVersion = "pairwise-binary/v1";

    public static readonly string[] DimensionNames =
    {
        "local-name",
        "element-declaration",
        "namespace-shape",
        "file-layout",
        "by-reference"
    };

    static readonly int[][] PairwiseCore =
    {
        new[] { 0, 0, 0, 0, 0 },
        new[] { 0, 1, 1, 1, 1 },
        new[] { 1, 0, 0, 1, 1 },
        new[] { 1, 1, 1, 0, 0 },
        new[] { 0, 0, 1, 0, 1 },
        new[] { 0, 1, 0, 1, 0 }
    };

    public LabGenerationManifest Generate(SeededVariantGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Seed < 0)
            throw new ArgumentOutOfRangeException(nameof(options.Seed), "Seed must be non-negative.");
        if (options.Count is < 6 or > 32)
            throw new ArgumentOutOfRangeException(nameof(options.Count), "Pairwise generation requires count in range 6-32.");

        var catalog = ScenarioCatalog.Load(options.CorpusRoot);
        if (catalog.HasErrors)
            throw new InvalidOperationException("Base corpus is invalid; run `lab validate` before seeded generation.");

        var entry = catalog.Entries.SingleOrDefault(item =>
            string.Equals(item.Scenario?.Id, options.BaseScenarioId, StringComparison.OrdinalIgnoreCase));
        if (entry?.Scenario == null)
            throw new InvalidOperationException($"Base scenario '{options.BaseScenarioId}' was not found in {catalog.CorpusRoot}.");
        if (!entry.IsValid || entry.Scenario.Implementation.State != ScenarioImplementationState.Ready)
            throw new InvalidOperationException($"Base scenario '{entry.Scenario.Id}' must be VALID and READY.");
        if (entry.Scenario.Expected.Status != ScenarioStatus.Pass)
            throw new InvalidOperationException("Metamorphic generation currently requires a PASS base scenario.");
        if (entry.Scenario.Source.MigrationFiles.Length != 1)
            throw new InvalidOperationException("The block-07 parameterized template requires exactly one migration source file.");

        var outputRoot = Path.GetFullPath(options.OutputRoot);
        PrepareOutput(outputRoot, options.Force);

        var rows = BuildPairwiseRows(options.Seed, options.Count);
        var variants = new List<LabGeneratedVariant>(rows.Length);
        for (var index = 0; index < rows.Length; index++)
            variants.Add(GenerateVariant(entry, outputRoot, options.Seed, index, rows[index]));

        var manifest = new LabGenerationManifest
        {
            GeneratorVersion = GeneratorVersion,
            Family = Family,
            BaseScenarioId = entry.Scenario.Id,
            BaseContentHash = entry.Scenario.Implementation.ContentHash,
            Seed = options.Seed,
            Dimensions = DimensionNames.ToArray(),
            CorpusFingerprint = ComputeCorpusFingerprint(entry.Scenario.Implementation.ContentHash, options.Seed, variants),
            Environment = CaptureEnvironment(),
            Variants = variants.ToArray()
        };

        WriteManifest(outputRoot, manifest);
        return manifest;
    }

    public static int[][] BuildPairwiseRows(int seed, int count)
    {
        if (seed < 0)
            throw new ArgumentOutOfRangeException(nameof(seed));
        if (count is < 6 or > 32)
            throw new ArgumentOutOfRangeException(nameof(count));

        var mask = (int)(((uint)seed * 2654435761u) & 31u);
        var rotation = seed % PairwiseCore.Length;
        var selected = new List<int[]>(count);
        var seen = new HashSet<int>();

        for (var offset = 0; offset < PairwiseCore.Length; offset++)
        {
            var core = PairwiseCore[(offset + rotation) % PairwiseCore.Length];
            var row = new int[DimensionNames.Length];
            var encoded = 0;
            for (var dimension = 0; dimension < row.Length; dimension++)
            {
                var flip = (mask >> dimension) & 1;
                row[dimension] = core[dimension] ^ flip;
                encoded |= row[dimension] << dimension;
            }

            if (seen.Add(encoded))
                selected.Add(row);
        }

        if (selected.Count < count)
        {
            var remaining = Enumerable.Range(0, 32)
                .Where(value => !seen.Contains(value))
                .OrderBy(value => StableOrderKey(seed, value))
                .ThenBy(value => value)
                .ToArray();

            foreach (var encoded in remaining)
            {
                if (selected.Count >= count)
                    break;

                var row = new int[DimensionNames.Length];
                for (var dimension = 0; dimension < row.Length; dimension++)
                    row[dimension] = (encoded >> dimension) & 1;
                selected.Add(row);
            }
        }

        return selected.ToArray();
    }

    public static bool CoversEveryPair(IEnumerable<int[]> rows)
    {
        var materialized = rows.ToArray();
        for (var left = 0; left < DimensionNames.Length; left++)
        {
            for (var right = left + 1; right < DimensionNames.Length; right++)
            {
                for (var leftValue = 0; leftValue <= 1; leftValue++)
                {
                    for (var rightValue = 0; rightValue <= 1; rightValue++)
                    {
                        if (!materialized.Any(row => row[left] == leftValue && row[right] == rightValue))
                            return false;
                    }
                }
            }
        }

        return true;
    }

    static LabGeneratedVariant GenerateVariant(
        ScenarioCatalogEntry baseEntry,
        string outputRoot,
        int familySeed,
        int index,
        int[] row)
    {
        var baseScenario = baseEntry.Scenario!;
        var id = $"p30-s{familySeed.ToString("D5", CultureInfo.InvariantCulture)}-v{(index + 1).ToString("D2", CultureInfo.InvariantCulture)}";
        var variantSeed = (int)(((long)familySeed + index + 1) % int.MaxValue);
        var variantDirectoryName = id;
        var variantDirectory = Path.Combine(outputRoot, variantDirectoryName);
        Directory.CreateDirectory(variantDirectory);

        var baseMigrationPath = baseScenario.Source.MigrationFiles[0].Replace('\\', '/');
        var moved = row[3] == 1;
        var generatedMigrationPath = moved
            ? $"Specs/{Path.GetFileName(baseMigrationPath)}"
            : baseMigrationPath;

        foreach (var relativePath in baseScenario.Project.Files)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (string.Equals(normalized, baseMigrationPath, StringComparison.OrdinalIgnoreCase))
                continue;

            CopyDeclaredFile(baseEntry.ScenarioDirectory, variantDirectory, normalized);
        }

        var baseSourceFile = Path.Combine(
            baseEntry.ScenarioDirectory,
            baseMigrationPath.Replace('/', Path.DirectorySeparatorChar));
        var source = File.ReadAllText(baseSourceFile);
        source = ApplySourceTransformations(source, familySeed, row);
        WriteText(Path.Combine(variantDirectory, generatedMigrationPath.Replace('/', Path.DirectorySeparatorChar)), source);

        var projectFiles = baseScenario.Project.Files
            .Select(path => path.Replace('\\', '/'))
            .Where(path => !string.Equals(path, baseMigrationPath, StringComparison.OrdinalIgnoreCase))
            .Append(generatedMigrationPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var dimensions = DescribeDimensions(row);
        var features = baseScenario.Source.Features
            .Concat(dimensions.Select(pair => $"Metamorphic:{pair.Key}={pair.Value}"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var tags = baseScenario.Tags
            .Where(tag => !string.Equals(tag, "stable", StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(tag, "smoke", StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(tag, "pr", StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { "generated", "metamorphic", "p30", "block-07" })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        var generatedScenario = baseScenario with
        {
            Id = id,
            Seed = variantSeed,
            Tags = tags,
            Source = baseScenario.Source with
            {
                Template = "seeded-metamorphic/p30-basic-login",
                Features = features,
                MigrationFiles = new[] { generatedMigrationPath }
            },
            Project = baseScenario.Project with
            {
                Files = projectFiles,
                MsBuild = baseScenario.Project.MsBuild with
                {
                    FileScopedNamespace = row[2] == 0
                }
            },
            Implementation = baseScenario.Implementation with
            {
                Block = "07-seeded-generation",
                Notes = $"Generated metamorphic variant from {baseScenario.Id}; {FormatDimensions(dimensions)}.",
                ContentHash = ""
            }
        };

        var contentHash = ScenarioContentHasher.Compute(variantDirectory, projectFiles);
        generatedScenario = generatedScenario with
        {
            Implementation = generatedScenario.Implementation with { ContentHash = contentHash }
        };

        WriteText(
            Path.Combine(variantDirectory, "scenario.json"),
            JsonSerializer.Serialize(generatedScenario, LabJson.Options) + Environment.NewLine);

        return new LabGeneratedVariant
        {
            Index = index + 1,
            Id = id,
            Seed = variantSeed,
            Directory = variantDirectoryName,
            ContentHash = contentHash,
            ExpectedStatus = generatedScenario.Expected.Status,
            Dimensions = dimensions
        };
    }

    static string ApplySourceTransformations(string source, int seed, int[] row)
    {
        source = NormalizeNewlines(source);
        var localName = row[0] == 1 ? $"statusElement{(seed % 97).ToString("D2", CultureInfo.InvariantCulture)}" : "result";
        if (row[0] == 1)
        {
            source = source.Replace(
                "var result = WebDriver.FindElement(By.Id(\"result\"));",
                $"var {localName} = WebDriver.FindElement(By.Id(\"result\"));",
                StringComparison.Ordinal);
            source = source.Replace("result.Displayed", $"{localName}.Displayed", StringComparison.Ordinal);
            source = source.Replace("result.Text", $"{localName}.Text", StringComparison.Ordinal);
        }

        if (row[1] == 1)
        {
            source = source.Replace(
                $"var {localName} = WebDriver.FindElement(By.Id(\"result\"));",
                $"IWebElement {localName} = WebDriver.FindElement(By.Id(\"result\"));",
                StringComparison.Ordinal);
        }

        if (row[4] == 1)
        {
            if (!source.Contains("using SeleniumBy = OpenQA.Selenium.By;", StringComparison.Ordinal))
                source = source.Replace(
                    "using OpenQA.Selenium;\n",
                    "using OpenQA.Selenium;\nusing SeleniumBy = OpenQA.Selenium.By;\n",
                    StringComparison.Ordinal);
            source = source.Replace("By.Id(", "SeleniumBy.Id(", StringComparison.Ordinal);
        }

        if (row[2] == 1)
            source = ConvertFileScopedNamespaceToBlock(source);

        return source.EndsWith("\n", StringComparison.Ordinal) ? source : source + "\n";
    }

    static string ConvertFileScopedNamespaceToBlock(string source)
    {
        var match = Regex.Match(
            source,
            @"(?m)^namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*;\s*$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            throw new InvalidOperationException("The p30 template expected a file-scoped namespace in the migration source.");

        var before = source[..match.Index].TrimEnd('\n');
        var body = source[(match.Index + match.Length)..].Trim('\n');
        var indentedBody = string.Join(
            "\n",
            body.Split('\n').Select(line => line.Length == 0 ? "" : "    " + line));
        return $"{before}\n\nnamespace {match.Groups["name"].Value}\n{{\n{indentedBody}\n}}\n";
    }

    static Dictionary<string, string> DescribeDimensions(int[] row)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DimensionNames[0]] = row[0] == 0 ? "original" : "renamed",
            [DimensionNames[1]] = row[1] == 0 ? "var" : "explicit",
            [DimensionNames[2]] = row[2] == 0 ? "file-scoped" : "block",
            [DimensionNames[3]] = row[3] == 0 ? "tests" : "specs",
            [DimensionNames[4]] = row[4] == 0 ? "direct" : "alias"
        };
    }

    static string FormatDimensions(IReadOnlyDictionary<string, string> dimensions) =>
        string.Join(", ", dimensions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));

    static void CopyDeclaredFile(string sourceRoot, string destinationRoot, string relativePath)
    {
        var source = Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var destination = Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, NormalizeNewlines(text), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    static void PrepareOutput(string outputRoot, bool force)
    {
        if (Directory.Exists(outputRoot))
        {
            if (Directory.EnumerateFileSystemEntries(outputRoot).Any())
            {
                if (!force)
                    throw new InvalidOperationException($"Generation output is not empty: {outputRoot}. Use --force to replace it.");
                Directory.Delete(outputRoot, recursive: true);
            }
        }

        Directory.CreateDirectory(outputRoot);
    }

    static int StableOrderKey(int seed, int value)
    {
        unchecked
        {
            var hash = (uint)seed;
            hash ^= (uint)value + 0x9e3779b9u + (hash << 6) + (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7feb352du;
            hash ^= hash >> 15;
            return (int)(hash & 0x7fffffffu);
        }
    }

    static string ComputeCorpusFingerprint(string baseHash, int seed, IEnumerable<LabGeneratedVariant> variants)
    {
        var builder = new StringBuilder();
        builder.AppendLine(GeneratorVersion);
        builder.AppendLine(baseHash);
        builder.AppendLine(seed.ToString(CultureInfo.InvariantCulture));
        foreach (var variant in variants.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append(variant.Id).Append('|').Append(variant.ContentHash).Append('|');
            foreach (var dimension in variant.Dimensions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.Append(dimension.Key).Append('=').Append(dimension.Value).Append(';');
            builder.AppendLine();
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return "sha256:" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    static LabGenerationEnvironment CaptureEnvironment()
    {
        var assemblyVersion = typeof(SeededVariantGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(SeededVariantGenerator).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        return new LabGenerationEnvironment
        {
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            RuntimeVersion = Environment.Version.ToString(),
            OsDescription = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CurrentCulture = CultureInfo.CurrentCulture.Name,
            GeneratorAssemblyVersion = assemblyVersion
        };
    }

    static void WriteManifest(string outputRoot, LabGenerationManifest manifest)
    {
        WriteText(
            Path.Combine(outputRoot, "generation-manifest.json"),
            JsonSerializer.Serialize(manifest, LabJson.Options) + Environment.NewLine);

        var lines = new List<string>
        {
            "# Migrator Lab seeded generation",
            "",
            $"- **Family:** `{manifest.Family}`",
            $"- **Base:** `{manifest.BaseScenarioId}`",
            $"- **Seed:** `{manifest.Seed}`",
            $"- **Generator:** `{manifest.GeneratorVersion}`",
            $"- **Corpus fingerprint:** `{manifest.CorpusFingerprint}`",
            $"- **Variants:** `{manifest.Variants.Length}`",
            "",
            "| Scenario | Seed | Dimensions | Content hash |",
            "|---|---:|---|---|"
        };
        foreach (var variant in manifest.Variants)
        {
            lines.Add($"| {variant.Id} | {variant.Seed} | {FormatDimensions(variant.Dimensions)} | `{variant.ContentHash}` |");
        }

        WriteText(Path.Combine(outputRoot, "generation-manifest.md"), string.Join("\n", lines) + "\n");
    }

    static string NormalizeNewlines(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');
}
