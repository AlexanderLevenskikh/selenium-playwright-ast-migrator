namespace Migrator.Lab.Contracts;

public enum ScenarioStatus
{
    Pass,
    PassWithWarnings,
    Regression,
    MigratorFailure,
    SourceInvalid,
    InfrastructureFailure,
    NonDeterministic,
    UnsupportedAsExpected
}

public enum ScenarioImplementationState
{
    Planned,
    Ready
}

public enum ValidationIssueSeverity
{
    Warning,
    Error
}
