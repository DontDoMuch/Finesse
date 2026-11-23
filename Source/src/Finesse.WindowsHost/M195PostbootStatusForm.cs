using System.Drawing;
using System.Windows.Forms;
using Finesse.Compatibility;

namespace Finesse.WindowsHost;

/// <summary>
/// Shown at interactive logon by the one-shot elevated task. This is NOT an
/// installation entrypoint. The very first GPU snapshot and exact postboot
/// classification remain owned by the native postboot workflow.
/// </summary>
internal sealed class M195PostbootStatusForm : Form
{
    private readonly Label _heading = new();
    private readonly Label _detail = new();
    private readonly Button _close = new();
    private bool _complete;
    internal int? ResultCode { get; private set; }

    internal M195PostbootStatusForm()
    {
        Text = "Finesse — Driver verification";
        ClientSize = new Size(600, 305);
        MinimumSize = new Size(550, 285);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(19, 16, 29);
        ForeColor = Color.FromArgb(244, 239, 251);
        Font = new Font("Segoe UI", 10f);
        Padding = new Padding(28, 25, 28, 20);
        var logo = new Label {
            Text = "Finesse", Dock = DockStyle.Top, Height = 63,
            Font = new Font("Segoe UI Semibold", 27f, FontStyle.Bold),
            ForeColor = ForeColor
        };
        Controls.Add(logo);
        _heading.Text = "Verifying your graphics driver";
        _heading.Font = new Font("Segoe UI Semibold", 17f);
        _heading.ForeColor = ForeColor;
        _heading.Dock = DockStyle.Top;
        _heading.Height = 56;
        Controls.Add(_heading);
        _detail.Text = "Checking the driver and AMD Software after the Windows restart.";
        _detail.ForeColor = Color.FromArgb(184, 174, 208);
        _detail.Dock = DockStyle.Top;
        _detail.Height = 98;
        Controls.Add(_detail);
        _close.Text = "Close";
        _close.Enabled = false;
        _close.Size = new Size(140, 40);
        _close.Location = new Point(28, 230);
        _close.BackColor = Color.FromArgb(99, 68, 160);
        _close.ForeColor = Color.White;
        _close.FlatStyle = FlatStyle.Flat;
        _close.FlatAppearance.BorderSize = 0;
        _close.Click += (_, _) => Close();
        Controls.Add(_close);
        Controls.SetChildIndex(logo, 0);
        Controls.SetChildIndex(_heading, 1);
        Controls.SetChildIndex(_detail, 2);
        Shown += async (_, _) => await VerifyAsync();
    }

    private async Task VerifyAsync()
    {
        try
        {
            int? code = await Task.Run(() =>
                M195PostbootDispatch.TryHandleAsync(
                    [M195PostbootTicketContract.ResumeArgument]));
            ResultCode = code ?? 21;
            switch (ResultCode)
            {
                case 0:
                    _heading.Text = "Graphics driver verified";
                    _heading.ForeColor = Color.FromArgb(151, 216, 175);
                    _detail.Text = "The exact AMD driver is running without a device problem, and AMD Software is installed. Diagnostic evidence has been saved.";
                    break;
                case 20:
                    _heading.Text = "Original driver restored";
                    _heading.ForeColor = Color.FromArgb(243, 191, 139);
                    _detail.Text = "The new driver did not pass post-restart verification. Finesse confirmed the original driver was restored. This is not a successful new-driver install.";
                    break;
                default:
                    _heading.Text = "Driver verification needs attention";
                    _heading.ForeColor = Color.FromArgb(243, 191, 139);
                    _detail.Text = "Finesse could not confirm the new driver or a healthy recovery. Review the postboot diagnostic report before attempting another installation.";
                    break;
            }
        }
        catch
        {
            ResultCode = 21;
            _heading.Text = "Driver verification needs attention";
            _heading.ForeColor = Color.FromArgb(243, 191, 139);
            _detail.Text = "The postboot verification could not finish. Finesse preserves available recovery evidence without claiming the new driver succeeded.";
        }
        finally
        {
            _complete = true;
            _close.Enabled = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_complete)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }
}
