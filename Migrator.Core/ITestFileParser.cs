using Migrator.Core.Models;

namespace Migrator.Core;

public interface ITestFileParser
{
    TestFileModel Parse(string filePath);
    IEnumerable<TestFileModel> ParseDirectory(string directoryPath);
}
