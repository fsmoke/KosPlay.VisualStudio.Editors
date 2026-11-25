using System.Drawing;
using System.Windows.Forms;

namespace KosPlay.VisualStudio.Editors
{
    internal sealed class AboutBox : Form
    {
        internal AboutBox()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(360, 160);
            Padding = new Padding(5);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Name = "AboutBox";
            Text = "About";

            var informationLabel = new Label
            {
                Name = "informationLabel",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Command Editor\r\nCommand and macro editor\r\nVersion " +
                    typeof(AboutBox).Assembly.GetName().Version
            };
            var buttonBox = new FlowLayoutPanel
            {
                Name = "aboutButtonBox",
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(5)
            };
            var closeButton = new Button
            {
                Name = "closeButton",
                Text = "OK",
                AutoSize = true,
                MinimumSize = new Size(95, 28),
                DialogResult = DialogResult.OK
            };
            buttonBox.Controls.Add(closeButton);
            AcceptButton = closeButton;
            CancelButton = closeButton;
            Controls.Add(informationLabel);
            Controls.Add(buttonBox);
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
