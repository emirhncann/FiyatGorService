using System.Text.Json;

namespace FiyatGorService.Configuration;

public static class SettingsBootstrap
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings EnsureSettingsFileExists()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaults = AppSettings.CreateDefault();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(defaults, SerializerOptions));
            return defaults;
        }

        return LoadFromDisk();
    }

    public static AppSettings LoadFromDisk()
    {
        if (!File.Exists(SettingsPath))
        {
            return EnsureSettingsFileExists();
        }

        var json = File.ReadAllText(SettingsPath);
        var raw = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? AppSettings.CreateDefault();
        return AppSettingsNormalizer.Normalize(raw);
    }
}

internal static class AppSettingsNormalizer
{
    public static AppSettings Normalize(AppSettings settings)
    {
        if (settings.Port <= 0 || settings.Port > 65535)
        {
            settings.Port = AppSettings.DefaultPort;
        }

        settings.Sql ??= new SqlSettings();
        settings.Sql.Server = settings.Sql.Server?.Trim() ?? string.Empty;
        settings.Sql.Database = settings.Sql.Database?.Trim() ?? string.Empty;
        settings.Sql.User = settings.Sql.User?.Trim() ?? string.Empty;
        settings.Sql.Password ??= string.Empty;

        settings.PriceQuery = string.IsNullOrWhiteSpace(settings.PriceQuery)
            ? AppSettings.DefaultPriceQuery
            : settings.PriceQuery.Trim();

        settings.LogoBase64 = settings.LogoBase64?.Trim() ?? string.Empty;
        settings.LogoContentType = settings.LogoContentType?.Trim() ?? string.Empty;

        settings.Admin ??= new AdminSettings();
        settings.Admin.Username = string.IsNullOrWhiteSpace(settings.Admin.Username)
            ? "admin"
            : settings.Admin.Username.Trim();
        settings.Admin.PasswordHash = settings.Admin.PasswordHash?.Trim() ?? string.Empty;

        return settings;
    }
}
