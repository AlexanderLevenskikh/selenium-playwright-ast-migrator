using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Migrator.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Migrator.Roslyn;

/// <summary>
/// Deterministic project-level semantic view used as the foundation for whole-project
/// migration analysis. The same compilation graph is reused internally by the production
/// Roslyn source frontend so project-owned symbol identity does not have to be guessed from
/// invocation text. The builder loads SDK-style C# sources and ProjectReference edges without
/// invoking MSBuild, so package assets unavailable through framework/HintPath references may
/// remain unresolved and are reported as diagnostics rather than guessed.
/// </summary>
public sealed record ProjectSemanticIndex(
    string SchemaVersion,
    string RootProject,
    string SemanticSha256,
    string ReferenceMode,
    int Projects,
    int SourceFiles,
    int Types,
    int Methods,
    int Calls,
    int ResolvedCalls,
    int UnresolvedCalls,
    int CompilationErrors,
    int CompilationWarnings,
    SemanticProjectRecord[] ProjectRecords,
    SemanticTypeRecord[] TypeRecords,
    SemanticMethodRecord[] MethodRecords,
    SemanticCallRecord[] CallRecords,
    SemanticDiagnosticRecord[] Diagnostics);

public sealed record SemanticProjectRecord(
    string ProjectFile,
    string AssemblyName,
    string[] SourceFiles,
    string[] ProjectReferences);

public sealed record SemanticTypeRecord(
    string SymbolId,
    string ProjectFile,
    string Name,
    string Kind,
    string? BaseType,
    string[] Interfaces,
    string[] SourceFiles,
    bool IsPartial);

public sealed record SemanticMethodRecord(
    string SymbolId,
    string ProjectFile,
    string ContainingTypeSymbolId,
    string Name,
    string ReturnType,
    string SourceFile,
    int SourceLine,
    bool IsAsyncDeclared,
    bool ReturnsAwaitable,
    bool IsExtensionMethod);

public sealed record SemanticCallRecord(
    string CallerSymbolId,
    string? CalleeSymbolId,
    string Display,
    string SourceFile,
    int SourceLine,
    int SourceColumn,
    bool IsResolved,
    bool IsSourceMethod,
    bool IsExtensionMethod,
    bool IsAwaited,
    string CandidateReason);

public sealed record SemanticDiagnosticRecord(
    string Id,
    string Severity,
    string ProjectFile,
    string SourceFile,
    int SourceLine,
    int SourceColumn);


internal sealed record ProjectSemanticBoundSource(
    CSharpCompilation Compilation,
    SyntaxTree SyntaxTree);

internal sealed record ProjectSemanticSourceCompilation(
    CSharpCompilation Compilation,
    SyntaxTree SyntaxTree);

internal sealed class ProjectSemanticCompilationContext
{
    readonly IReadOnlyDictionary<string, ProjectSemanticSourceCompilation> _sources;

    public ProjectSemanticCompilationContext(
        string rootProjectPath,
        IReadOnlyDictionary<string, ProjectSemanticSourceCompilation> sources)
    {
        RootProjectPath = Path.GetFullPath(rootProjectPath);
        _sources = sources;
    }

    public string RootProjectPath { get; }

    public CSharpParseOptions? GetParseOptions(string sourceFile)
    {
        var key = NormalizePath(sourceFile);
        return _sources.TryGetValue(key, out var source)
            ? source.SyntaxTree.Options as CSharpParseOptions
            : null;
    }

    public IReadOnlyDictionary<string, ProjectSemanticBoundSource> BindSourceOverrides(
        IReadOnlyDictionary<string, SyntaxTree> sourceOverrides)
    {
        ArgumentNullException.ThrowIfNull(sourceOverrides);

        var normalizedOverrides = sourceOverrides
            .ToDictionary(
                pair => NormalizePath(pair.Key),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        var transformed = new Dictionary<CSharpCompilation, CSharpCompilation>(
            ReferenceEqualityComparer.Instance);

        foreach (var source in _sources.Values)
        {
            if (transformed.ContainsKey(source.Compilation))
                continue;

            var compilation = source.Compilation;
            foreach (var entry in _sources
                         .Where(entry => ReferenceEquals(entry.Value.Compilation, source.Compilation))
                         .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(entry => entry.Key, StringComparer.Ordinal))
            {
                if (normalizedOverrides.TryGetValue(entry.Key, out var replacement))
                    compilation = compilation.ReplaceSyntaxTree(entry.Value.SyntaxTree, replacement);
            }

            transformed[source.Compilation] =
                SemanticCompilationSupport.AddMissingFrameworkAnchors(compilation);
        }

        var result = new Dictionary<string, ProjectSemanticBoundSource>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _sources
                     .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var tree = normalizedOverrides.TryGetValue(entry.Key, out var replacement)
                ? replacement
                : entry.Value.SyntaxTree;

            result[entry.Key] = new ProjectSemanticBoundSource(
                transformed[entry.Value.Compilation],
                tree);
        }

        return result;
    }

