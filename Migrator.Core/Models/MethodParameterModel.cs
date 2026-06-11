namespace Migrator.Core.Models;

public record MethodParameterModel(
    string Type,
    string Name,
    string? DefaultValue
);
