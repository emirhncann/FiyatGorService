using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FiyatGorService.Configuration;

public sealed class SettingsWriter : ISettingsWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IOptionsMonitor<AppSettings> _optionsMonitor;

    public SettingsWriter(IOptionsMonitor<AppSettings> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public async Task<SaveSettingsResult> SaveAsync(AppSettings settings, bool allowPortChange, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);

        try
        {
            var current = _optionsMonitor.CurrentValue;
            var normalized = AppSettingsNormalizer.Normalize(Clone(settings));
            var portChanged = current.Port != normalized.Port;

            if (portChanged && !allowPortChange)
            {
                return new SaveSettingsResult(false, true, current.Port, normalized.Port);
            }

            var json = JsonSerializer.Serialize(normalized, SerializerOptions);
            await File.WriteAllTextAsync(SettingsBootstrap.SettingsPath, json, cancellationToken);

            return new SaveSettingsResult(true, false, current.Port, normalized.Port);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static AppSettings Clone(AppSettings settings)
    {
        return new AppSettings
        {
            Port = settings.Port,
            Sql = new SqlSettings
            {
                Server = settings.Sql.Server,
                Database = settings.Sql.Database,
                User = settings.Sql.User,
                Password = settings.Sql.Password
            },
            PriceQuery = settings.PriceQuery,
            LogoBase64 = settings.LogoBase64,
            LogoContentType = settings.LogoContentType,
            Admin = new AdminSettings
            {
                Username = settings.Admin.Username,
                PasswordHash = settings.Admin.PasswordHash
            }
        };
    }
}