    static string NormalizePath(string path)
        => Path.GetFullPath(path);
}
public static class ProjectSemanticIndexBuilder
{
    const string SchemaVersion = "project-semantic-index/v1";
    const string ReferenceMode = "framework+hintpath+project-references";

    static readonly Lazy<MetadataReference[]> PlatformReferences = new(CreatePlatformReferences);

    public static string? FindNearestProject(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return null;

        var full = Path.GetFullPath(inputPath);
        if (File.Exists(full) && string.Equals(Path.GetExtension(full), ".csproj", StringComparison.OrdinalIgnoreCase))
            return full;

        var start = File.Exists(full) ? Path.GetDirectoryName(full) : full;
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
            return null;

        // Never infer a source project by walking out of generated/build/test-output trees.
        // Those paths often live under a repository that happens to contain a csproj but are
        // not themselves authoritative project source inputs.
        if (ContainsGeneratedPathSegment(start))
            return null;

        var current = new DirectoryInfo(start);
        while (current != null)
        {
            var projects = current.GetFiles("*.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();
            if (projects.Length == 1)
                return projects[0].FullName;
            if (projects.Length > 1)
                return null; // Ambiguous project ownership is explicit, never first-match.

            current = current.Parent;
        }

        return null;
    }

    public static ProjectSemanticIndex BuildForInput(string inputPath)
    {
        var projectPath = FindNearestProject(inputPath)
            ?? throw new InvalidOperationException("No unambiguous C# project could be associated with the input path.");
        return Build(projectPath);
    }


    internal static ProjectSemanticCompilationContext? BuildCompilationContextForInput(string inputPath)
    {
        var projectPath = FindNearestProject(inputPath);
        if (projectPath == null)
            return null;

        var graph = new CompilationGraphBuilder().Build(projectPath);
        var sources = new Dictionary<string, ProjectSemanticSourceCompilation>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes.Values
                     .OrderBy(node => node.ProjectPath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(node => node.ProjectPath, StringComparer.Ordinal))
        {
            foreach (var tree in node.Compilation.SyntaxTrees
                         .OrderBy(tree => tree.FilePath, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(tree => tree.FilePath, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(tree.FilePath))
                    continue;

                var fullPath = Path.GetFullPath(tree.FilePath);
                if (sources.TryGetValue(fullPath, out var existing)
                    && !ReferenceEquals(existing.Compilation, node.Compilation))
                {
                    throw new InvalidOperationException(
                        $"PROJECT_SEMANTIC_SOURCE_OWNERSHIP_AMBIGUOUS: '{fullPath}' belongs to more than one project compilation.");
                }

                sources[fullPath] = new ProjectSemanticSourceCompilation(
                    node.Compilation,
                    tree);
            }
        }

        return new ProjectSemanticCompilationContext(projectPath, sources);
    }
    public static ProjectSemanticIndex Build(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        projectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(projectPath))
            throw new FileNotFoundException("Project file was not found.", projectPath);

        var graph = new CompilationGraphBuilder().Build(projectPath);
        var rootDirectory = Path.GetDirectoryName(projectPath)!;

        var projectRecords = graph.Nodes.Values
            .OrderBy(node => StableRelativePath(rootDirectory, node.ProjectPath), StringComparer.Ordinal)
            .Select(node => new SemanticProjectRecord(
                ProjectFile: StableRelativePath(rootDirectory, node.ProjectPath),
                AssemblyName: node.Compilation.AssemblyName ?? Path.GetFileNameWithoutExtension(node.ProjectPath),
                SourceFiles: node.SourceFiles
                    .Select(file => StableRelativePath(rootDirectory, file))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray(),
                ProjectReferences: node.ProjectReferences
                    .Select(reference => StableRelativePath(rootDirectory, reference))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        var types = new Dictionary<string, SemanticTypeRecord>(StringComparer.Ordinal);
        var methods = new Dictionary<string, SemanticMethodRecord>(StringComparer.Ordinal);
        var calls = new List<SemanticCallRecord>();
        var diagnostics = new List<SemanticDiagnosticRecord>();
        var sourceAssemblyNames = graph.Nodes.Values
            .Select(node => node.Compilation.AssemblyName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var node in graph.Nodes.Values
                     .OrderBy(item => StableRelativePath(rootDirectory, item.ProjectPath), StringComparer.Ordinal))
        {
            var projectFile = StableRelativePath(rootDirectory, node.ProjectPath);
            foreach (var tree in node.Compilation.SyntaxTrees
                         .OrderBy(tree => StableRelativePath(rootDirectory, tree.FilePath), StringComparer.Ordinal))
            {
                var model = node.Compilation.GetSemanticModel(tree, ignoreAccessibility: true);
                var root = tree.GetRoot();

                foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                        continue;

                    var symbolId = TypeId(symbol);
                    types[symbolId] = new SemanticTypeRecord(
                        SymbolId: symbolId,
                        ProjectFile: projectFile,
                        Name: symbol.Name,
                        Kind: symbol.TypeKind.ToString(),
                        BaseType: symbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType ? TypeId(baseType) : null,
                        Interfaces: symbol.Interfaces.Select(TypeId).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                        SourceFiles: symbol.DeclaringSyntaxReferences
                            .Select(reference => StableRelativePath(rootDirectory, reference.SyntaxTree.FilePath))
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(value => value, StringComparer.Ordinal)
                            .ToArray(),
                        IsPartial: symbol.DeclaringSyntaxReferences.Length > 1
                            || declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
                }

                foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(declaration) is not IMethodSymbol method)
                        continue;

                    var methodId = MethodId(method);
                    var lineSpan = declaration.Identifier.GetLocation().GetLineSpan();
                    methods[methodId] = new SemanticMethodRecord(
                        SymbolId: methodId,
                        ProjectFile: projectFile,
                        ContainingTypeSymbolId: TypeId(method.ContainingType),
                        Name: method.Name,
                        ReturnType: TypeId(method.ReturnType),
                        SourceFile: StableRelativePath(rootDirectory, tree.FilePath),
                        SourceLine: lineSpan.StartLinePosition.Line + 1,
                        IsAsyncDeclared: declaration.Modifiers.Any(SyntaxKind.AsyncKeyword),
                        ReturnsAwaitable: IsAwaitable(method.ReturnType),
                        IsExtensionMethod: method.IsExtensionMethod);

                    foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        // Nested local/lambda declarations own their own calls and must not leak
                        // into the enclosing method's call graph entry. Local functions are
                        // intentionally deferred until they have their own stable symbol records.
                        if (invocation.Ancestors()
                            .TakeWhile(ancestor => !ReferenceEquals(ancestor, declaration))
                            .Any(ancestor => ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))
                            continue;

                        var symbolInfo = model.GetSymbolInfo(invocation);
                        var callee = symbolInfo.Symbol as IMethodSymbol;
                        var canonicalCallee = callee?.ReducedFrom ?? callee;
                        var location = invocation.GetLocation().GetLineSpan();
                        calls.Add(new SemanticCallRecord(
                            CallerSymbolId: methodId,
                            CalleeSymbolId: canonicalCallee != null ? MethodId(canonicalCallee) : null,
                            Display: NormalizeDisplay(invocation.Expression.ToString()),
                            SourceFile: StableRelativePath(rootDirectory, tree.FilePath),
                            SourceLine: location.StartLinePosition.Line + 1,
                            SourceColumn: location.StartLinePosition.Character + 1,
                            IsResolved: canonicalCallee != null,
                            IsSourceMethod: canonicalCallee?.ContainingAssembly?.Name is { } assemblyName
                                && sourceAssemblyNames.Contains(assemblyName),
                            IsExtensionMethod: callee?.ReducedFrom != null || canonicalCallee?.IsExtensionMethod == true,
                            IsAwaited: invocation.Ancestors().OfType<AwaitExpressionSyntax>().Any(),
                            CandidateReason: symbolInfo.CandidateReason.ToString()));
                    }
                }
            }

            foreach (var diagnostic in node.Compilation.GetDiagnostics()
                         .Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
            {
                var span = diagnostic.Location.GetLineSpan();
                diagnostics.Add(new SemanticDiagnosticRecord(
                    Id: diagnostic.Id,
                    Severity: diagnostic.Severity.ToString(),
                    ProjectFile: projectFile,
                    SourceFile: diagnostic.Location.IsInSource
                        ? StableRelativePath(rootDirectory, span.Path)
                        : string.Empty,
                    SourceLine: diagnostic.Location.IsInSource ? span.StartLinePosition.Line + 1 : 0,
                    SourceColumn: diagnostic.Location.IsInSource ? span.StartLinePosition.Character + 1 : 0));
            }
        }

        var typeRecords = types.Values.OrderBy(item => item.SymbolId, StringComparer.Ordinal).ToArray();
        var methodRecords = methods.Values.OrderBy(item => item.SymbolId, StringComparer.Ordinal).ToArray();
        var callRecords = calls
            .OrderBy(item => item.CallerSymbolId, StringComparer.Ordinal)
            .ThenBy(item => item.SourceFile, StringComparer.Ordinal)
            .ThenBy(item => item.SourceLine)
            .ThenBy(item => item.SourceColumn)
            .ThenBy(item => item.CalleeSymbolId ?? item.Display, StringComparer.Ordinal)
            .ToArray();
        var diagnosticRecords = diagnostics
            .OrderBy(item => item.ProjectFile, StringComparer.Ordinal)
            .ThenBy(item => item.SourceFile, StringComparer.Ordinal)
            .ThenBy(item => item.SourceLine)
            .ThenBy(item => item.SourceColumn)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        var semanticHash = ComputeSemanticHash(projectRecords, typeRecords, methodRecords, callRecords, diagnosticRecords);
        return new ProjectSemanticIndex(
            SchemaVersion,
            RootProject: StableRelativePath(rootDirectory, projectPath),
            SemanticSha256: semanticHash,
            ReferenceMode,
            Projects: projectRecords.Length,
            SourceFiles: projectRecords.Sum(project => project.SourceFiles.Length),
            Types: typeRecords.Length,
            Methods: methodRecords.Length,
            Calls: callRecords.Length,
            ResolvedCalls: callRecords.Count(call => call.IsResolved),
            UnresolvedCalls: callRecords.Count(call => !call.IsResolved),
            CompilationErrors: diagnosticRecords.Count(item => string.Equals(item.Severity, nameof(DiagnosticSeverity.Error), StringComparison.Ordinal)),
            CompilationWarnings: diagnosticRecords.Count(item => string.Equals(item.Severity, nameof(DiagnosticSeverity.Warning), StringComparison.Ordinal)),
            ProjectRecords: projectRecords,
            TypeRecords: typeRecords,
            MethodRecords: methodRecords,
            CallRecords: callRecords,
            Diagnostics: diagnosticRecords);
    }

    static bool ContainsGeneratedPathSegment(string path)
    {
        var parts = Path.GetFullPath(path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || part.Equals("artifacts", StringComparison.OrdinalIgnoreCase)
                || part.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || part.Equals(".vs", StringComparison.OrdinalIgnoreCase)
                || part.Equals(".idea", StringComparison.OrdinalIgnoreCase))
                return true;

            if (part.Equals("migration", StringComparison.OrdinalIgnoreCase)
                && i + 1 < parts.Length
                && parts[i + 1].Equals("runs", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static bool IsAwaitable(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return false;

        var definition = named.OriginalDefinition;
        var namespaceName = definition.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return string.Equals(namespaceName, "System.Threading.Tasks", StringComparison.Ordinal)
            && (string.Equals(definition.Name, "Task", StringComparison.Ordinal)
                || string.Equals(definition.Name, "ValueTask", StringComparison.Ordinal));
    }

    static string TypeId(ITypeSymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    static string MethodId(IMethodSymbol method)
    {
        var canonical = method.ReducedFrom ?? method;
        var containingType = canonical.ContainingType != null ? TypeId(canonical.ContainingType) : "<global>";
        var parameters = string.Join(",", canonical.Parameters.Select(parameter => TypeId(parameter.Type)));
        return $"{containingType}::{canonical.MetadataName}({parameters})";
    }

    static string NormalizeDisplay(string value) => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    static string StableRelativePath(string rootDirectory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var full = Path.GetFullPath(path);
        return Path.GetRelativePath(rootDirectory, full).Replace('\\', '/');
    }

    static string ComputeSemanticHash(
        IEnumerable<SemanticProjectRecord> projects,
        IEnumerable<SemanticTypeRecord> types,
        IEnumerable<SemanticMethodRecord> methods,
        IEnumerable<SemanticCallRecord> calls,
        IEnumerable<SemanticDiagnosticRecord> diagnostics)
    {
        var builder = new StringBuilder();
        builder.Append("SCHEMA|").Append(SchemaVersion).Append('|').Append(ReferenceMode).Append('\n');
        foreach (var project in projects)
        {
            builder.Append("P|").Append(project.ProjectFile).Append('|').Append(project.AssemblyName).Append('\n');
            foreach (var source in project.SourceFiles)
                builder.Append("PS|").Append(project.ProjectFile).Append('|').Append(source).Append('\n');
            foreach (var reference in project.ProjectReferences)
                builder.Append("PR|").Append(project.ProjectFile).Append('|').Append(reference).Append('\n');
        }

        foreach (var type in types)
        {
            builder.Append("T|").Append(type.SymbolId).Append('|').Append(type.ProjectFile).Append('|')
                .Append(type.Kind).Append('|').Append(type.BaseType).Append('|').Append(type.IsPartial).Append('\n');
            foreach (var @interface in type.Interfaces)
                builder.Append("TI|").Append(type.SymbolId).Append('|').Append(@interface).Append('\n');
            foreach (var source in type.SourceFiles)
                builder.Append("TS|").Append(type.SymbolId).Append('|').Append(source).Append('\n');
        }

        foreach (var method in methods)
        {
            builder.Append("M|").Append(method.SymbolId).Append('|').Append(method.ProjectFile).Append('|')
                .Append(method.ReturnType).Append('|').Append(method.SourceFile).Append('|').Append(method.SourceLine).Append('|')
                .Append(method.IsAsyncDeclared).Append('|').Append(method.ReturnsAwaitable).Append('|').Append(method.IsExtensionMethod).Append('\n');
        }

        foreach (var call in calls)
        {
            builder.Append("C|").Append(call.CallerSymbolId).Append('|').Append(call.CalleeSymbolId).Append('|')
                .Append(call.Display).Append('|').Append(call.SourceFile).Append('|').Append(call.SourceLine).Append('|')
                .Append(call.SourceColumn).Append('|').Append(call.IsResolved).Append('|').Append(call.IsSourceMethod).Append('|')
                .Append(call.IsExtensionMethod).Append('|').Append(call.IsAwaited).Append('|').Append(call.CandidateReason).Append('\n');
        }

        foreach (var diagnostic in diagnostics)
        {
            builder.Append("D|").Append(diagnostic.Id).Append('|').Append(diagnostic.Severity).Append('|')
                .Append(diagnostic.ProjectFile).Append('|').Append(diagnostic.SourceFile).Append('|')
                .Append(diagnostic.SourceLine).Append('|').Append(diagnostic.SourceColumn).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    static MetadataReference[] CreatePlatformReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            return new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            };
        }

        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    sealed class CompilationGraphBuilder
    {
        readonly Dictionary<string, CompilationNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _stack = new(StringComparer.OrdinalIgnoreCase);

        public CompilationGraph Build(string rootProjectPath)
        {
            BuildNode(Path.GetFullPath(rootProjectPath));
            return new CompilationGraph(_nodes);
        }

        CompilationNode BuildNode(string projectPath)
        {
            if (_nodes.TryGetValue(projectPath, out var existing))
                return existing;
            if (!_stack.Add(projectPath))
                throw new InvalidOperationException($"ProjectReference cycle detected at {Path.GetFileName(projectPath)}.");

            try
            {
                var document = XDocument.Load(projectPath, LoadOptions.None);
                var projectDirectory = Path.GetDirectoryName(projectPath)!;
                var projectReferences = ReadProjectReferences(document, projectDirectory);
                var referenceNodes = projectReferences.Select(BuildNode).ToArray();
                var sourceFiles = ReadSourceFiles(document, projectDirectory);
                var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: ReadDefineConstants(document));
                var trees = sourceFiles
                    .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file, Encoding.UTF8))
                    .ToArray();

                var references = new List<MetadataReference>(PlatformReferences.Value);
                references.AddRange(ReadHintPathReferences(document, projectDirectory));
                references.AddRange(referenceNodes.Select(node => node.Compilation.ToMetadataReference()));

                var assemblyName = ReadProperty(document, "AssemblyName") ?? Path.GetFileNameWithoutExtension(projectPath);
                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    trees,
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                var node = new CompilationNode(projectPath, projectReferences, sourceFiles, compilation);
                _nodes[projectPath] = node;
                return node;
            }
            finally
            {
                _stack.Remove(projectPath);
            }
        }

        static string[] ReadProjectReferences(XDocument document, string projectDirectory)
        {
            return document.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("$(", StringComparison.Ordinal))
                .Select(value => ResolveRelativePath(projectDirectory, value!))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        static string[] ReadSourceFiles(XDocument document, string projectDirectory)
        {
            var defaultItems = !string.Equals(ReadProperty(document, "EnableDefaultCompileItems"), "false", StringComparison.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (defaultItems)
            {
                foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                             .Where(file => !ContainsGeneratedPathSegment(file)))
                    files.Add(Path.GetFullPath(file));
            }

            foreach (var include in ReadCompilePatterns(document, "Include"))
            {
                foreach (var file in ExpandCompilePattern(projectDirectory, include))
                    files.Add(file);
            }

            foreach (var remove in ReadCompilePatterns(document, "Remove"))
            {
                foreach (var file in files.ToArray())
                {
                    var relative = Path.GetRelativePath(projectDirectory, file).Replace('\\', '/');
                    if (ScopeResolver.IsMatch(remove, relative))
                        files.Remove(file);
                }
            }

            return files
                .Where(File.Exists)
                .OrderBy(path => Path.GetRelativePath(projectDirectory, path).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => Path.GetRelativePath(projectDirectory, path).Replace('\\', '/'), StringComparer.Ordinal)
                .ToArray();
        }

        static IEnumerable<string> ReadCompilePatterns(XDocument document, string attributeName)
        {
            return document.Descendants()
                .Where(element => element.Name.LocalName == "Compile")
                .Select(element => element.Attribute(attributeName)?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .SelectMany(value => value!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(value => !value.Contains("$(", StringComparison.Ordinal));
        }

        static IEnumerable<string> ExpandCompilePattern(string projectDirectory, string pattern)
        {
            var normalized = pattern.Replace('\\', '/');
            if (!normalized.Contains('*') && !normalized.Contains('?'))
            {
                var full = ResolveRelativePath(projectDirectory, normalized);
                if (File.Exists(full))
                    yield return full;
                yield break;
            }

            // SDK-style wildcard Compile items are normally rooted in the project directory.
            // Enumerate candidate C# files deterministically and reuse the canonical glob engine.
            foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                         .Where(file => !ContainsGeneratedPathSegment(file))
                         .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(file => file, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(projectDirectory, file).Replace('\\', '/');
                if (ScopeResolver.IsMatch(normalized, relative))
                    yield return Path.GetFullPath(file);
            }
        }

        static MetadataReference[] ReadHintPathReferences(XDocument document, string projectDirectory)
        {
            return document.Descendants()
                .Where(element => element.Name.LocalName == "Reference")
                .Select(element => element.Elements().FirstOrDefault(child => child.Name.LocalName == "HintPath")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("$(", StringComparison.Ordinal))
                .Select(value => ResolveRelativePath(projectDirectory, value!))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        static string[] ReadDefineConstants(XDocument document)
        {
            return document.Descendants()
                .Where(element => element.Name.LocalName == "DefineConstants")
                .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(value => value.All(character => char.IsLetterOrDigit(character) || character == '_'))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        static string ResolveRelativePath(string baseDirectory, string relativePath)
        {
            var platformPath = relativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(baseDirectory, platformPath));
        }

        static string? ReadProperty(XDocument document, string propertyName)
        {
            return document.Descendants()
                .Where(element => element.Name.LocalName == propertyName)
                .Select(element => element.Value.Trim())
                .LastOrDefault(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("$(", StringComparison.Ordinal));
        }
    }

    sealed record CompilationGraph(IReadOnlyDictionary<string, CompilationNode> Nodes);
    sealed record CompilationNode(string ProjectPath, string[] ProjectReferences, string[] SourceFiles, CSharpCompilation Compilation);
}
