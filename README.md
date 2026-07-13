# FiyatGorService

Bu proje, .NET 8 tabanli bir ASP.NET Core Minimal API + Razor Pages uygulamasidir ve Windows Service olarak calisacak sekilde hazirlanmistir.

## API

Servis SSL VPN uzerinden erisilen yerel ag ortaminda calisir; tablet istekleri icin ayri bir API anahtari gerekmez.

- `GET /api/health` — SQL baglanti durumu
- `GET /api/price?barcode=...&sube=0001&bolge=IST` — fiyat sorgusu (`barcode` disindaki her query parametresi otomatik SQL parametresi olur, ornegin `@sube`)
- `GET /api/logo` — firma logosu
- `GET /admin` — yonetim paneli (basic auth)

`/admin` basic auth ile korunur. Ilk calistirmada otomatik olusturulan `settings.json` icinde varsayilan admin sifresi `admin123` olarak ayarlanir.

## Ayarlar (`settings.json`)

Uygulama `appsettings.json` kullanmaz. Ayarlar exe'nin yanindaki `settings.json` dosyasinda tutulur. Dosya repoya gitmez; sablon icin `settings.example.json` kullanin.

```json
{
  "Port": 5080,
  "Sql": {
    "Server": "",
    "Database": "",
    "User": "",
    "Password": ""
  },
  "PriceQuery": "SELECT TOP 1 Barkod, UrunAdi, Fiyat, Birim FROM dbo.Urunler WHERE Barkod = @Barkod",
  "LogoBase64": "",
  "LogoContentType": "",
  "Admin": {
    "Username": "admin",
    "PasswordHash": ""
  }
}
```

- Ilk calistirmada `settings.json` yoksa otomatik olusturulur.
- `IOptionsMonitor<AppSettings>` ile dosya degisiklikleri izlenir; port haric ayarlar restart gerektirmeden yansir.
- Port degisikligi ayri bir mekanizma ile servis yeniden baslatmasi gerektirir.
- SQL connection string runtime'da `AppSettings.Sql.ToConnectionString()` ile uretilir.

## Local Calistirma

```powershell
dotnet restore
dotnet run --project .\src\FiyatGorService\FiyatGorService.csproj
```

Yerel erisim (ornek; port `settings.json`'dan okunur):

- `http://localhost:5080/api/health`
- `http://localhost:5080/admin`

## Publish

```powershell
dotnet publish .\src\FiyatGorService\FiyatGorService.csproj -c Release -r win-x64 --self-contained false -o C:\Services\FiyatGorService
```

Publish sonrasinda `settings.example.json` dosyasini `C:\Services\FiyatGorService\settings.json` olarak kopyalayip ortama gore duzenleyin.

## Windows Service Kurulumu

```powershell
sc.exe create FiyatGorService binPath= "C:\Program Files\dotnet\dotnet.exe C:\Services\FiyatGorService\FiyatGorService.dll" start= auto
sc.exe description FiyatGorService "FiyatGor barkod fiyat servis uygulamasi"
sc.exe start FiyatGorService
sc.exe failure FiyatGorService reset= 86400 actions= restart/5000/restart/5000/restart/5000
sc.exe failureflag FiyatGorService 1
```
