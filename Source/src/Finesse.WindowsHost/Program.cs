using System.Security.Principal;
using System.Windows.Forms;
using Finesse.Compatibility;

namespace Finesse.WindowsHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Finesse supports Windows only.");
        if (args.Length > 1 ||
            (args.Length == 1 &&
             !M195PostbootDispatch.IsExactResumeCommand(args)))
            return 2;

        // app.manifest requests elevation once at process launch. The same
        // elevated process handles all 11 preboot steps. The postboot Task
        // Scheduler launch uses highest InteractiveToken without another
        // installer self-relaunch or UAC request.
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(
                WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("Administrator approval is required to install the driver.",
                "Finesse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 5;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        if (args.Length == 1)
        {
            // Mandatory earliest executable dispatch: never launch the
            // normal GUI or start a new installation for this argument.
            using var postboot = new M195PostbootStatusForm();
            Application.Run(postboot);
            return postboot.ResultCode ?? 21;
        }

        using var gui = new M195InstallForm();
        Application.Run(gui);
        return gui.ResultCode;
    }

    internal static string ReadOriginal194Manifest()
    {
        const string resource = "Finesse.M195.Original194Manifest";
        using Stream stream = typeof(Program).Assembly.GetManifestResourceStream(
            resource) ?? throw new InvalidDataException(
                "The original 194-file AMD manifest is missing from this Finesse build.");
        using var reader = new StreamReader(stream);
        string original = reader.ReadToEnd();
        // The original inventory validates every source SHA before any
        // signing or driver binding. The EXE contains only SHA manifests,
        // never AMD original INF/DAT/SYS/DLL/CAT bytes.
        _ = Amd26111SourceInventory.ParsePinned26111(original);
        return original;
    }

    internal static async Task<M195WindowsFullLifecycleActions> CreateActionsAsync(
        string sessionRoot, IProgress<string>? toolProgress,
        IProgress<string>? identifiedDevice, CancellationToken token)
    {
        // The exact physical display adapter is read-only inspected before
        // composing its corresponding frozen 26/30/28 policy; never use a
        // manual profile selector or a wildcard hardware match.
        var gpu = M195WindowsGpuInventory.RequireUniqueExactGpu(
            await M195WindowsGpuInventory.EnumerateAsync(token));
        identifiedDevice?.Report(gpu.Profile.Id switch
        {
            "LegionGo1" => "Lenovo Legion Go 1",
            "LegionGoS-Z1Extreme" => "Lenovo Legion Go S — Z1 Extreme",
            "LegionGo2-Z2Extreme" => "Lenovo Legion Go 2 — Z2 Extreme",
            _ => gpu.Profile.Id
        });
        string policy = M195FrozenProfileRegistryPolicies.RequireAuthorized(
            gpu.Profile);
        M195PinnedToolBootstrap.Tools tools =
            await M195PinnedToolBootstrap.EnsureAsync(
                cacheRoot: null, toolProgress, token);

        string workspace = Path.GetFullPath(sessionRoot);
        Directory.CreateDirectory(workspace);
        var preparer = new M195WindowsPreparationActions.Settings(
            Path.Combine(workspace, "OriginalDriverBackup"),
            Path.Combine(workspace, "OfficialAmdDownload"),
            Path.Combine(workspace, "OriginalAmdExtraction"),
            Path.Combine(workspace, "UnsignedCandidate"),
            tools.SevenZrExe,
            tools.Inf2CatExe,
            ReadOriginal194Manifest(),
            policy,
            LocalOfficialAmdInstaller: null,
            FreshPostbootEvidenceRoot:
                Path.Combine(workspace, "PostbootRegistration"));

        var settings = new M195WindowsFullLifecycleActions.Settings(
            preparer, tools.SignToolExe,
            tools.Portable7ZipReleaseExe,
            Path.Combine(workspace, "SoftwareExtraction"),
            Path.Combine(workspace, "SoftwareInstallEvidence"),
            Path.Combine(workspace, "PrebootRecoveryEvidence"),
            Path.Combine(workspace, "ScopedCleanupEvidence"));
        return new M195WindowsFullLifecycleActions(settings);
    }

    internal static string NewSessionRoot() =>
        Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData),
            "Finesse", "Sessions",
            "M195-" + Guid.NewGuid().ToString("N"));
}
