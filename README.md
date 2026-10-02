# UpdateHub

Windows için tek yerden güncelleme merkezi: **uygulamalar, Windows, sürücüler ve BIOS/UEFI**.
Yalnızca **resmi dağıtım kanallarındaki kararlı sürümler** kurulur.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4) ![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20Fluent%20(WPF--UI)-0078D4) ![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6)

## Ne yapar?

| Alan | Kaynak | Nasıl |
| --- | --- | --- |
| **Uygulamalar** | Windows Paket Yöneticisi (`winget`) + Microsoft Store (`msstore`) | winget manifestleri yayıncıların **resmi indirme adreslerine** işaret eder ve SHA-256 doğrulaması yapılır. Beta/preview/nightly/canary/insider/dev/RC yapıları "Ön sürüm" olarak işaretlenir ve toplu güncellemeye **asla** dahil edilmez. |
| **Windows güncellemeleri** | Windows Update Aracısı (WUA COM API) | Kalite, güvenlik ve özellik güncellemeleri. "Preview" paketleri ön sürüm sayılır. |
| **Sürücüler** | Windows Update | Microsoft tarafından dağıtılan **WHQL imzalı** sürücüler. Ayrıca tüm aygıtların yüklü sürücü sürümleri, imza durumu ve sorun kodları listelenir. |
| **BIOS / UEFI** | Üreticinin resmi aracı + Windows Update | Dell → **Dell Command \| Update** (`dcu-cli`), Lenovo → **Lenovo System Update** (`tvsu`), HP → **HP Image Assistant**. Diğer üreticiler (ASUS, MSI, GIGABYTE, ASRock, Acer, Surface…) için yüklü sürüm gösterilir, Windows Update'teki firmware taranır ve **resmi destek sayfası** açılır. Üçüncü taraf BIOS dosyaları hiçbir zaman kullanılmaz. |

Tasarım: Windows 11 Fluent (Mica arka plan, koyu/açık/sistem teması), sol gezinme, sayaç rozetleri, ilerleme ve bildirimler.

## Kurulum

1. **Gereksinim:** Windows 10 1809+ / Windows 11, [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (self-contained paket kullanırsanız gerekmez) ve `winget` (Microsoft Store'daki *Uygulama Yükleyici* paketi; Windows 11'de hazır gelir).
2. GitHub Actions **Build** iş akışının ürettiği `UpdateHub-win-x64.zip` (veya `-selfcontained.zip`) dosyasını indirip açın.
3. `UpdateHub.exe`'yi çalıştırın. Uygulama **yönetici izni** ister; sürücü, Windows ve BIOS kurulumları için zorunludur.

### Kaynak koddan derleme

```powershell
git clone https://github.com/nymeria0061/update-apps.git
cd update-apps
dotnet build UpdateHub.sln -c Release
dotnet run --project src/UpdateHub.App
```

Tek dosya yayın:

```powershell
dotnet publish src/UpdateHub.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Kullanım

* **Genel Bakış** – cihaz/OS/BIOS özeti, kategori sayaçları, *Güncellemeleri tara* ve *Tümünü güncelle*. Toplu güncelleme yalnızca kararlı, otomatik kurulabilen ve yoksayılmamış öğeleri kurar; BIOS varsayılan olarak hariçtir.
* **Uygulamalar / Windows ve Sürücüler** – arama, seçim, tek tek veya seçilenleri kurma, yoksayma, bilgi sayfası.
* **BIOS / UEFI** – yüklü BIOS, anakart, üretici aracının durumu; araç yoksa tek tıkla `winget` ile kurulum (Dell/Lenovo/HP). Her firmware kurulumu önce onay ister.
* **Aygıtlar** – tüm PnP aygıtları ve sürücü sürümleri; sorunlu / imzasız filtre; Aygıt Yöneticisi kısayolu.
* **Günlük** – tüm işlemler; dosya kopyası `%LOCALAPPDATA%\UpdateHub\logs`.
* **Ayarlar** – kararlı-sürüm politikası, yalnızca resmi winget kaynakları, kategori açma/kapama, tema, yoksayılanları geri alma, araç yolları.

## Mimari

```
src/
  UpdateHub.Core      modeller, IUpdateProvider sözleşmesi, StableChannelPolicy, VersionComparer, UpdateOrchestrator, ayarlar
  UpdateHub.Windows   winget sağlayıcısı (tablo ayrıştırma, yerel-dil bağımsız), WUA sağlayıcıları (software/driver/firmware),
                      WMI sistem bilgisi + aygıt envanteri, OEM BIOS stratejileri (Dell / Lenovo / HP), yeniden başlatma
  UpdateHub.App       WPF + WPF-UI (Fluent), MVVM (CommunityToolkit.Mvvm), DI (Microsoft.Extensions.Hosting)
tests/UpdateHub.Tests xunit: winget ayrıştırıcı, kanal politikası, sürüm karşılaştırma, orkestratör, ayarlar
```

`IUpdateProvider` uygulayan her sınıf tarama + kurulum yapar; `UpdateOrchestrator` sağlayıcıları paralel tarar, kurulumları sırayla (firmware en sonda) çalıştırır ve politikaları uygular. Yeni bir kaynak eklemek için bir `IUpdateProvider` yazıp `App.xaml.cs` içinde kaydetmek yeterlidir.

## Sınırlar ve dürüst notlar

* BIOS flaşlama yalnızca üreticinin kendi aracı üzerinden yapılır. Anakart üreticilerinin (ASUS/MSI/GIGABYTE/ASRock) komut satırı aracı olmadığından bu cihazlarda BIOS **otomatik kurulmaz**; uygulama sürümü gösterir ve resmi sayfaya yönlendirir.
* HP ev serisi (Pavilion/Envy/OMEN) cihazlarda HP Image Assistant sonuç vermeyebilir; BIOS genellikle Windows Update/HP Support Assistant ile gelir.
* winget ile yüklenmemiş (örn. elle kurulmuş) ve winget deposunda manifesti olmayan uygulamalar listelenemez.
* Windows Update aramaları WSUS/grup ilkesi ayarlarına uyar.
