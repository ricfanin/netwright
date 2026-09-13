using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Netwright.Fixtures.Wpf;

/// <summary>
/// A Border that is exposed in the UIA control view (a plain Border has no automation peer)
/// but deliberately supports no control patterns, in particular no Invoke pattern.
/// </summary>
public sealed class MouseTarget : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new MouseTargetAutomationPeer(this);

    private sealed class MouseTargetAutomationPeer : FrameworkElementAutomationPeer
    {
        public MouseTargetAutomationPeer(MouseTarget owner)
            : base(owner)
        {
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;

        protected override string GetClassNameCore() => "MouseTarget";

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}
