namespace Migrator.Lab.Corpus.P32.TargetCode;

// Pre-existing production code that already lives in the TARGET project before the
// migration. It is carried into the generated runtime target verbatim (PreExisting/)
// and never passed to the migrator.
public static class OrderStatus
{
    public static string Normalize(string raw) => (raw ?? string.Empty).Trim().ToLowerInvariant();
}
