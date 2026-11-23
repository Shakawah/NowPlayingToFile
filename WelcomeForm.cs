
using System;
using System.Drawing;
using System.Windows.Forms;

public class WelcomeForm : Form
{
    private readonly CheckBox dontShowAgain;

    public bool DontShowAgain => dontShowAgain.Checked;

    public WelcomeForm()
    {
        Text = "Welcome to NowPlayingToFile";
        Size = new Size(450, 200);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        var message = new Label
        {
            Text = "Now Playing To File is running in the background.\n" +
                "You can find its icon in the notification area.\n" +
                "Right-click the icon to show the ReadMe, Exit the app and more.",
            Location = new Point(20, 20),
            Size = new Size(390, 80),
        };

        dontShowAgain = new CheckBox
        {
            Text = "Don't show this again",
            Location = new Point(20, 120),
            AutoSize = true
        };

        var okButton = new Button
        {
            Text = "OK",
            Location = new Point(335, 115),
            Size = new Size(75, 30),
        };
        okButton.Click += (_, _) => Close();

        Controls.Add(message);
        Controls.Add(dontShowAgain);
        Controls.Add(okButton);
        AcceptButton = okButton;
    }
}
