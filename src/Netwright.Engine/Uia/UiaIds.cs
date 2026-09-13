namespace Netwright.Engine.Uia;

/// <summary>
/// UI Automation property identifiers (UIA_*PropertyId). These values are part of the stable Windows ABI.
/// </summary>
internal static class PropertyIds
{
    public const int RuntimeId = 30000;
    public const int BoundingRectangle = 30001;
    public const int ProcessId = 30002;
    public const int ControlType = 30003;
    public const int Name = 30005;
    public const int HasKeyboardFocus = 30008;
    public const int IsKeyboardFocusable = 30009;
    public const int IsEnabled = 30010;
    public const int AutomationId = 30011;
    public const int ClassName = 30012;
    public const int HelpText = 30013;
    public const int IsPassword = 30019;
    public const int NativeWindowHandle = 30020;
    public const int IsOffscreen = 30022;
    public const int FrameworkId = 30024;

    public const int IsExpandCollapsePatternAvailable = 30028;
    public const int IsGridPatternAvailable = 30030;
    public const int IsInvokePatternAvailable = 30031;
    public const int IsRangeValuePatternAvailable = 30033;
    public const int IsScrollPatternAvailable = 30034;
    public const int IsScrollItemPatternAvailable = 30035;
    public const int IsSelectionItemPatternAvailable = 30036;
    public const int IsSelectionPatternAvailable = 30037;
    public const int IsTogglePatternAvailable = 30041;
    public const int IsValuePatternAvailable = 30043;
    public const int IsWindowPatternAvailable = 30044;

    public const int ValueValue = 30045;
    public const int ValueIsReadOnly = 30046;
    public const int RangeValueValue = 30047;
    public const int RangeValueMinimum = 30049;
    public const int RangeValueMaximum = 30050;
    public const int ScrollHorizontallyScrollable = 30057;
    public const int ScrollVerticallyScrollable = 30058;
    public const int GridRowCount = 30062;
    public const int GridColumnCount = 30063;
    public const int ExpandCollapseState = 30070;
    public const int WindowIsModal = 30077;
    public const int SelectionItemIsSelected = 30079;
    public const int ToggleToggleState = 30086;
    public const int IsLegacyIAccessiblePatternAvailable = 30090;
    public const int LegacyIAccessibleDefaultAction = 30100;

    /// <summary>
    /// Properties of a full capture, with the programmatic name UI Automation reports for each (checked
    /// by an integration test). Every extra property costs one provider call per element, so this list
    /// is deliberately short; availability of most patterns is inferred from their state properties.
    /// </summary>
    public static readonly (int Id, string ProgrammaticName)[] FullSet =
    [
        (RuntimeId, "RuntimeId"),
        (ControlType, "ControlType"),
        (Name, "Name"),
        (AutomationId, "AutomationId"),
        (IsEnabled, "IsEnabled"),
        (IsOffscreen, "IsOffscreen"),
        (HasKeyboardFocus, "HasKeyboardFocus"),
        (IsPassword, "IsPassword"),
        (IsInvokePatternAvailable, "IsInvokePatternAvailable"),
        (IsScrollItemPatternAvailable, "IsScrollItemPatternAvailable"),
        (IsLegacyIAccessiblePatternAvailable, "IsLegacyIAccessiblePatternAvailable"),
        (ValueValue, "ValuePattern.Value"),
        (ValueIsReadOnly, "ValuePattern.IsReadOnly"),
        (RangeValueValue, "RangeValuePattern.Value"),
        (ScrollVerticallyScrollable, "ScrollPattern.VerticallyScrollable"),
        (GridRowCount, "GridPattern.RowCount"),
        (ExpandCollapseState, "ExpandCollapsePattern.ExpandCollapseState"),
        (WindowIsModal, "WindowPattern.IsModal"),
        (SelectionItemIsSelected, "SelectionItemPattern.IsSelected"),
        (ToggleToggleState, "TogglePattern.ToggleState"),
    ];

    /// <summary>Properties compared by <see cref="Model.UiTree.Fingerprint"/>; enough to detect that the UI Settled.</summary>
    public static readonly (int Id, string ProgrammaticName)[] LightSet =
    [
        (RuntimeId, "RuntimeId"),
        (ControlType, "ControlType"),
        (Name, "Name"),
        (AutomationId, "AutomationId"),
        (IsEnabled, "IsEnabled"),
        (IsOffscreen, "IsOffscreen"),
        (HasKeyboardFocus, "HasKeyboardFocus"),
        (ValueValue, "ValuePattern.Value"),
        (RangeValueValue, "RangeValuePattern.Value"),
        (ExpandCollapseState, "ExpandCollapsePattern.ExpandCollapseState"),
        (SelectionItemIsSelected, "SelectionItemPattern.IsSelected"),
        (ToggleToggleState, "TogglePattern.ToggleState"),
    ];
}

/// <summary>UI Automation control pattern identifiers (UIA_*PatternId).</summary>
internal static class PatternIds
{
    public const int Invoke = 10000;
    public const int Selection = 10001;
    public const int Value = 10002;
    public const int RangeValue = 10003;
    public const int Scroll = 10004;
    public const int ExpandCollapse = 10005;
    public const int Grid = 10006;
    public const int Window = 10009;
    public const int SelectionItem = 10010;
    public const int Toggle = 10015;
    public const int ScrollItem = 10017;
    public const int LegacyIAccessible = 10018;
    public const int ItemContainer = 10019;
    public const int VirtualizedItem = 10020;
}

/// <summary>UI Automation control type identifiers (UIA_*ControlTypeId).</summary>
internal static class ControlTypeIds
{
    public const int Button = 50000;
    public const int Calendar = 50001;
    public const int CheckBox = 50002;
    public const int ComboBox = 50003;
    public const int Edit = 50004;
    public const int Hyperlink = 50005;
    public const int Image = 50006;
    public const int ListItem = 50007;
    public const int List = 50008;
    public const int Menu = 50009;
    public const int MenuBar = 50010;
    public const int MenuItem = 50011;
    public const int ProgressBar = 50012;
    public const int RadioButton = 50013;
    public const int ScrollBar = 50014;
    public const int Slider = 50015;
    public const int Spinner = 50016;
    public const int StatusBar = 50017;
    public const int Tab = 50018;
    public const int TabItem = 50019;
    public const int Text = 50020;
    public const int ToolBar = 50021;
    public const int ToolTip = 50022;
    public const int Tree = 50023;
    public const int TreeItem = 50024;
    public const int Custom = 50025;
    public const int Group = 50026;
    public const int Thumb = 50027;
    public const int DataGrid = 50028;
    public const int DataItem = 50029;
    public const int Document = 50030;
    public const int SplitButton = 50031;
    public const int Window = 50032;
    public const int Pane = 50033;
    public const int Header = 50034;
    public const int HeaderItem = 50035;
    public const int Table = 50036;
    public const int TitleBar = 50037;
    public const int Separator = 50038;
    public const int SemanticZoom = 50039;
    public const int AppBar = 50040;
}
