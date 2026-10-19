using System;
using NAudio.CoreAudioApi;
namespace PartsPortal.Services;
internal sealed class TemporaryShakeVolume : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDevice _device;
    private readonly float _originalVolume;
    private readonly bool _originalMute;
    private readonly float _appliedVolume;
    private readonly bool _changedVolume;
    private bool _disposed;
    public TemporaryShakeVolume(float minimumVolume = 0.80f)
    {
        _enumerator = new MMDeviceEnumerator();
        _device = _enumerator.GetDefaultAudioEndpoint(
            DataFlow.Render,
            Role.Multimedia);
        var audio = _device.AudioEndpointVolume;
        _originalVolume = audio.MasterVolumeLevelScalar;
        _originalMute = audio.Mute;
        _appliedVolume = Math.Max(
            _originalVolume,
            Math.Clamp(minimumVolume, 0f, 1f));
        _changedVolume =
            _appliedVolume > _originalVolume + 0.001f;
        if (_changedVolume)
            audio.MasterVolumeLevelScalar = _appliedVolume;
        audio.Mute = false;
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            var audio = _device.AudioEndpointVolume;
            // Leave a manually adjusted volume alone.
            if (_changedVolume &&
                Math.Abs(audio.MasterVolumeLevelScalar -
                         _appliedVolume) < 0.01f)
            {
                audio.MasterVolumeLevelScalar =
                    _originalVolume;
            }
            // Restore the original mute state.
            if (_originalMute && !audio.Mute)
                audio.Mute = true;
        }
        finally
        {
            _device.Dispose();
            _enumerator.Dispose();
        }
    }
}