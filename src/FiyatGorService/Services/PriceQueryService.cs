using System.Diagnostics;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using FiyatGorService.Configuration;

namespace FiyatGorService.Services;

public sealed class PriceQueryService
{
    private const int LargeImageWarningThresholdBytes = 300 * 1024;
    private const string DefaultProductImageContentType = "image/jpeg";

    private static readonly HashSet<string> ReservedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "BARKOD",
        "URUNADI",
        "FIYAT",
        "BIRIM",
        "URUNRESIM",
        "URUNRESIMTIPI"
    };

    private readonly IOptionsMonitor<AppSettings> _settingsMonitor;
    private readonly ILogger<PriceQueryService> _logger;

    public PriceQueryService(IOptionsMonitor<AppSettings> settingsMonitor, ILogger<PriceQueryService> logger)
    {
        _settingsMonitor = settingsMonitor;
        _logger = logger;
    }

    public async Task<PriceQueryResult> QueryAsync(
        string barcode,
        IReadOnlyDictionary<string, string> extraParameters,
        CancellationToken cancellationToken)
    {
        var settings = _settingsMonitor.CurrentValue;
        var connectionString = settings.Sql.ToConnectionString();
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("SQL baglanti ayarlari bos.");
        }

        if (string.IsNullOrWhiteSpace(settings.PriceQuery))
        {
            throw new InvalidOperationException("PriceQuery ayari bos.");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(settings.PriceQuery, connection)
            {
                CommandTimeout = 15
            };
            command.Parameters.Add(new SqlParameter("@Barkod", barcode));

            foreach (var parameter in extraParameters)
            {
                command.Parameters.Add(new SqlParameter("@" + parameter.Key, parameter.Value));
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                stopwatch.Stop();
                _logger.LogInformation("Price query completed. Barcode={Barcode} Found={Found} DurationMs={DurationMs}", barcode, false, stopwatch.ElapsedMilliseconds);
                return new PriceQueryResult(false, barcode, null, null, null, null, Array.Empty<ExtraField>());
            }

            var columns = ReadColumns(reader);
            var resultBarcode = GetReservedString(columns, "BARKOD") ?? barcode;
            var productName = GetReservedString(columns, "URUNADI");
            var price = GetReservedDecimal(columns, "FIYAT");
            var unit = GetReservedString(columns, "BIRIM");
            var productImage = ResolveProductImage(columns, resultBarcode);
            var extra = BuildExtraFields(columns);

            stopwatch.Stop();
            _logger.LogInformation("Price query completed. Barcode={Barcode} Found={Found} DurationMs={DurationMs}", barcode, true, stopwatch.ElapsedMilliseconds);

            return new PriceQueryResult(
                true,
                resultBarcode,
                productName,
                price,
                unit,
                productImage,
                extra);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Price query failed. Barcode={Barcode} DurationMs={DurationMs}", barcode, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private string? ResolveProductImage(IReadOnlyList<ColumnValue> columns, string barcode)
    {
        var column = columns.FirstOrDefault(c => string.Equals(c.Name, "URUNRESIM", StringComparison.OrdinalIgnoreCase));
        if (column?.Value is null)
        {
            return null;
        }

        string? productImage = column.Value switch
        {
            byte[] bytes => BuildDataUri(Convert.ToBase64String(bytes), GetImageContentType(columns)),
            string text when string.IsNullOrWhiteSpace(text) => null,
            string text when text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) => text,
            string text => BuildDataUri(text, GetImageContentType(columns)),
            _ => BuildDataUri(Convert.ToString(column.Value) ?? string.Empty, GetImageContentType(columns))
        };

        if (!string.IsNullOrEmpty(productImage) && productImage.Length > LargeImageWarningThresholdBytes)
        {
            var sizeKb = Math.Round(productImage.Length / 1024.0, 1);
            _logger.LogWarning("Ürün resmi büyük, {Barcode}: {SizeKB}KB", barcode, sizeKb);
        }

        return productImage;
    }

    private static string GetImageContentType(IReadOnlyList<ColumnValue> columns)
    {
        var contentType = GetReservedString(columns, "URUNRESIMTIPI");
        return string.IsNullOrWhiteSpace(contentType) ? DefaultProductImageContentType : contentType.Trim();
    }

    private static string BuildDataUri(string base64, string contentType)
    {
        return $"data:{contentType};base64,{base64}";
    }

    private static List<ColumnValue> ReadColumns(IDataRecord reader)
    {
        var columns = new List<ColumnValue>(reader.FieldCount);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new ColumnValue(reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i)));
        }

        return columns;
    }

    private static List<ExtraField> BuildExtraFields(IEnumerable<ColumnValue> columns)
    {
        var extra = new List<ExtraField>();

        foreach (var column in columns)
        {
            if (ReservedColumns.Contains(column.Name))
            {
                continue;
            }

            extra.Add(new ExtraField(column.Name, column.Value?.ToString() ?? string.Empty));
        }

        return extra;
    }

    private static string? GetReservedString(IEnumerable<ColumnValue> columns, string reservedName)
    {
        var column = columns.FirstOrDefault(c => string.Equals(c.Name, reservedName, StringComparison.OrdinalIgnoreCase));
        return column?.Value is null ? null : Convert.ToString(column.Value);
    }

    private static decimal? GetReservedDecimal(IEnumerable<ColumnValue> columns, string reservedName)
    {
        var column = columns.FirstOrDefault(c => string.Equals(c.Name, reservedName, StringComparison.OrdinalIgnoreCase));
        if (column?.Value is null)
        {
            return null;
        }

        return Convert.ToDecimal(column.Value);
    }

    private sealed record ColumnValue(string Name, object? Value);
}

public sealed record ExtraField(string Label, string Value);

public sealed record PriceQueryResult(
    bool Found,
    string Barcode,
    string? ProductName,
    decimal? Price,
    string? Unit,
    string? ProductImage,
    IReadOnlyList<ExtraField> Extra);
