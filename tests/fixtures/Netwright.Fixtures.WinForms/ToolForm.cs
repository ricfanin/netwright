using System;
using System.Drawing;
using System.Windows.Forms;

namespace Netwright.Fixtures.WinForms
{
    /// <summary>Non-modal window "Tool Window" with txtToolNote and btnToolClose.</summary>
    internal sealed class ToolForm : Form
    {
        public ToolForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            Text = "Tool Window";
            Name = "ToolForm";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(320, 120);

            var txtToolNote = new TextBox
            {
                Name = "txtToolNote",
                AccessibleName = "Note",
                Location = new Point(20, 20),
                Size = new Size(280, 24),
            };

            var btnToolClose = new Button
            {
                Name = "btnToolClose",
                Text = "Close",
                Location = new Point(215, 70),
                Size = new Size(85, 30),
            };
            btnToolClose.Click += OnCloseClick;

            Controls.Add(txtToolNote);
            Controls.Add(btnToolClose);
        }

        private void OnCloseClick(object? sender, EventArgs e)
        {
            Close();
        }
    }
}
