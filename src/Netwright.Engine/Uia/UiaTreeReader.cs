using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using Netwright.Engine.Model;

namespace Netwright.Engine.Uia;

internal enum CaptureDetail
{
    /// <summary>What Snapshots, Selectors and actions need, with live element references.</summary>
    Full,

    /// <summary>
    /// Only what a fingerprint compares, without live references. About half the cost of a full
    /// capture; used to poll whether the UI has Settled.
    /// </summary>
    Light,
}

/// <summary>
/// Captures the Target App's windows into <see cref="UiTree"/>s with one cached UI Automation query
/// per top-level window. Windows are found with Win32 enumeration, which is in-process and far
/// cheaper than asking UI Automation to search the desktop.
/// </summary>
internal sealed class UiaTreeReader
{
    /// <summary>The Win32 system menu that UI Automation synthesizes for every window, with a new RuntimeId on each query.</summary>
    private const string SystemMenuBarId = "SystemMenuBar";

    private readonly IUIAutomation _automation;
    private readonly IUIAutomationCacheRequest _fullRequest;
    private readonly IUIAutomationCacheRequest _fullWithOffscreenRequest;
    private readonly IUIAutomationCacheRequest _lightRequest;
    private readonly IUIAutomationCacheRequest _lightWithOffscreenRequest;
    private readonly object _notSupported;

    public UiaTreeReader(IUIAutomation automation)
    {
        _automation = automation;
        _notSupported = automation.ReservedNotSupportedValue;

        // Filtering offscreen elements inside the provider matters for non-virtualized controls:
        // a WinForms DataGridView with 1000 rows drops from ~8 s to ~1.7 s per capture.
        var onscreen = automation.CreateAndCondition(automation.ControlViewCondition, automation.CreatePropertyCondition(PropertyIds.IsOffscreen, false));
        var full = PropertyIds.FullSet.Select(p => p.Id).ToArray();
        var light = PropertyIds.LightSet.Select(p => p.Id).ToArray();
        _fullRequest = CreateRequest(full, AutomationElementMode.AutomationElementMode_Full, onscreen);
        _fullWithOffscreenRequest = CreateRequest(full, AutomationElementMode.AutomationElementMode_Full);
        _lightRequest = CreateRequest(light, AutomationElementMode.AutomationElementMode_None, onscreen);
        _lightWithOffscreenRequest = CreateRequest(light, AutomationElementMode.AutomationElementMode_None);
    }

    public IUIAutomation Automation => _automation;

    /// <summary>
    /// Visible top-level windows of the given processes in z-order, excluding windows owned by
    /// another window of the app: UI Automation shows those inside their owner.
    /// </summary>
    public static List<nint> TopLevelWindowHandles(IReadOnlyCollection<int> processIds)
    {
        var handles = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.GetWindowThreadProcessId(hwnd, out var pid) == 0)
            {
                return true;
            }

            if (processIds.Contains((int)pid) && NativeMethods.IsWindowVisible(hwnd))
            {
                var owner = NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner);
                var ownedByApp = false;
                if (owner != 0 && NativeMethods.GetWindowThreadProcessId(owner, out var ownerPid) != 0)
                {
                    ownedByApp = processIds.Contains((int)ownerPid) && NativeMethods.IsWindowVisible(owner);
                }

