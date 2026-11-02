using DawnResourceExtractionTool.Core;
using DawnResourceExtractionTool.Core.Localization;

namespace DawnResourceExtractionTool.App.ViewModels;

/// <summary>
/// One file set the tool fetches once and then needs: how to tell whether it is already here, and
/// how to download it. The loading screen walks the list before the main window opens.
/// </summary>
public sealed record DownloadableResource(string Name, Func<bool> IsReady, Func<Task> Fetch)
{
    public bool Ready => IsReady();
}

/// <summary>
/// The startup resource check. Every feature that needs a downloaded file adds one line to
/// <see cref="BuildResourceList"/>: the loading screen then checks it, downloads it when missing and
/// shows the progress, without any other code having to know about it.
/// </summary>
public sealed partial class MainViewModel
{
    private List<DownloadableResource>? _resources;

    /// <summary>Everything the loading screen checks, in download order.</summary>
    private List<DownloadableResource> BuildResourceList() => new()
    {
        new DownloadableResource(Strings.T("Resource.Hashes"), () => !NeedsHashTable, FetchHashesAsync),
        new DownloadableResource(Strings.T("Resource.AudioTool"), () => VgmstreamTool.IsAvailable,
            () => VgmstreamTool.EnsureAsync(new Progress<string>(message => StatusText = message), Token)),
        new DownloadableResource(Strings.T("Resource.SpeechModel"), () => SpeechRecognizer.IsModelPresent,
            () => SpeechRecognizer.EnsureModelAsync(new Progress<string>(message => StatusText = message), Token)),
    };

    /// <summary>The list itself, for the log, the tool tip and the self test.</summary>
    public IReadOnlyList<DownloadableResource> Resources => _resources ??= BuildResourceList();

    /// <summary>"哈希表与名称索引 ✓ · 音频解码工具（vgmstream） ✗": what is here and what is not.</summary>
    public string ResourceSummary =>
        string.Join(" · ", Resources.Select(resource => resource.Name + (resource.Ready ? " ✓" : " ✗")));

    public bool AllResourcesReady => Resources.All(resource => resource.Ready);

    private CancellationToken Token => _cancellation?.Token ?? CancellationToken.None;

    /// <summary>
    /// Startup check, run while the loading screen is up: downloads whatever is missing, one item at
    /// a time. Each item belongs to an optional feature, so a failure is only logged (the toolbar
    /// buttons still download on demand) and the check moves on to the next one.
    /// </summary>
    public async Task<bool> EnsureAllResourcesAsync()
    {
        // Refresh first: the flags behind the list are set by InitializeAsync / the audio page.
        Log(Strings.T("Resource.Check.Log", ResourceSummary));

        foreach (var resource in Resources.Where(resource => !resource.Ready).ToList())
        {
            Log(Strings.T("Resource.Check.Download", resource.Name));
            await RunBusyAsync(Strings.T("Resource.Check.Busy", resource.Name), resource.Fetch, showErrors: false);
            Log(resource.Ready
                ? Strings.T("Resource.Check.Ready", resource.Name)
                : Strings.T("Resource.Check.NotReady", resource.Name, StatusText));
        }

        RefreshAudioToolState();
        if (!AllResourcesReady) return false;

        StatusText = Strings.T("Resource.Check.StatusReady");
        Log(Strings.T("Resource.Check.AllReady"));
        return true;
    }
}
