namespace AneedWeb.Services;

// Per-Blazor-circuit selection. The song is resolved server-side from SAM's stable song ID.
public sealed class SongPopupState
{
    public long? SongId { get; private set; }
    public event Action? Changed;

    public void Open(long songId)
    {
        if (songId <= 0) return;
        SongId = songId;
        Changed?.Invoke();
    }

    public void Close()
    {
        SongId = null;
        Changed?.Invoke();
    }
}
