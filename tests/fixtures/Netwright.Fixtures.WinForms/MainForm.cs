using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Netwright.Fixtures.WinForms
{
    /// <summary>
    /// Main window of the WinForms Fixture App. See tests/fixtures/FIXTURE-CONTRACT.md.
    /// AutomationId == Control.Name for every control.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const int Margin8 = 12;
        private const int LabelWidth = 90;
        private const int FieldLeft = Margin8 + LabelWidth + 8;

        private readonly FixtureOptions _options;

        // Top bar
        private readonly Label _lblModalResult = new Label();
        private readonly Label _lblDiag = new Label();

        // Tabs
        private readonly TabControl _tabs = new TabControl();

        // Form tab
        private readonly TextBox _txtName = new TextBox();
        private readonly TextBox _txtEmail = new TextBox();
        private readonly ComboBox _cmbCountry = new ComboBox();
        private readonly CheckBox _chkTerms = new CheckBox();
        private readonly RadioButton _rbPlanFree = new RadioButton();
        private readonly RadioButton _rbPlanPro = new RadioButton();
        private readonly TrackBar _sldQuantity = new TrackBar();
        private readonly Button _btnSubmit = new Button();
        private readonly Label _lblResult = new Label();

        // Async tab
        private readonly Button _btnLoad = new Button();
        private readonly Label _lblStatus = new Label();
        private readonly ListBox _lstItems = new ListBox();
        private readonly Button _btnLater = new Button();
        private readonly Label _lblLater = new Label();

        // Tree tab
        private readonly Label _lblSelectedNode = new Label();

        // Keyboard tab
        private readonly Label _lblLastKey = new Label();
        private readonly Label _lblMouse = new Label();

        // Scroll tab
        private readonly Label _lblScrollClicked = new Label();

        private ToolForm? _toolForm;

        public MainForm(FixtureOptions options)
        {
            _options = options;

            SuspendLayout();

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            Name = "MainForm";
            Text = "Netwright Fixture (WinForms)";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1000, 750);

            var menu = BuildMenu();
            var topBar = BuildTopBar();

            _tabs.Name = "tabs";
            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.Add(BuildFormTab());
            _tabs.TabPages.Add(BuildAsyncTab());
            _tabs.TabPages.Add(BuildGridTab());
            _tabs.TabPages.Add(BuildTreeTab());
            _tabs.TabPages.Add(BuildKeyboardTab());
            _tabs.TabPages.Add(BuildScrollTab());

            // Docking order: controls added last are docked first.
            Controls.Add(_tabs);
            Controls.Add(topBar);
            Controls.Add(menu);
            MainMenuStrip = menu;

            SelectInitialTab();

            ResumeLayout(true);
        }

        protected override bool ShowWithoutActivation => _options.NoActivate;

        // ------------------------------------------------------------------ Top bar

        private MenuStrip BuildMenu()
        {
            var miOpenModal = new ToolStripMenuItem("Open modal...") { Name = "miOpenModal" };
            miOpenModal.Click += (s, e) => OpenModalDeferred();

            var miExit = new ToolStripMenuItem("Exit") { Name = "miExit" };
            miExit.Click += (s, e) => Close();

            var miFile = new ToolStripMenuItem("File") { Name = "miFile" };
            miFile.DropDownItems.Add(miOpenModal);
            miFile.DropDownItems.Add(miExit);

            var menu = new MenuStrip { Name = "menuMain", Dock = DockStyle.Top };
            menu.Items.Add(miFile);
            return menu;
        }

        private Panel BuildTopBar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 48 };

            int x = Margin8;
            var btnOpenModal = AddButton(bar, "btnOpenModal", "Open modal", ref x, 110);
            btnOpenModal.Click += (s, e) => OpenModalDeferred();

            ConfigureLabel(_lblModalResult, "lblModalResult", "Modal result: none", new Point(x, 14), 150);
            bar.Controls.Add(_lblModalResult);
            x += 150 + 8;

            var btnOpenWindow = AddButton(bar, "btnOpenWindow", "Open tool window", ref x, 130);
            btnOpenWindow.Click += OnOpenWindowClick;

            var btnTrace = AddButton(bar, "btnTrace", "Write diagnostics", ref x, 130);
            btnTrace.Click += OnTraceClick;

            ConfigureLabel(_lblDiag, "lblDiag", string.Empty, new Point(x, 14), 150);
            bar.Controls.Add(_lblDiag);
            x += 150 + 8;

            var btnCrash = AddButton(bar, "btnCrash", "Crash", ref x, 80);
            btnCrash.Click += OnCrashClick;

            return bar;
        }

        private static Button AddButton(Control parent, string name, string text, ref int x, int width)
        {
            var button = new Button
            {
                Name = name,
                Text = text,
                Location = new Point(x, 9),
                Size = new Size(width, 30),
            };
            parent.Controls.Add(button);
            x += width + 8;
            return button;
        }

        // Deferred to the message loop: on .NET (Core) WinForms a UIA Invoke runs the Click handler
        // synchronously inside the provider call, so a ShowDialog there blocks the automation client
        // (and an exception is swallowed into a COM error). WPF's ButtonAutomationPeer defers the same way.
        private void OpenModalDeferred()
        {
            BeginInvoke(new Action(OpenModal));
        }

        private void OpenModal()
        {
            using (var dialog = new ConfirmForm())
            {
                DialogResult result = dialog.ShowDialog(this);
                if (result == DialogResult.Yes)
                {
                    _lblModalResult.Text = "Modal result: Yes";
                }
                else if (result == DialogResult.No)
                {
                    _lblModalResult.Text = "Modal result: No";
                }
            }
        }

        private void OnOpenWindowClick(object? sender, EventArgs e)
        {
            if (_toolForm == null || _toolForm.IsDisposed)
            {
                _toolForm = new ToolForm();
                _toolForm.Show(this);
            }
            else
            {
                _toolForm.Activate();
            }
        }

        private void OnTraceClick(object? sender, EventArgs e)
        {
            Trace.WriteLine("fixture-trace: hello");
            Console.Out.WriteLine("fixture-stdout: hello");
            Console.Out.Flush();
            Console.Error.WriteLine("fixture-stderr: hello");
            _lblDiag.Text = "Diagnostics written";
        }

        private void OnCrashClick(object? sender, EventArgs e)
        {
            // Thrown from the message loop (still the UI thread, still unhandled) rather than directly in
            // Click: a UIA Invoke on .NET WinForms runs Click inside the provider call, where the exception
            // would be converted to a COM error and the process would survive.
            BeginInvoke(new Action(ThrowCrash));
        }

        private static void ThrowCrash()
        {
            throw new InvalidOperationException("Fixture crash requested");
        }

        // ------------------------------------------------------------------ Form tab

        private TabPage BuildFormTab()
        {
            var page = NewTabPage("tabForm", "Form");
            int y = Margin8 + 4;

            AddCaption(page, "Name", y);
            _txtName.Name = "txtName";
            _txtName.AccessibleName = "Name";
            _txtName.Location = new Point(FieldLeft, y);
            _txtName.Size = new Size(260, 24);
            page.Controls.Add(_txtName);
            y += 36;

            AddCaption(page, "Email", y);
            _txtEmail.Name = "txtEmail";
            _txtEmail.AccessibleName = "Email";
            _txtEmail.Location = new Point(FieldLeft, y);
            _txtEmail.Size = new Size(260, 24);
            page.Controls.Add(_txtEmail);
            y += 36;

            AddCaption(page, "Country", y);
            _cmbCountry.Name = "cmbCountry";
            _cmbCountry.AccessibleName = "Country";
            _cmbCountry.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbCountry.Items.AddRange(new object[] { "Italy", "Germany", "France", "Spain", "United States" });
            _cmbCountry.Location = new Point(FieldLeft, y);
            _cmbCountry.Size = new Size(260, 24);
            page.Controls.Add(_cmbCountry);
            y += 36;

            _chkTerms.Name = "chkTerms";
            _chkTerms.Text = "Accept terms";
            _chkTerms.Location = new Point(FieldLeft, y);
            _chkTerms.Size = new Size(200, 24);
            _chkTerms.CheckedChanged += (s, e) => _btnSubmit.Enabled = _chkTerms.Checked;
            page.Controls.Add(_chkTerms);
            y += 36;

            AddCaption(page, "Plan", y);
            _rbPlanFree.Name = "rbPlanFree";
            _rbPlanFree.Text = "Free";
            _rbPlanFree.Checked = true;
            _rbPlanFree.Location = new Point(FieldLeft, y);
            _rbPlanFree.Size = new Size(80, 24);
            page.Controls.Add(_rbPlanFree);

            _rbPlanPro.Name = "rbPlanPro";
            _rbPlanPro.Text = "Pro";
            _rbPlanPro.Location = new Point(FieldLeft + 90, y);
            _rbPlanPro.Size = new Size(80, 24);
            page.Controls.Add(_rbPlanPro);
            y += 36;

            AddCaption(page, "Quantity", y);
            _sldQuantity.Name = "sldQuantity";
            _sldQuantity.AccessibleName = "Quantity";
            _sldQuantity.Minimum = 0;
            _sldQuantity.Maximum = 10;
            _sldQuantity.Value = 1;
            _sldQuantity.TickFrequency = 1;
            _sldQuantity.Location = new Point(FieldLeft, y);
            _sldQuantity.Size = new Size(260, 45);
            page.Controls.Add(_sldQuantity);
            y += 56;

            _btnSubmit.Name = "btnSubmit";
            _btnSubmit.Text = "Submit";
            _btnSubmit.Enabled = false;
            _btnSubmit.Location = new Point(FieldLeft, y);
            _btnSubmit.Size = new Size(100, 30);
            _btnSubmit.Click += OnSubmitClick;
            page.Controls.Add(_btnSubmit);

            var btnReset = new Button
            {
                Name = "btnReset",
                Text = "Reset",
                Location = new Point(FieldLeft + 110, y),
                Size = new Size(100, 30),
            };
            btnReset.Click += OnResetClick;
            page.Controls.Add(btnReset);
            y += 44;

            ConfigureLabel(_lblResult, "lblResult", string.Empty, new Point(FieldLeft, y), 600);
            page.Controls.Add(_lblResult);

            return page;
        }

        private void OnSubmitClick(object? sender, EventArgs e)
        {
            string country = _cmbCountry.SelectedItem as string ?? string.Empty;
            string plan = _rbPlanPro.Checked ? "Pro" : "Free";
            _lblResult.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Submitted: {0} <{1}> {2} {3} x{4}",
                _txtName.Text,
                _txtEmail.Text,
                country,
                plan,
                _sldQuantity.Value);
        }

        private void OnResetClick(object? sender, EventArgs e)
        {
            _txtName.Text = string.Empty;
            _txtEmail.Text = string.Empty;
            _cmbCountry.SelectedIndex = -1;
            _chkTerms.Checked = false;
            _rbPlanFree.Checked = true;
            _sldQuantity.Value = 1;
            _lblResult.Text = string.Empty;
        }

        // ------------------------------------------------------------------ Async tab

        private TabPage BuildAsyncTab()
        {
            var page = NewTabPage("tabAsync", "Async");
            int y = Margin8 + 4;

            _btnLoad.Name = "btnLoad";
            _btnLoad.Text = "Load data";
            _btnLoad.Location = new Point(Margin8, y);
            _btnLoad.Size = new Size(110, 30);
            _btnLoad.Click += OnLoadClick;
            page.Controls.Add(_btnLoad);

            ConfigureLabel(_lblStatus, "lblStatus", "Idle", new Point(Margin8 + 120, y + 6), 250);
            page.Controls.Add(_lblStatus);
            y += 40;

            _lstItems.Name = "lstItems";
            _lstItems.AccessibleName = "Items";
            _lstItems.Location = new Point(Margin8, y);
            _lstItems.Size = new Size(260, 120);
            page.Controls.Add(_lstItems);
            y += 136;

            var btnEnableLater = new Button
            {
                Name = "btnEnableLater",
                Text = "Enable later",
                Location = new Point(Margin8, y),
                Size = new Size(110, 30),
            };
            btnEnableLater.Click += OnEnableLaterClick;
            page.Controls.Add(btnEnableLater);

            _btnLater.Name = "btnLater";
            _btnLater.Text = "Later";
            _btnLater.Enabled = false;
            _btnLater.Location = new Point(Margin8 + 120, y);
            _btnLater.Size = new Size(110, 30);
            _btnLater.Click += (s, e) => _lblLater.Text = "Later clicked";
            page.Controls.Add(_btnLater);

            ConfigureLabel(_lblLater, "lblLater", string.Empty, new Point(Margin8 + 240, y + 6), 250);
            page.Controls.Add(_lblLater);

            return page;
        }

        private async void OnLoadClick(object? sender, EventArgs e)
        {
            _btnLoad.Enabled = false;
            _lblStatus.Text = "Loading...";

            await Task.Delay(1500);

            _lstItems.BeginUpdate();
            _lstItems.Items.Clear();
            _lstItems.Items.AddRange(new object[] { "Alpha", "Beta", "Gamma" });
            _lstItems.EndUpdate();

            _lblStatus.Text = "Loaded 3 items";
            _btnLoad.Enabled = true;
        }

        private async void OnEnableLaterClick(object? sender, EventArgs e)
        {
            await Task.Delay(1000);
            _btnLater.Enabled = true;
        }

        // ------------------------------------------------------------------ Grid tab

        private static TabPage BuildGridTab()
        {
            var page = NewTabPage("tabGrid", "Grid");

            var grid = new DataGridView
            {
                Name = "gridOrders",
                AccessibleName = "Orders",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Id", Width = 100 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Customer", HeaderText = "Customer", Width = 200 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "Amount", Width = 120 });

            var rows = new DataGridViewRow[1000];
            for (int i = 0; i < rows.Length; i++)
            {
                int id = i + 1;
                var row = new DataGridViewRow();
                row.CreateCells(
                    grid,
                    id.ToString(CultureInfo.InvariantCulture),
                    "Customer " + id.ToString(CultureInfo.InvariantCulture),
                    (id * 10).ToString("0.00", CultureInfo.InvariantCulture));
                rows[i] = row;
            }

            grid.Rows.AddRange(rows);
            page.Controls.Add(grid);
            return page;
        }

        // ------------------------------------------------------------------ Tree tab

        private TabPage BuildTreeTab()
        {
            var page = NewTabPage("tabTree", "Tree");

            var tree = new NoAutoSelectTreeView
            {
                Name = "treeFolders",
                AccessibleName = "Folders",
                Location = new Point(Margin8, Margin8 + 4),
                Size = new Size(300, 300),
                HideSelection = false,
            };

            var documents = new TreeNode("Documents", new[] { new TreeNode("Invoices"), new TreeNode("Reports") });
            var root = new TreeNode("Root", new[] { documents, new TreeNode("Pictures") });
            tree.Nodes.Add(root);
            tree.CollapseAll();
            tree.AfterSelect += (s, e) =>
            {
                if (e.Node != null)
                {
                    _lblSelectedNode.Text = "Selected: " + e.Node.Text;
                }
            };
            page.Controls.Add(tree);

            ConfigureLabel(_lblSelectedNode, "lblSelectedNode", string.Empty, new Point(Margin8 + 320, Margin8 + 4), 300);
            page.Controls.Add(_lblSelectedNode);

            return page;
        }

        // ------------------------------------------------------------------ Keyboard tab

        private TabPage BuildKeyboardTab()
        {
            var page = NewTabPage("tabKeyboard", "Keyboard");
            int y = Margin8 + 4;

            AddCaption(page, "Keys", y);
            var txtKeys = new TextBox
            {
                Name = "txtKeys",
                AccessibleName = "Keys",
                Location = new Point(FieldLeft, y),
                Size = new Size(260, 24),
            };
            txtKeys.KeyDown += (s, e) => _lblLastKey.Text = "Last key: " + e.KeyCode.ToString();
            page.Controls.Add(txtKeys);

            ConfigureLabel(_lblLastKey, "lblLastKey", "Last key: none", new Point(FieldLeft + 280, y + 3), 250);
            page.Controls.Add(_lblLastKey);
            y += 48;

            var pnlMouse = new Panel
            {
                Name = "pnlMouse",
                AccessibleName = "Mouse target",
                Location = new Point(FieldLeft, y),
                Size = new Size(200, 100),
                BackColor = Color.SteelBlue,
                BorderStyle = BorderStyle.FixedSingle,
            };
            pnlMouse.MouseClick += OnMouseTargetClick;
            pnlMouse.MouseDoubleClick += OnMouseTargetDoubleClick;
            page.Controls.Add(pnlMouse);

            ConfigureLabel(_lblMouse, "lblMouse", "Mouse: none", new Point(FieldLeft + 220, y + 40), 250);
            page.Controls.Add(_lblMouse);

            return page;
        }

        private void OnMouseTargetClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _lblMouse.Text = "Mouse: left";
            }
            else if (e.Button == MouseButtons.Right)
            {
                _lblMouse.Text = "Mouse: right";
            }
        }

        private void OnMouseTargetDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _lblMouse.Text = "Mouse: double";
            }
        }

        // ------------------------------------------------------------------ Scroll tab

        private TabPage BuildScrollTab()
        {
            var page = NewTabPage("tabScroll", "Scroll");

            var scrLong = new Panel
            {
                Name = "scrLong",
                AccessibleName = "Long list",
                Location = new Point(Margin8, Margin8 + 4),
                Size = new Size(260, 400),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };

            scrLong.SuspendLayout();
            for (int i = 1; i <= 100; i++)
            {
                int n = i;
                var item = new Button
                {
                    Name = "btnItem" + n.ToString("D3", CultureInfo.InvariantCulture),
                    Text = "Item " + n.ToString(CultureInfo.InvariantCulture),
                    Location = new Point(8, 8 + (i - 1) * 34),
                    Size = new Size(200, 30),
                };
                item.Click += (s, e) => _lblScrollClicked.Text = "Clicked Item " + n.ToString(CultureInfo.InvariantCulture);
                scrLong.Controls.Add(item);
            }

            scrLong.ResumeLayout(false);
            page.Controls.Add(scrLong);

            ConfigureLabel(_lblScrollClicked, "lblScrollClicked", string.Empty, new Point(Margin8 + 280, Margin8 + 4), 250);
            page.Controls.Add(_lblScrollClicked);

            return page;
        }

        // ------------------------------------------------------------------ Helpers

        private static TabPage NewTabPage(string name, string text)
        {
            return new TabPage(text) { Name = name, UseVisualStyleBackColor = true };
        }

        private static void AddCaption(Control parent, string text, int y)
        {
            // Visual caption only; unnamed so it does not pollute the AutomationId space.
            parent.Controls.Add(new Label
            {
                Text = text,
                Location = new Point(Margin8, y + 3),
                Size = new Size(LabelWidth, 20),
            });
        }

        private static void ConfigureLabel(Label label, string name, string text, Point location, int width)
        {
            label.Name = name;
            label.Text = text;
            label.AutoSize = false;
            label.Location = location;
            label.Size = new Size(width, 20);
        }

        /// <summary>
        /// The native TreeView selects its first node when it receives focus with no selection
        /// (e.g. when its tab page is shown). That would set lblSelectedNode without any user
        /// action, so focus-driven selection is cancelled; explicit selections still go through.
        /// </summary>
        private sealed class NoAutoSelectTreeView : TreeView
        {
            private const int WmSetFocus = 0x0007;
            private bool _inSetFocus;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmSetFocus && SelectedNode == null)
                {
                    _inSetFocus = true;
                    try
                    {
                        base.WndProc(ref m);
                    }
                    finally
                    {
                        _inSetFocus = false;
                    }

                    return;
                }

                base.WndProc(ref m);
            }

            protected override void OnBeforeSelect(TreeViewCancelEventArgs e)
            {
                if (_inSetFocus && e.Action == TreeViewAction.Unknown)
                {
                    e.Cancel = true;
                    return;
                }

                base.OnBeforeSelect(e);
            }
        }

        private void SelectInitialTab()
        {
            if (string.IsNullOrEmpty(_options.InitialTab))
            {
                return;
            }

            foreach (TabPage page in _tabs.TabPages)
            {
                if (string.Equals(page.Text, _options.InitialTab, StringComparison.OrdinalIgnoreCase))
                {
                    _tabs.SelectedTab = page;
                    return;
                }
            }
        }
    }
}
