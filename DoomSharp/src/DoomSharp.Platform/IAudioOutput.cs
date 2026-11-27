namespace DoomSharp.Platform;

/// <summary>
/// Audio output seam — the managed replacement for <c>I_SubmitSound</c>'s
/// <c>write(audio_fd, mixbuffer, SAMPLECOUNT*BUFMUL)</c> (i_sound.c:665-670).
/// </summary>
/// <remarks>
/// Implementations receive 11,025 Hz stereo interleaved signed 16-bit PCM in
/// blocks of <see cref="SoundMixer.BufferSize"/> shorts. Implementations must
/// copy the buffer if they retain it beyond the call.
/// </remarks>
public interface IAudioOutput : IDisposable
{
    /// <summary>Whether the output is live (a real device was acquired).</summary>
    bool IsLive { get; }

    /// <summary>
    /// Submits one mixed block of PCM to the device. The span holds
    /// <see cref="SoundMixer.BufferSize"/> shorts (512 left/right pairs).
    /// </summary>
    void Submit(ReadOnlySpan<short> stereoPcm);
}
