# Zero File Extractor: çalışma notları

- WinUI 3 / Windows App SDK 2.x / .NET 10, tek proje MSIX: `src/ZeroFileExtractor`. Görsel dil Zero File Commander ile ortak (Mica, kart yüzeyler, Zero Mavi + Sinyal Amber).
- `src/ZfxShell`: Dosya Gezgini sağ tık menüsü (C++, IExplorerCommand). Yalnızca seçili yolları `ZeroFileExtractor.exe --komut …` ile iletir; komut adları `Services/LaunchRequest.cs` ile, uzantı listesi `Engine/ArchiveFormats.cs` ile aynı tutulmalı. CLSID `Package.appxmanifest` içinde de geçer.
- Hedef platformlar x64 ve ARM64. Derleme yalnızca Windows'ta yapılabiliyor; Linux ortamında doğrulama GitHub Actions (`.github/workflows/build.yml`) üzerinden.
- Arşiv okuma: SharpCompress. ZIP yazma (AES-256 dahil): SharpZipLib. 7Z/TAR yazma: SharpCompress. RAR yazma: yalnızca kurulu WinRAR'ın `Rar.exe`'si ile (biçim tescilli; açık kaynak RAR sıkıştırıcı yok).
- Arayüz metinleri kodda Türkçe; İngilizce karşılıklar `Strings/en.json` (anahtar: Türkçe metin). Yeni metin eklersen oraya da ekle. Kod ve tanımlayıcılar İngilizce.
- Renkler için önce WinUI tema kaynaklarını kullan; marka renkleri `App.xaml` ve `docs/DESIGN.md` içinde.
- Güvenlik kuralları: çıkarırken hedef klasörün dışına yazma yok (zip-slip denetimi `ArchiveSession.Extract`); silmeler her zaman Geri Dönüşüm Kutusu'na; arşiv düzenleme geçici kopyada yapılıp sonra yer değiştirir.
- Yeni özellikleri `docs/ROADMAP.md` sırasıyla ekle ve tamamlananları işaretle.
