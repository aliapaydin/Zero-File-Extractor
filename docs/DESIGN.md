# Tasarım notları

Zero File Extractor, [Zero File Commander](https://github.com/aliapaydin/zero-file-commander) ile aynı ailenin üyesi. İkisi yan yana açıldığında tek bir ürünün iki penceresi gibi durmalı.

## Ortak görsel dil

- **Zemin:** Mica. İçerik, 10 px köşeli kart yüzeylerde (`CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush`).
- **Yerleşim sırası:** başlık çubuğu → uygulama menüsü → şerit → yol çubuğu → içerik → durum çubuğu.
- **Şerit:** seçime göre etkinleşen `CommandBar`; solunda açık arşivin biçimini gösteren vurgu renkli etiket.
- **Simgeler:** Segoe Fluent Icons. Dosya simgeleri Windows kabuğundan alınır, böylece her tür tanıdık görünür.
- **Hareket:** ilerleme çubuğu dışında animasyon yok; hızlı hissettirmek her şeyden önemli.

## Renkler

| Ad | Hex | Kullanım |
|---|---|---|
| Zero Mavi | `#1F6FD6` | Logo zemini, vurgu |
| Sinyal Amber | `#F2B84B` | Logodaki ok, şifreli dosya kilidi, uyarılar |
| Gece | `#0D1016` | Karanlık tema zemini |
| Kâğıt | `#EEF1F5` | Aydınlık tema zemini |

Uygulama içinde mümkün olduğunca WinUI tema kaynakları kullanılır; sistem vurgu rengi ve yüksek karşıtlık modu kendiliğinden çalışır.

## Logo

Commander'daki sıfırın aynısı; ok bu kez yukarı, sıfırın içinden dışarı çıkıyor: dosya arşivden çıkıyor.

- Ana: `assets/brand/zfx-logo.svg`
- 16–32 px için: `assets/brand/zfx-logo-small.svg`

## İlkeler

1. **Önce içine bak.** Arşivi çıkarmadan gezmek ve önizlemek varsayılan.
2. **Dağıtma.** Akıllı klasör kuralı: tek kök klasör varsa doğrudan, yoksa arşivin adında klasöre.
3. **Söz ver, tut.** Süre tahmini başlamadan önce gösterilir ve her işte gerçek hıza göre düzeltilir.
4. **Zarar verme.** Silme Geri Dönüşüm Kutusu'na; arşiv düzenleme kopyada; hedef klasörün dışına yazma yok.
5. **Klavye önce gelir.** Her komutun bir kısayolu var.
