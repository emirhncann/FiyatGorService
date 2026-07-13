using FiyatGorService.Security;

namespace FiyatGorService.Configuration;

public sealed class AppSettings
{
    public const int DefaultPort = 5080;

    public const string DefaultPriceQuery =
        "SELECT TOP 1 Barkod, UrunAdi, Fiyat, Birim FROM dbo.Urunler WHERE Barkod = @Barkod";

    public int Port { get; set; } = DefaultPort;

    public SqlSettings Sql { get; set; } = new();

    public string PriceQuery { get; set; } = DefaultPriceQuery;

    public string LogoBase64 { get; set; } = string.Empty;

    public string LogoContentType { get; set; } = string.Empty;

    public AdminSettings Admin { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            Port = DefaultPort,
            Sql = new SqlSettings(),
            PriceQuery = DefaultPriceQuery,
            LogoBase64 = string.Empty,
            LogoContentType = string.Empty,
            Admin = new AdminSettings
            {
                Username = "admin",
                PasswordHash = PasswordHasher.HashPassword("admin123")
            }
        };
    }
}

public sealed class SqlSettings
{
    public string Server { get; set; } = string.Empty;

    public string Database { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class AdminSettings
{
    public string Username { get; set; } = "admin";

    public string PasswordHash { get; set; } = string.Empty;
}
