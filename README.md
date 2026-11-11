<p align="center">
  <img src="assets/brand/zfx-logo-256.png" width="112" alt="Zero File Extractor logosu">
</p>

<h1 align="center">Zero File Extractor</h1>

<p align="center">
  ZIP, RAR, 7Z ve daha fazlasını açan, sıkıştıran, içine bakan modern arşiv aracı.<br>
  <sub>A modern archive tool for Windows: open, preview, extract and create ZIP, RAR, 7Z and more.</sub>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/lisans-MIT-1f6fd6" alt="MIT Lisansı"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-1f6fd6" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/sürüm-0.1.0-1f6fd6" alt="Sürüm 0.1.0">
  <img src="https://img.shields.io/badge/.NET-10-512bd4" alt=".NET 10">
</p>

---

**Zero File Extractor**, [Zero File Commander](https://github.com/aliapaydin/zero-file-commander) ile aynı görsel dili paylaşan açık kaynaklı bir arşiv yöneticisi. Arşivi çıkarmadan içinde gezebilir, dosyaları önizleyebilir, sürükleyip bırakarak çıkarabilir ya da sıkıştırabilirsin. Reklam, telemetri ve deneme süresi uyarısı yok.

## Öne çıkanlar

- **Çıkarmadan içine bak:** klasör ağacı, ekmek kırıntısı yolu, sıralanabilir sütunlar, arşiv içinde arama ve düz görünüm.
- **Önizleme paneli:** görseller, metin ve kod dosyaları, ikili dosyalar için hex dökümü. Dosya arşivden çıkarılmadan gösterilir.
- **Sürükle bırak her yönde:** arşivi pencereye bırak, açılsın. Dosya ya da klasör bırak, yeni arşiv olsun. Arşivin içinden Dosya Gezgini'ne ya da masaüstüne sürükle, oraya çıksın.
- **Süre ve boyut tahmini:** sıkıştırmadan önce yaklaşık süreyi ve arşiv boyutunu söyler. Tahmin, bilgisayarında ölçülen gerçek hıza göre her işte iyileşir.
- **Sağ tık menüsü:** Dosya Gezgini'nde *Zero File Extractor* alt menüsü. Windows 11'in yeni menüsünde de görünür.
- **Güvenli:** arşiv içindeki kötü niyetli yollar hedef klasörün dışına yazamaz, silinenler Geri Dönüşüm Kutusu'na gider, yarıda kalan iş asıl arşivi bozmaz.

## Desteklenen biçimler

| Biçim | Aç / çıkar | Oluştur | Not |
|---|:-:|:-:|---|
| ZIP, ZIPX | ✅ | ✅ | AES-256 parola, ZIP64, içine dosya ekleme ve silme |
| 7Z | ✅ | ✅ | LZMA2. Parolalı 7Z açılır; parolalı 7Z oluşturma henüz yok |
| RAR, RAR5 | ✅ | ⚠️ | Çok parçalı (`.part1.rar`, `.r00`) ve parolalı arşivler açılır. Oluşturmak için kurulu WinRAR gerekir |
| TAR | ✅ | ✅ | |
| TAR.GZ / TGZ | ✅ | ✅ | |
| TAR.BZ2 / TBZ2 | ✅ | ✅ | |
| TAR.ZST / TZST | ✅ | ✅ | |
| TAR.XZ, TAR.LZ, TAR.Z | ✅ | | |
| GZ, BZ2, XZ, ZST, LZ | ✅ | | Tek dosya sıkıştırmaları |
| ARJ, ACE, ARC | ✅ | | |
| JAR, WAR, APK, IPA, EPUB, CBZ, CBR, NUPKG, VSIX, XPI | ✅ | | ZIP/RAR tabanlı kaplar |

> **RAR neden oluşturulamıyor?** RAR'ın sıkıştırma algoritması RARLAB'e ait ve kapalı. Lisansı yalnızca *açmaya* izin veriyor. Bu yüzden hiçbir açık kaynak araç RAR üretemez. Bilgisayarında WinRAR kuruluysa Zero File Extractor onun `Rar.exe` aracını kullanarak RAR oluşturur; kurulu değilse seçenek kapalı görünür.

## Özellikler

### Arşivde gezinme
- Klasör ağacı (Ctrl+Shift+T), ekmek kırıntısı yolu, üst klasöre Backspace ile çıkma
- Sütunlar: ad, boyut, sıkıştırılmış boyut, oran, tür, değiştirilme tarihi. Başlığa tıklayarak sıralama
- Şifreli dosyalarda kilit simgesi
- Arşiv içinde arama (Ctrl+F) ve tüm dosyaları tek listede gösteren düz görünüm (Ctrl+B)
- Dosyaya çift tıklayınca varsayılan uygulamasında açma
- Son açılan arşivler listesi

### Çıkarma
- Tümünü ya da yalnızca seçilenleri çıkarma
- **Akıllı klasör:** arşivin kökünde tek klasör varsa doğrudan, dağınık dosyalar varsa arşivin adında yeni klasöre çıkarır. Masaüstüne saçılan yüzlerce dosya olmaz
- Aynı adlı dosya için: sor, üzerine yaz, atla ya da ikisini de tut. "Kalan tüm çakışmalar için uygula" seçeneğiyle
- Klasör yapısını yok sayıp tüm dosyaları tek klasöre çıkarma
- Bitince klasörü açma, bitince arşivi Geri Dönüşüm Kutusu'na gönderme
- Birden fazla arşivi sırayla çıkarma
- Seçilenleri panoya kopyalayıp Dosya Gezgini'ne yapıştırma (Ctrl+C)

### Sıkıştırma
- Biçim: ZIP, 7Z, TAR, TAR.GZ, TAR.BZ2, TAR.ZST (WinRAR kuruluysa RAR)
- Altı düzey: Depola, En hızlı, Hızlı, Normal, İyi, En iyi
- Başlamadan önce tahmini süre ve tahmini arşiv boyutu
- ZIP için AES-256 parola
- Bitince kaynak dosyaları Geri Dönüşüm Kutusu'na gönderme
- Var olan ZIP'e dosya ekleme (Insert) ve içinden silme (Delete)

### İlerleme penceresi
- Hız, geçen süre, kalan süre, dosya sayacı
- Duraklat, sürdür, iptal
- Görev çubuğu simgesinde ilerleme
- Atlanan ya da sorunlu dosyaların listesi

### Doğrulama
- Arşivi test et (Ctrl+T): tüm dosyaları okuyup bütünlüğü doğrular
- Arşiv dosyasının SHA-256 özeti
- Arşiv özellikleri (Alt+Enter): biçim, boyutlar, oran, solid, çok parçalı, açıklama

### Dosya Gezgini sağ tık menüsü

Bir arşive sağ tıklayınca:

| Komut | Ne yapar |
|---|---|
| Zero File Extractor ile aç | Arşivi pencerede açar |
| Buraya çıkar | Arşivin bulunduğu klasöre çıkarır |
| "ad\\" klasörüne çıkar | Arşivin adında yeni klasöre çıkarır |
| Klasör seçerek çıkar… | Hedef klasörü ve seçenekleri sorar |
| Arşivi test et | Bütünlüğü doğrular |

Herhangi bir dosya ya da klasöre sağ tıklayınca:

| Komut | Ne yapar |
|---|---|
| "ad.zip" olarak sıkıştır | Aynı klasöre ZIP oluşturur |
| "ad.7z" olarak sıkıştır | Aynı klasöre 7Z oluşturur |
| Sıkıştır… | Biçim, düzey, parola ve hedef sorar; süreyi tahmin eder |

Birden fazla öğe seçiliyken de çalışır. Menü Windows'un diline göre Türkçe ya da İngilizce görünür.

### Görünüm
- Mica arka plan, aydınlık, karanlık ya da sisteme göre tema
- Şerit, klasör ağacı, önizleme paneli ve durum çubuğu ayrı ayrı açılıp kapanır
- Sürüklenebilir panel ayırıcılar. Pencere konumu ve panel genişlikleri hatırlanır
- Türkçe ve İngilizce arayüz

## ⌨️ Klavye kısayolları

| Kısayol | İşlev |
|---|---|
| Ctrl+O | Arşiv aç |
| Ctrl+N | Dosyalardan yeni arşiv |
| Ctrl+Shift+N | Klasörden yeni arşiv |
| Ctrl+W | Arşivi kapat |
| Ctrl+E / F5 | Tümünü çıkar |
| Ctrl+Shift+E / F6 | Seçilenleri çıkar |
| Ctrl+Shift+H | Arşivin yanına çıkar (akıllı klasör) |
| Ctrl+T | Arşivi test et |
| Enter | Klasöre gir / dosyayı aç |
| Backspace / Alt+↑ | Üst klasör |
| F3 | Önizle |
| Ctrl+Q | Önizleme panelini aç/kapat |
| Ctrl+Shift+T | Klasör ağacını aç/kapat |
| Ctrl+B | Düz görünüm |
| Ctrl+F | Arşivde ara |
| Esc | Aramayı temizle |
| Insert / Ctrl+Shift+A | Arşive dosya ekle (ZIP) |
| Delete | Arşivden sil (ZIP) |
| Ctrl+A | Tümünü seç |
| Ctrl+I | Seçimi tersine çevir |
| Ctrl+C | Seçilenleri panoya kopyala |
| Ctrl+Shift+C | Arşiv içi yolu kopyala |
| Alt+Enter | Arşiv özellikleri |
| Ctrl+R | Yenile |
| Ctrl+, | Ayarlar |
| F1 | Klavye kısayolları |

## 🔧 Kurulum ve derleme

Gerekenler:
- Windows 10 2004 (19041) ya da üstü
- Visual Studio 2022 ya da üstü; iş yükleri: **.NET masaüstü geliştirme**, **Windows uygulama geliştirme** ve sağ tık menüsü DLL'i için **C++ ile masaüstü geliştirme**
- .NET 10 SDK

```powershell
git clone https://github.com/aliapaydin/Zero-File-Extractor.git
cd Zero-File-Extractor
start ZeroFileExtractor.sln
```

Visual Studio'da platformu **x64** seç, **F5**. Sağ tık menüsü yalnızca uygulama paket olarak kurulduğunda (F5 ile dağıtım dahil) görünür. Menü görünmezse Dosya Gezgini'ni yeniden başlat.

C++ araçları kurulu değilse ve yalnızca arayüz üzerinde çalışacaksan sağ tık menüsünü derlemeden geçebilirsin:

```powershell
msbuild src\ZeroFileExtractor\ZeroFileExtractor.csproj /restore /p:Platform=x64 /p:BuildShellExtension=false
```

### Komut satırı

Uygulama, sağ tık menüsünün kullandığı komutları doğrudan da kabul eder:

```
ZeroFileExtractor.exe arsiv.zip                      Arşivi pencerede aç
ZeroFileExtractor.exe --extract-here a.zip b.rar     Bulundukları klasöre çıkar
ZeroFileExtractor.exe --extract-to-folder a.zip      "a\" klasörüne çıkar
ZeroFileExtractor.exe --extract-ask a.zip            Hedefi sor
ZeroFileExtractor.exe --test a.7z                    Bütünlüğü doğrula
ZeroFileExtractor.exe --zip klasor dosya.txt         ZIP oluştur
ZeroFileExtractor.exe --7z klasor                    7Z oluştur
ZeroFileExtractor.exe --compress-ask klasor          Biçim ve düzeyi sor
```

## 🗺️ Yol haritası

Ayrıntılar [docs/ROADMAP.md](docs/ROADMAP.md) içinde. Sıradakiler: parolalı 7Z oluşturma, arşivi parçalara bölme, biçim dönüştürme, kendi açılan arşivler ve Zero File Commander ile bütünleşme.

## Yapı

```
src/ZeroFileExtractor/     WinUI 3 uygulaması
  Engine/                  Arşiv okuma, yazma, ZIP düzenleme, RAR köprüsü
  Services/                İşler, tahmin, ayarlar, dil, kabuk yardımcıları
  Views/                   İlerleme penceresi, diyaloglar, seçenek panelleri
  Strings/en.json          İngilizce metinler
src/ZfxShell/              Dosya Gezgini sağ tık menüsü (C++)
```

## Teşekkür

- [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT): arşiv okuma, 7Z ve TAR yazma
- [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) (MIT): AES şifreli ZIP yazma
- RAR, RARLAB'in tescilli biçimidir. Zero File Extractor RAR arşivlerini açar; oluşturmak için kurulu WinRAR'ı kullanır.

## Lisans

[MIT](LICENSE) © Ali Apaydın

## Gizlilik

Uygulama veri toplamaz ve internete bağlanmaz. Ayrıntılar: [PRIVACY.md](PRIVACY.md)
