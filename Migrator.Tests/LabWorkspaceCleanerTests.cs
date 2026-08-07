using Migrator.Lab.Execution;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class LabWorkspaceCleanerTests
{
    [Fact]
    public void DeleteBuildOutputs_RemovesNestedBinAndObjButPreservesSourceTruth()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-lab-clean-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "Shared", "Widget.cs");
            var nestedObj = Path.Combine(root, "Shared", "obj", "Release", "net10.0");
            var nestedBin = Path.Combine(root, "Tests", "bin", "Release", "net10.0");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(nestedObj);
            Directory.CreateDirectory(nestedBin);
            File.WriteAllText(source, "namespace Fixture; public sealed class Widget { }");
            File.WriteAllText(Path.Combine(nestedObj, "Shared.AssemblyInfo.cs"), "// generated");
            File.WriteAllText(Path.Combine(nestedBin, "fixture.dll"), "binary placeholder");

            LabWorkspaceCleaner.DeleteBuildOutputs(root);

            Assert.True(File.Exists(source));
            Assert.False(Directory.Exists(Path.Combine(root, "Shared", "obj")));
            Assert.False(Directory.Exists(Path.Combine(root, "Tests", "bin")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
