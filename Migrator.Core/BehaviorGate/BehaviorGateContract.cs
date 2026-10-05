using System;
using System.Collections.Generic;

namespace Migrator.Core.BehaviorGate;

public static class BehaviorGateContract
{
    public const string SpecSchemaVersion = "migrator-behavior-spec/v1";
    public const string ReportSchemaVersion = "migrator-behavior/v1";

    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Blocked = "blocked";

    public const string KindDomText = "dom-text";
    public const string KindDomVisible = "dom-visible";
    public const string KindDomChecked = "dom-checked";
    public const string KindDomCount = "dom-count";
    public const string KindEventSequence = "event-sequence";

    public static readonly string[] Kinds =
    {
        KindDomText,
        KindDomVisible,
        KindDomChecked,
        KindDomCount,
        KindEventSequence
    };

    public static bool IsKnownKind(string kind) => Array.IndexOf(Kinds, kind) >= 0;
}

public enum BehaviorCheckStatus
{
    Passed,
    Mismatch,
    NotObserved
}

/// <summary>
/// One declared observable of a behavior scenario: after the scenario's actions run, the
/// application must be in a state satisfying <see cref="Expected"/> for the element/path.
/// Preconditions and disallowed side effects are declared for humans; enforcement happens
/// through <see cref="BehaviorGateEvaluator"/> on observed checks.
/// </summary>
public sealed record BehaviorObservableSpec(
    string Key,
    string Kind,
    string Path,
    string Expected);

public sealed record BehaviorScenarioSpec(
    string SchemaVersion,
    string Id,
    string Title,
    string ScenarioId,
    IReadOnlyList<BehaviorObservableSpec> Observables,
    IReadOnlyList<string>? Preconditions = null,
    IReadOnlyList<string>? DisallowedSideEffects = null);

public sealed record BehaviorCheckResult(
    string ScenarioId,
    string ScenarioTitle,
    string ObservableKey,
    string Kind,
    string Path,
    string Expected,
    BehaviorCheckStatus Status,
    string? Actual,
    string? Reason);

public sealed record BehaviorGateReport(
    string SchemaVersion,
    string Status,
    int Scenarios,
    int Checks,
    int Passed,
    int Mismatch,
    int NotObserved,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<BehaviorCheckResult> ChecksDetail,
    string Sha256);
