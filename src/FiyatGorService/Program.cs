using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Serilog;
using FiyatGorService.Authentication;
using FiyatGorService.Configuration;
using FiyatGorService.Services;

var bootstrapSettings = SettingsBootstrap.EnsureSettingsFileExists();
const long MaxLogoSizeBytes = 10 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Configuration.AddJsonFile(SettingsBootstrap.SettingsPath, optional: false, reloadOnChange: true);
builder.Services.Configure<AppSettings>(builder.Configuration);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "FiyatGorService";
});

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(AppContext.BaseDirectory, "logs", "log-.txt"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true);
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(bootstrapSettings.Port);
});

builder.Services.AddSingleton<ISettingsWriter, SettingsWriter>();
builder.Services.AddSingleton<PriceQueryService>();
builder.Services.AddSingleton<ServiceRestartCoordinator>();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizePage("/Admin/Index");
});
builder.Services.AddAuthentication("Basic")
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("Basic", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("price", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromSeconds(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", async (IOptionsMonitor<AppSettings> settingsMonitor, CancellationToken cancellationToken) =>
{
    var settings = settingsMonitor.CurrentValue;
    var sqlConnected = false;

    try
    {
        var connectionString = settings.Sql.ToConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            sqlConnected = true;
        }
    }
    catch
    {
        sqlConnected = false;
    }

    return Results.Ok(new
    {
        status = "ok",
        sqlConnected
    });
});

app.MapGet("/api/logo", () =>
{
    var settings = SettingsBootstrap.LoadFromDisk();
    if (string.IsNullOrWhiteSpace(settings.LogoBase64))
    {
        return Results.NotFound();
    }

    try
    {
        var bytes = Convert.FromBase64String(settings.LogoBase64);
        var contentType = string.IsNullOrWhiteSpace(settings.LogoContentType) ? "image/png" : settings.LogoContentType;
        return Results.File(bytes, contentType, enableRangeProcessing: false);
    }
    catch
    {
        return Results.NotFound();
    }
});

app.MapGet("/api/price", async (
    HttpRequest request,
    [FromQuery] string barcode,
    PriceQueryService priceQueryService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(barcode))
    {
        return Results.BadRequest(new { error = "barcode parametresi zorunludur." });
    }

    try
    {
        var extraParameters = DynamicQueryParameterParser.Parse(request.Query);
        var result = await priceQueryService.QueryAsync(barcode, extraParameters, cancellationToken);
        if (!result.Found)
        {
            return Results.Ok(new
            {
                found = false,
                barcode = result.Barcode
            });
        }

        return Results.Ok(new
        {
            found = true,
            barcode = result.Barcode,
            productName = result.ProductName,
            price = result.Price,
            unit = result.Unit,
            productImage = result.ProductImage,
            extra = result.Extra.Select(field => new { label = field.Label, value = field.Value })
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Price endpoint failed for barcode {Barcode}", barcode);
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireRateLimiting("price");

var adminApi = app.MapGroup("/admin/api").RequireAuthorization().DisableAntiforgery();

adminApi.MapGet("/settings", (IOptionsMonitor<AppSettings> settingsMonitor) =>
{
    var settings = settingsMonitor.CurrentValue;
    return Results.Ok(new AdminSettingsResponse(
        settings.Sql.Server,
        settings.Sql.Database,
        settings.Sql.User,
        settings.Sql.Password,
        settings.PriceQuery,
        settings.Port,
        string.IsNullOrWhiteSpace(settings.LogoBase64) ? null : settings.LogoBase64,
        string.IsNullOrWhiteSpace(settings.LogoContentType) ? null : settings.LogoContentType));
});

adminApi.MapPost("/test-connection", async (
    AdminSettingsRequest request,
    CancellationToken cancellationToken) =>
{
    try
    {
        var candidate = BuildUpdatedSettings(request);
        await using var connection = new SqlConnection(candidate.Sql.ToConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
        return Results.Ok(new { message = "SQL baglantisi basarili." });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

adminApi.MapPost("/settings", async (
    AdminSettingsRequest request,
    IOptionsMonitor<AppSettings> settingsMonitor,
    ISettingsWriter settingsWriter,
    ServiceRestartCoordinator restartCoordinator,
    CancellationToken cancellationToken) =>
{
    try
    {
        var current = SettingsBootstrap.LoadFromDisk();
        var updated = BuildUpdatedSettings(request, current);
        var saveResult = await settingsWriter.SaveAsync(updated, request.ConfirmPortChange, cancellationToken);

        if (saveResult.RequiresRestartConfirmation)
        {
            return Results.Ok(new
            {
                requiresRestartConfirmation = true,
                previousPort = saveResult.PreviousPort,
                newPort = saveResult.NewPort
            });
        }

        if (saveResult.PreviousPort != saveResult.NewPort)
        {
            restartCoordinator.ScheduleRestart(TimeSpan.FromSeconds(1));
            return Results.Ok(new
            {
                message = $"Port kaydedildi. Servis {saveResult.NewPort} portu icin yeniden baslatiliyor."
            });
        }

        return Results.Ok(new { message = "Ayarlar kaydedildi." });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

adminApi.MapPost("/logo", async (
    IFormFile file,
    IOptionsMonitor<AppSettings> settingsMonitor,
    ISettingsWriter settingsWriter,
    CancellationToken cancellationToken) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Gecerli bir logo dosyasi secin." });
    }

    if (file.Length > MaxLogoSizeBytes)
    {
        return Results.BadRequest(new { error = "Dosya çok büyük" });
    }

    var contentType = ResolveLogoContentType(file);
    if (contentType is null)
    {
        return Results.BadRequest(new { error = "Sadece PNG veya JPEG dosyalari kabul edilir." });
    }

    await using var memoryStream = new MemoryStream();
    await file.CopyToAsync(memoryStream, cancellationToken);
    var bytes = memoryStream.ToArray();
    var logoBase64 = Convert.ToBase64String(bytes);

    var current = SettingsBootstrap.LoadFromDisk();
    var updated = new AppSettings
    {
        Port = current.Port,
        Sql = new SqlSettings
        {
            Server = current.Sql.Server,
            Database = current.Sql.Database,
            User = current.Sql.User,
            Password = current.Sql.Password
        },
        PriceQuery = current.PriceQuery,
        LogoBase64 = logoBase64,
        LogoContentType = contentType,
        Admin = new AdminSettings
        {
            Username = current.Admin.Username,
            PasswordHash = current.Admin.PasswordHash
        }
    };

    await settingsWriter.SaveAsync(updated, allowPortChange: true, cancellationToken);

    return Results.Ok(new
    {
        message = "Logo guncellendi.",
        logoBase64,
        logoContentType = contentType
    });
});

app.MapRazorPages();

app.Run();

static AppSettings BuildUpdatedSettings(AdminSettingsRequest request, AppSettings? current = null)
{
    return new AppSettings
    {
        Port = request.Port,
        Sql = new SqlSettings
        {
            Server = request.SqlServer,
            Database = request.Database,
            User = request.Username,
            Password = request.Password
        },
        PriceQuery = request.PriceQuery,
        LogoBase64 = current?.LogoBase64 ?? string.Empty,
        LogoContentType = current?.LogoContentType ?? string.Empty,
        Admin = new AdminSettings
        {
            Username = current?.Admin.Username ?? "admin",
            PasswordHash = current?.Admin.PasswordHash ?? string.Empty
        }
    };
}

static string? ResolveLogoContentType(IFormFile file)
{
    var contentType = file.ContentType?.ToLowerInvariant();
    if (contentType is "image/png" or "image/jpeg")
    {
        return contentType;
    }

    return Path.GetExtension(file.FileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => null
    };
}

sealed record AdminSettingsRequest(
    string SqlServer,
    string Database,
    string Username,
    string Password,
    string PriceQuery,
    int Port,
    bool ConfirmPortChange);

sealed record AdminSettingsResponse(
    string SqlServer,
    string Database,
    string Username,
    string Password,
    string PriceQuery,
    int Port,
    string? LogoBase64,
    string? LogoContentType);
