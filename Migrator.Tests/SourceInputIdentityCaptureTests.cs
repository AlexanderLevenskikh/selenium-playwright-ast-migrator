using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

public sealed class SourceInputIdentityCaptureTests
{
    [Fact]
    public void Capture_IsStableAndCanExcludeMigrationWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-source-identity-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var migration = Path.Combine(source, "migration");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(migration);

        try
        {
            File.WriteAllText(Path.Combine(source, "B.cs"), "class B {}\n");
            File.WriteAllText(Path.Combine(source, "A.cs"), "class A {}\n");
            File.WriteAllText(Path.Combine(migration, "Ghost.cs"), "class Ghost {}\n");

            var first = SourceInputIdentityCapture.Capture(source, migration);

            File.Delete(Path.Combine(source, "A.cs"));
            File.WriteAllText(Path.Combine(source, "A.cs"), "class A {}\n");
            File.WriteAllText(Path.Combine(migration, "Ghost.cs"), "class ChangedGhost {}\n");
            var second = SourceInputIdentityCapture.Capture(source, migration);

            Assert.Equal(2, first.Files);
            Assert.Equal(first.Hash, second.Hash);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
