using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public interface ILabProcessRunner
{
    Task<LabProcessResult> RunAsync(LabProcessRequest request, CancellationToken cancellationToken = default);
}
