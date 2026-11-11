# Microsoft Store'da yayımlama

Zero File Commander ile aynı adımlar; farklar burada.

1. **Adı ayırt:** Partner Center → Uygulamalar → Yeni ürün → "Zero File Extractor".
2. **Kimliği eşle:** Ürün kimliği sayfasındaki `Package/Identity/Name` ve `Publisher` değerlerini `src/ZeroFileExtractor/Package.appxmanifest` içindeki `Identity` ile karşılaştır. Şu an `AliApaydin.ZeroFileExtractor` varsayıldı; Partner Center farklı bir ad verdiyse manifesti ona göre düzelt.
3. **Paketi üret:** GitHub → Actions → **store-package** → Run workflow. Çıkan `.msixupload` dosyasını Partner Center'a yükle.
4. **Sertifikasyon notları** (gönderim sırasında "Notes for certification" alanına):
   - `runFullTrust`: masaüstü uygulaması; kullanıcının seçtiği klasörlerde arşiv okur ve yazar.
   - Dosya Gezgini bağlam menüsü uzantısı (`ZfxShell.dll`) yalnızca seçili yolları uygulamaya iletir.
   - RAR oluşturma, kullanıcının bilgisayarında kurulu WinRAR varsa onun `Rar.exe` aracını çalıştırır; uygulama RAR sıkıştırma kodu içermez.
5. **Gizlilik politikası adresi:** depo herkese açık olduğunda `PRIVACY.md` bağlantısı. Depo özelken Store bu adrese erişemez; yayımlamadan önce depoyu herkese aç ya da politikayı başka bir adreste yayımla.
