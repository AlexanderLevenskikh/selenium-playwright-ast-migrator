using Migrator.Core;

namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Reserved frontend entry used to make future source ids discoverable while failing explicitly.
/// </summary>
public sealed class UnsupportedSourceFrontend : ISourceFrontend
{
    readonly string _message;

    public UnsupportedSourceFrontend(SourceSpec source, string message, IReadOnlyCollection<string>? aliases = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _message = string.IsNullOrWhiteSpace(message)
            ? $"Source frontend '{source.Id}' is not implemented yet."
            : message;
        Aliases = aliases ?? Array.Empty<string>();
    }

    public SourceSpec Source { get; }
    public IReadOnlyCollection<string> Aliases { get; }

    public bool CanParse(MigrationRequest request) => false;

    public SourceParseResult Parse(MigrationRequest request) => throw new NotSupportedException(_message);

    public NotSupportedException CreateNotSupportedException() => new(_message);
}
