using System.Text;
using Migrator.Core.Models;

namespace Migrator.PlaywrightDotNet;

public partial class PlaywrightDotNetRenderer
{
    void RenderControlStateAssertion(StringBuilder sb, ControlStateAssertionAction action)
    {
        if (action.Target.Kind == TargetKind.Unresolved)
        {
            RenderUnresolvedTargetComment(sb, action.Target, "control state assertion", action.SourceLine);
            return;
        }

        var method = action.Kind == ControlStateAssertionKind.Disabled
            ? "ToBeDisabledAsync"
            : "ToBeEnabledAsync";
        sb.AppendLine($"{_indent}{_indent}await {ExpectCall()}({RenderTargetExpression(action.Target)}).{method}(); // line {action.SourceLine}");
    }
}