                if (!ownedByApp)
                {
                    handles.Add(hwnd);
                }
            }

            return true;
        }, 0);

        return handles;
    }

    /// <summary>Captures every window of the given processes.</summary>
    public UiTree Capture(IReadOnlyCollection<int> processIds, CaptureDetail detail = CaptureDetail.Full, bool includeOffscreen = false)
    {
        var request = (detail, includeOffscreen) switch
        {
            (CaptureDetail.Full, false) => _fullRequest,
            (CaptureDetail.Full, true) => _fullWithOffscreenRequest,
            (_, false) => _lightRequest,
            _ => _lightWithOffscreenRequest,
        };

        return CaptureWith(processIds, request, detail, includeOffscreen);
    }

    internal UiTree CaptureWith(IReadOnlyCollection<int> processIds, IUIAutomationCacheRequest request, CaptureDetail detail, bool includesOffscreen = true)
    {
        var windows = new List<UiNode>();
        foreach (var handle in TopLevelWindowHandles(processIds))
        {
            try
            {
                var cached = _automation.ElementFromHandleBuildCache(handle, request);
                if (cached is not null)
                {
                    windows.Add(Convert(cached, parent: null, detail, handle));
                }
            }
            catch (COMException)
            {
                // The window closed between enumeration and caching.
            }
        }

        return new UiTree(windows, DateTimeOffset.UtcNow, includesOffscreen, detail == CaptureDetail.Light);
    }

    /// <summary>Captures a single element subtree with full detail, offscreen elements included.</summary>
    public UiNode CaptureElement(IUIAutomationElement element) =>
        Convert(element.BuildUpdatedCache(_fullWithOffscreenRequest), parent: null, CaptureDetail.Full, 0);

    internal IUIAutomationCacheRequest CreateRequest(IEnumerable<int> properties, AutomationElementMode mode, IUIAutomationCondition? filter = null)
    {
        var request = _automation.CreateCacheRequest();
        foreach (var id in properties)
        {
            request.AddProperty(id);
        }

        request.TreeScope = TreeScope.TreeScope_Subtree;
        request.TreeFilter = filter ?? _automation.ControlViewCondition;
        request.AutomationElementMode = mode;
        return request;
    }

    private UiNode Convert(IUIAutomationElement element, UiNode? parent, CaptureDetail detail, nint windowHandle)
    {
        var full = detail == CaptureDetail.Full;
        var controlType = Int(element, PropertyIds.ControlType) ?? ControlTypeIds.Custom;

        // Pattern availability is inferred from the pattern's own state properties, which read as
        // "not supported" when the pattern is missing. This halves the number of cached properties.
        var value = Str(element, PropertyIds.ValueValue);
        var toggle = Int(element, PropertyIds.ToggleToggleState);
        var selected = Bool(element, PropertyIds.SelectionItemIsSelected);
        var expand = Int(element, PropertyIds.ExpandCollapseState);
        var range = Double(element, PropertyIds.RangeValueValue);
        var modal = full ? Bool(element, PropertyIds.WindowIsModal) : null;
        var rows = full ? Int(element, PropertyIds.GridRowCount) : null;
        var scrollable = full ? Bool(element, PropertyIds.ScrollVerticallyScrollable) : null;

        var patterns = UiPatterns.None;
        if (value is not null) patterns |= UiPatterns.Value;
        if (toggle is not null) patterns |= UiPatterns.Toggle;
        if (selected is not null) patterns |= UiPatterns.SelectionItem;
        if (expand is not null) patterns |= UiPatterns.ExpandCollapse;
        if (range is not null) patterns |= UiPatterns.RangeValue;
        if (modal is not null) patterns |= UiPatterns.Window;
        if (rows is not null) patterns |= UiPatterns.Grid;
        if (scrollable is not null) patterns |= UiPatterns.Scroll;
        if (full)
        {
            if (Bool(element, PropertyIds.IsInvokePatternAvailable) == true) patterns |= UiPatterns.Invoke;
            if (Bool(element, PropertyIds.IsScrollItemPatternAvailable) == true) patterns |= UiPatterns.ScrollItem;
            if (Bool(element, PropertyIds.IsLegacyIAccessiblePatternAvailable) == true) patterns |= UiPatterns.LegacyIAccessible;
        }

        var node = new UiNode
        {
            Key = RuntimeKey(element),
            ControlTypeId = controlType,
            Role = Roles.FromControlType(controlType),
            Name = Str(element, PropertyIds.Name) ?? "",
            AutomationId = Str(element, PropertyIds.AutomationId) ?? "",
            WindowHandle = windowHandle,
            IsEnabled = Bool(element, PropertyIds.IsEnabled) ?? true,
            IsOffscreen = Bool(element, PropertyIds.IsOffscreen) ?? false,
            HasFocus = Bool(element, PropertyIds.HasKeyboardFocus) ?? false,
            IsPassword = full && Bool(element, PropertyIds.IsPassword) == true,
            Patterns = patterns,
            Value = value,
            ValueIsReadOnly = full && value is not null && Bool(element, PropertyIds.ValueIsReadOnly) == true,
            Toggle = toggle is { } t ? (ToggleValue)t : null,
            IsSelected = selected,
            Expand = expand is { } e ? (ExpandValue)e : null,
            RangeValue = range,
            IsScrollable = scrollable == true,
            GridRowCount = rows,
            IsModal = modal,
            IsMinimized = windowHandle != 0 && NativeMethods.IsIconic(windowHandle),
            Native = full ? element : null,
            Parent = parent,
        };

        IUIAutomationElementArray? children = null;
        try
        {
            children = element.GetCachedChildren();
        }
        catch (COMException)
        {
            // Children unavailable (element went away mid-query).
        }

        if (children is not null)
        {
            for (var i = 0; i < children.Length; i++)
            {
                var child = children.GetElement(i);
                if (IsSynthesizedChrome(child))
                {
                    continue;
                }

                node.Children.Add(Convert(child, node, detail, 0));
            }
        }

        return node;
    }

    /// <summary>Title bars and system menus carry no app state, and their RuntimeIds change on every query.</summary>
    private bool IsSynthesizedChrome(IUIAutomationElement element) =>
        Int(element, PropertyIds.ControlType) == ControlTypeIds.TitleBar ||
        string.Equals(Str(element, PropertyIds.AutomationId), SystemMenuBarId, StringComparison.Ordinal);

    private static string RuntimeKey(IUIAutomationElement element)
    {
        try
        {
            if (element.GetCachedPropertyValue(PropertyIds.RuntimeId) is int[] ids && ids.Length > 0)
            {
                return string.Join('.', ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            }
        }
        catch (COMException)
        {
        }

        return "anon." + Guid.NewGuid().ToString("N");
    }

    private object? Raw(IUIAutomationElement element, int propertyId)
    {
        try
        {
            // The Ex variant with ignoreDefaultValue=TRUE reports "not supported" instead of the
            // property's default (0, false, ""), which is what pattern inference relies on.
            var value = element.GetCachedPropertyValueEx(propertyId, 1);
            return ReferenceEquals(value, _notSupported) ? null : value;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private string? Str(IUIAutomationElement element, int propertyId) => Raw(element, propertyId) as string;

    private bool? Bool(IUIAutomationElement element, int propertyId) => Raw(element, propertyId) is bool b ? b : null;

    private int? Int(IUIAutomationElement element, int propertyId) => Raw(element, propertyId) switch
    {
        int i => i,
        long l => (int)l,
        _ => null,
    };

    private double? Double(IUIAutomationElement element, int propertyId) => Raw(element, propertyId) switch
    {
        double d => d,
        float f => f,
        int i => i,
        _ => null,
    };

    /// <summary>Live bounding rectangle of an element (not cached, to keep captures cheap).</summary>
    public static Rectangle LiveBounds(UiNode node)
    {
        try
        {
            var rect = node.Native?.CurrentBoundingRectangle;
            return rect is { } r && r.right > r.left && r.bottom > r.top
                ? new Rectangle(r.left, r.top, r.right - r.left, r.bottom - r.top)
                : Rectangle.Empty;
        }
        catch (COMException)
        {
            return Rectangle.Empty;
        }
    }

    /// <summary>The native window handle of a window node, read live for windows nested in their owner.</summary>
    public static nint LiveWindowHandle(UiNode window)
    {
        if (window.WindowHandle != 0)
        {
            return window.WindowHandle;
        }

        try
        {
            return window.Native?.CurrentNativeWindowHandle ?? 0;
        }
        catch (COMException)
        {
            return 0;
        }
    }
}
