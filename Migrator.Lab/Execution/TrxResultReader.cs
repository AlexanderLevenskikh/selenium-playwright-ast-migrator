using System.Xml.Linq;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public static class TrxResultReader
{
    public static LabSourceTestSummary Read(string trxPath, int expectedPassed)
    {
        if (!File.Exists(trxPath))
        {
            return new LabSourceTestSummary
            {
                ExpectedPassed = expectedPassed,
                TrxPath = Path.GetFullPath(trxPath)
            };
        }

        var document = XDocument.Load(trxPath);
        var counters = document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "Counters", StringComparison.OrdinalIgnoreCase));

        return new LabSourceTestSummary
        {
            Total = ReadInt(counters, "total"),
            Passed = ReadInt(counters, "passed"),
            Failed = ReadInt(counters, "failed") + ReadInt(counters, "error") + ReadInt(counters, "timeout") + ReadInt(counters, "aborted"),
            Skipped = ReadInt(counters, "notExecuted") + ReadInt(counters, "inconclusive") + ReadInt(counters, "notRunnable"),
            ExpectedPassed = expectedPassed,
            TrxPath = Path.GetFullPath(trxPath)
        };
    }

    static int ReadInt(XElement? element, string attribute)
    {
        var raw = element?.Attribute(attribute)?.Value;
        return int.TryParse(raw, out var value) ? value : 0;
    }
}
