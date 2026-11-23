using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Finesse.Compatibility;

namespace Finesse.WindowsHost;

/// <summary>
/// Single-button user-facing Windows 11 installation host. Technical milestone
/// names and native logs are never displayed in its progress area. This host
/// is draft-only until full physical device validation and signing.
/// </summary>
internal sealed class M195InstallForm : Form
{
    private static readonly Color Back = Color.FromArgb(19, 16, 29);
    private static readonly Color Surface = Color.FromArgb(33, 27, 48);
    private static readonly Color Secondary = Color.FromArgb(184, 174, 208);
    private static readonly Color Accent = Color.FromArgb(167, 133, 227);
    private static readonly Color Bright = Color.FromArgb(244, 239, 251);
    private static readonly Color Good = Color.FromArgb(151, 216, 175);
    private static readonly Color Warn = Color.FromArgb(243, 191, 139);
    private readonly Button _action = new();
    private readonly Label _status = new();
    private readonly Label _headline = new();
    private readonly Label _device = new();
    private readonly Label _percent = new();
    private readonly ProgressBar _progress = new();
    private readonly Label[] _rowLabels = new Label[12];
    private readonly string[] _stepTitles = [
        "Identify handheld", "Inspect graphics driver", "Preserve current driver",
        "Download official AMD driver", "Verify original AMD files",
        "Prepare handheld driver", "Generate driver catalog",
        "Sign and install graphics driver", "Verify pre-restart driver",
        "Install AMD Software", "Prepare one restart", "Verify after restart"
    ];
    private bool _busy;
    private string? _sessionRoot;

    internal int ResultCode { get; private set; }

