using Microsoft.Data.SqlClient;

namespace FiyatGorService.Configuration;

public static class SqlSettingsExtensions
{
    public static string ToConnectionString(this SqlSettings sql)
    {
        if (string.IsNullOrWhiteSpace(sql.Server) || string.IsNullOrWhiteSpace(sql.Database))
        {
            return string.Empty;
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = sql.Server.Trim(),
            InitialCatalog = sql.Database.Trim(),
            TrustServerCertificate = true
        };

        if (!string.IsNullOrWhiteSpace(sql.User))
        {
            builder.UserID = sql.User.Trim();
            builder.Password = sql.Password ?? string.Empty;
            builder.IntegratedSecurity = false;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }
}
