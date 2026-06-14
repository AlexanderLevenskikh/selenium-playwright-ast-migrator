namespace Migrator.Core;

/// <summary>
/// Thrown when adapter-config.json fails validation.
/// Contains structured, user-readable validation messages.
/// </summary>
public sealed class ConfigValidationError : Exception
{
    /// <summary>
    /// Individual validation error messages. Each is a self-contained line.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    public ConfigValidationError(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Invalid adapter-config.json:");
        foreach (var error in errors)
        {
            sb.AppendLine(error);
        }
        return sb.ToString().TrimEnd();
    }
}