    internal M195InstallForm()
    {
        Text = "Finesse — AMD Graphics Driver";
        ClientSize = new Size(900, 665);
        MinimumSize = new Size(770, 580);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        BackColor = Back;
        ForeColor = Bright;

        var root = new Panel {
            Dock = DockStyle.Fill, Padding = new Padding(26, 22, 26, 19),
            BackColor = Back
        };
        Controls.Add(root);
        var heading = new Label {
            Text = "Finesse", Dock = DockStyle.Top, Height = 63,
            Font = new Font("Segoe UI Semibold", 29f, FontStyle.Bold),
            ForeColor = Bright
        };
        root.Controls.Add(heading);
        var sub = new Label {
            Text = "AMD Adrenalin 26.11.1  •  Legion Go 1, Go S and Go 2",
            Dock = DockStyle.Top, Height = 35,
            ForeColor = Secondary, Font = new Font("Segoe UI", 10f)
        };
        root.Controls.Add(sub);
        // Reverse docking order is not assumed: use explicit child indices.
        root.Controls.SetChildIndex(heading, 0);
        root.Controls.SetChildIndex(sub, 1);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 75 };
        root.Controls.Add(footer);
        _action.Text = "Install AMD Driver";
        _action.Size = new Size(235, 47);
        _action.Location = new Point(0, 10);
        _action.FlatStyle = FlatStyle.Flat;
        _action.FlatAppearance.BorderSize = 0;
        _action.BackColor = Color.FromArgb(99, 68, 160);
        _action.ForeColor = Color.White;
        _action.Cursor = Cursors.Hand;
        _action.Font = new Font("Segoe UI Semibold", 10.5f);
        _action.Click += async (_, _) => await InstallAsync();
        footer.Controls.Add(_action);
        var footerCopy = new Label {
            Text = "One verified driver installation · one planned restart",
            Location = new Point(255, 12),
            Size = new Size(510, 42),
            ForeColor = Secondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        footer.Controls.Add(footerCopy);

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2,
            RowCount = 1, BackColor = Back,
            Padding = new Padding(0, 13, 0, 9)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(layout);
        root.Controls.SetChildIndex(layout, 2);

        var left = new Panel {
            Dock = DockStyle.Fill, BackColor = Surface,
            Padding = new Padding(17, 15, 15, 15),
            Margin = new Padding(0, 0, 9, 0)
        };
        layout.Controls.Add(left, 0, 0);
        var leftHeader = new Label {
            Text = "INSTALLATION STEPS", Dock = DockStyle.Top,
            Height = 32, ForeColor = Accent,
            Font = new Font("Segoe UI Semibold", 9f)
        };
        left.Controls.Add(leftHeader);
        var steps = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 12,
            AutoScroll = true, Margin = Padding.Empty
        };
        steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 12; i++)
        {
            steps.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            var line = new Label {
                Text = "○  " + _stepTitles[i], Dock = DockStyle.Fill,
                ForeColor = Secondary, TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true, Margin = new Padding(1, 2, 1, 0),
                Font = new Font("Segoe UI", 9f)
            };
            _rowLabels[i] = line;
            steps.Controls.Add(line, 0, i);
        }
        left.Controls.Add(steps);
        left.Controls.SetChildIndex(leftHeader, 0);

        var right = new Panel {
            Dock = DockStyle.Fill, BackColor = Surface,
            Padding = new Padding(22, 21, 22, 21),
            Margin = new Padding(9, 0, 0, 0)
        };
        layout.Controls.Add(right, 1, 0);
        var deviceHeading = new Label {
            Text = "DETECTED DEVICE", Dock = DockStyle.Top,
            Height = 31, ForeColor = Accent,
            Font = new Font("Segoe UI Semibold", 9f)
        };
        right.Controls.Add(deviceHeading);
        _device.Text = "Checked when installation begins";
        _device.ForeColor = Bright;
        _device.Font = new Font("Segoe UI Semibold", 13f);
        _device.AutoEllipsis = true;
        _device.Dock = DockStyle.Top;
        _device.Height = 63;
        right.Controls.Add(_device);
        _headline.Text = "Ready to install";
        _headline.Dock = DockStyle.Top;
        _headline.Height = 54;
        _headline.ForeColor = Bright;
        _headline.Font = new Font("Segoe UI Semibold", 18f);
        right.Controls.Add(_headline);
        _status.Text = "Finesse will verify the AMD files, preserve your current driver, and install AMD Software before restarting.";
        _status.ForeColor = Secondary;
        _status.AutoSize = false;
        _status.Dock = DockStyle.Top;
        _status.Height = 135;
        right.Controls.Add(_status);
        _percent.Text = "0%";
        _percent.ForeColor = Accent;
        _percent.Dock = DockStyle.Top;
        _percent.Height = 29;
        _percent.TextAlign = ContentAlignment.MiddleRight;
        right.Controls.Add(_percent);
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        _progress.Value = 0;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Dock = DockStyle.Top;
        _progress.Height = 20;
        right.Controls.Add(_progress);
        // Left-to-right docking stack order.
        right.Controls.SetChildIndex(deviceHeading, 0);
        right.Controls.SetChildIndex(_device, 1);
        right.Controls.SetChildIndex(_headline, 2);
        right.Controls.SetChildIndex(_status, 3);
        right.Controls.SetChildIndex(_percent, 4);
        right.Controls.SetChildIndex(_progress, 5);
    }

    private void UpdateStep(M195LifecycleSequence.Step step, string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateStep(step, message)));
            return;
        }
        int selected = (int)step - 1;
        for (int i = 0; i < _rowLabels.Length; i++)
        {
            _rowLabels[i].Text = (i < selected ? "✓  " :
                i == selected ? "●  " : "○  ") + _stepTitles[i];
            _rowLabels[i].ForeColor = i < selected ? Good :
                i == selected ? Bright : Secondary;
        }
        _headline.Text = _stepTitles[selected];
        _status.Text = message;
        // The last 9% belongs ONLY to the real postboot verification.
        int value = Math.Min(91, selected * 8 + 2);
        _progress.Value = value;
        _percent.Text = value + "%";
    }

    private void UpdateTool(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateTool(text)));
            return;
        }
        _status.Text = text;
    }

    private async Task InstallAsync()
    {
        if (_busy) return;
        _busy = true;
        _action.Enabled = false;
        _action.Text = "Installing…";
        _headline.Text = "Verifying prerequisites";
        _status.Text = "Checking the exact handheld model and trusted driver tools.";
        ResultCode = 1;
        _sessionRoot = Program.NewSessionRoot();
        try
        {
            var toolsProgress = new Progress<string>(UpdateTool);
            // Native Windows driver operations, MSI and task registration run
            // off the UI thread, using one already-elevated process.
            M195LifecycleSequence.PendingPostboot pending =
                await Task.Run(async () => {
                    M195WindowsFullLifecycleActions actions =
                        await Program.CreateActionsAsync(
                            _sessionRoot, toolsProgress, CancellationToken.None);
                    return await M195LifecycleSequence.RunPrebootAsync(
                        actions, UpdateStep, CancellationToken.None);
                });
            ResultCode = 0;
            _headline.Text = "Restart scheduled";
            _status.Text = "AMD Software is installed. Windows will restart once. Finesse will verify the graphics driver after you sign in.";
            _progress.Value = 91;
            _percent.Text = "91%";
            _action.Text = "Restart scheduled";
            _rowLabels[10].Text = "✓  " + _stepTitles[10];
            _rowLabels[10].ForeColor = Good;
            _rowLabels[11].Text = "●  " + _stepTitles[11];
            _rowLabels[11].ForeColor = Bright;
            _device.Text = pending.ExactHardwareId;
        }
        catch (Exception ex)
        {
            ResultCode = 1;
            _headline.Text = "Installation could not finish";
            _headline.ForeColor = Warn;
            _status.Text = "Finesse stopped because a required verification did not complete. The original driver recovery and diagnostic results are recorded where available.";
            _action.Text = "Installation stopped";
            string? destination = _sessionRoot;
            if (!string.IsNullOrWhiteSpace(destination))
            {
                try
                {
                    Directory.CreateDirectory(destination);
                    string receipt = Path.Combine(destination,
                        "M195-Host-InstallationFailure.json");
                    await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(
                        new {
                            Schema = "Finesse.M195.HostFailure.v1",
                            TimestampUtc = DateTimeOffset.UtcNow,
                            Exception = ex.ToString(),
                            LiveInstallSucceeded = false,
                            RestartConfirmed = false
                        }, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { /* retain on-screen failure without inventing receipt */ }
            }
        }
        finally
        {
            _busy = false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }
}
