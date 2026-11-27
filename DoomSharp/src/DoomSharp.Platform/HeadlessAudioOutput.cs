namespace DoomSharp.Platform;

/// <summary>
/// Headless audio output — records every submitted buffer for deterministic
/// unit tests (the audio analogue of <see cref="HeadlessFramePresenter"/>).
/// </summary>
public sealed class HeadlessAudioOutput : IAudioOutput
{
    /// <summary>All submitted buffers, copied at Submit time.</summary>
    public System.Collections.Generic.List<short[]> Frames { get; } = [];

    /// <summary>Number of Submit calls.</summary>
    public int SubmitCount => Frames.Count;

    /// <summary>Always false: the headless output is not a live device.</summary>
    public bool IsLive => false;

    public void Submit(ReadOnlySpan<short> stereoPcm)
    {
        var copy = new short[stereoPcm.Length];
        stereoPcm.CopyTo(copy);
        Frames.Add(copy);
    }

    public void Dispose()
    {
    }
}
