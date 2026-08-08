using System.Runtime.CompilerServices;

// Allows Migrator.Tests to exercise internal triage/automation-policy logic
// (e.g. LabFailureTriageService.RecommendAutomationDisposition and
// WithinAutoFixComponentBudget) directly with constructed inputs, without
// exposing them as a public API surface.
[assembly: InternalsVisibleTo("Migrator.Tests")]
