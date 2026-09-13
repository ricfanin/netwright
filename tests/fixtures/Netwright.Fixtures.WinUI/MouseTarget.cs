using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Netwright.Fixtures.WinUI;

/// <summary>
/// A panel with a visible background that is exposed in the UIA control view (a plain Grid/Border has no
/// automation peer; WinUI Border is sealed) but deliberately supports no control patterns, in particular no Invoke.
/// </summary>
public sealed partial class MouseTarget : Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new MouseTargetAutomationPeer(this);

    private sealed partial class MouseTargetAutomationPeer : FrameworkElementAutomationPeer
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
