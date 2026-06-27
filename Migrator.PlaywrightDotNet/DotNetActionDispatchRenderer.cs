using System.Text;
using Migrator.Core.Models;

namespace Migrator.PlaywrightDotNet;

public partial class PlaywrightDotNetRenderer
{
    static bool ActionHasResolvedTarget(TestAction action) => action switch
    {
        ClickAction a => a.Target.Kind != TargetKind.Unresolved,
        SendKeysAction a => a.Target.Kind != TargetKind.Unresolved,
        PressAction a => a.Target.Kind != TargetKind.Unresolved,
        TextAssertionAction a => a.Target.Kind != TargetKind.Unresolved,
        VisibilityAssertionAction a => a.Target.Kind != TargetKind.Unresolved,
        WaitForAction a => a.Target.Kind != TargetKind.Unresolved,
        TableCountAssertionAction a => a.Target.Kind != TargetKind.Unresolved,
        TableRowAccessAction a => a.Target.Kind != TargetKind.Unresolved,
        TableRowTextAccessAction a => a.Target.Kind != TargetKind.Unresolved,
        _ => false
    };
    void RenderAction(StringBuilder sb, TestAction action)
    {
        switch (action)
        {
            case ClickAction click:
                RenderClick(sb, click);
                break;
            case SendKeysAction sendKeys:
                RenderSendKeys(sb, sendKeys);
                break;
            case PressAction press:
                RenderPress(sb, press);
                break;
            case AssertThatAction assertThat:
                RenderAssertThat(sb, assertThat);
                break;
            case AssertAreEqualAction assertEqual:
                RenderAssertAreEqual(sb, assertEqual);
                break;
            case TextAssertionAction textAssert:
                RenderTextAssertion(sb, textAssert);
                break;
            case VisibilityAssertionAction visAssert:
                RenderVisibilityAssertion(sb, visAssert);
                break;
            case WaitForAction wait:
                RenderWaitFor(sb, wait);
                break;
            case UrlAssertionAction urlAssert:
                RenderUrlAssertion(sb, urlAssert);
                break;
            case MethodInvocationAction methodInv:
                RenderMethodInvocation(sb, methodInv);
                break;
            case MappedMethodInvocationAction mappedInv:
                RenderMappedMethodInvocation(sb, mappedInv);
                break;
            case MappedExpressionAssertionAction mappedExpr:
                RenderMappedExpressionAssertion(sb, mappedExpr);
                break;
            case AssertMultipleAction assertMultiple:
                RenderAssertMultiple(sb, assertMultiple);
                break;
            case UnsupportedAction unsupported:
                RenderUnsupported(sb, unsupported);
                break;
            case RawStatementAction raw:
                RenderRawStatement(sb, raw);
                break;
            case LocalDeclarationAction localDecl:
                RenderLocalDeclaration(sb, localDecl);
                break;
            case LocatorDeclarationAction locatorDecl:
                RenderLocatorDeclaration(sb, locatorDecl);
                break;
            case NavigationAction nav:
                RenderNavigation(sb, nav);
                break;
            case ConditionalBlockAction condBlock:
                RenderConditionalBlock(sb, condBlock);
                break;
            case TableRowTextAccessAction tableTextAccess:
                RenderTableRowTextAccess(sb, tableTextAccess);
                break;
            case TableCountAssertionAction tableCountAssert:
                RenderTableCountAssertion(sb, tableCountAssert);
                break;
            case TableRowAccessAction tableRowAccess:
                RenderTableRowAccess(sb, tableRowAccess);
                break;
            default:
                sb.AppendLine($"{_indent}{_indent}// [unknown action: {action.GetType().Name}]");
                break;
        }
    }
}
