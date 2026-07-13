namespace FiyatGorService.Configuration;

public interface ISettingsWriter
{
    Task<SaveSettingsResult> SaveAsync(AppSettings settings, bool allowPortChange, CancellationToken cancellationToken = default);
}

public sealed record SaveSettingsResult(bool Saved, bool RequiresRestartConfirmation, int PreviousPort, int NewPort);
