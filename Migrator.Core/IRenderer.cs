using Migrator.Core.Models;

namespace Migrator.Core;

public interface IRenderer
{
    string Render(TestFileModel model, IProjectAdapter? adapter = null);
}
