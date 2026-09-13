using System.Drawing;
using System.Windows.Forms;

namespace Netwright.Fixtures.WinForms
{
    /// <summary>Modal dialog "Confirm" with lblConfirm, btnYes and btnNo.</summary>
    internal sealed class ConfirmForm : Form
    {
        public ConfirmForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            Text = "Confirm";
            Name = "ConfirmForm";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(300, 120);

            var lblConfirm = new Label
            {
                Name = "lblConfirm",
                Text = "Are you sure?",
                Location = new Point(20, 20),
                Size = new Size(260, 24),
            };

            var btnYes = new Button
            {
                Name = "btnYes",
                Text = "Yes",
                Location = new Point(100, 70),
                Size = new Size(85, 30),
                DialogResult = DialogResult.Yes,
            };

            var btnNo = new Button
            {
                Name = "btnNo",
                Text = "No",
                Location = new Point(195, 70),
                Size = new Size(85, 30),
                DialogResult = DialogResult.No,
            };

            Controls.Add(lblConfirm);
            Controls.Add(btnYes);
            Controls.Add(btnNo);

            AcceptButton = btnYes;
            CancelButton = btnNo;
        }
    }
}
