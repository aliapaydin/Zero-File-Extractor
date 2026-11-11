# Yol haritası

## v0.1: İlk sürüm
- [x] WinUI 3 + Windows App SDK + .NET 10, MSIX paketleme, x64 ve ARM64
- [x] Zero File Commander ile ortak görsel dil: Mica, menü, şerit, yol çubuğu, kart yüzeyler
- [x] Açma: ZIP, 7Z, RAR/RAR5 (çok parçalı dahil), TAR ailesi, GZ, BZ2, XZ, ZST, LZ, ARJ, ACE, ARC
- [x] Oluşturma: ZIP (AES-256), 7Z (LZMA2), TAR, TAR.GZ, TAR.BZ2, TAR.ZST; WinRAR kuruluysa RAR
- [x] Altı sıkıştırma düzeyi, süre ve boyut tahmini (ölçülen hıza göre kendini düzeltir)
- [x] Arşiv içinde gezinme: klasör ağacı, ekmek kırıntısı, sıralama, arama, düz görünüm
- [x] Önizleme paneli: görsel, metin/kod, hex
- [x] Sürükle bırak: aç, sıkıştır, arşive ekle, arşivden dışarı çıkar
- [x] İlerleme penceresi: hız, kalan süre, duraklat/sürdür/iptal, görev çubuğu ilerlemesi, iş kuyruğu
- [x] Çakışma çözümü, akıllı klasör, düz çıkarma
- [x] ZIP'e dosya ekleme ve silme
- [x] Arşiv testi, SHA-256, arşiv özellikleri
- [x] Dosya Gezgini sağ tık menüsü (Windows 11 yeni menüsü dahil)
- [x] Dosya türü ilişkilendirmeleri, komut satırı
- [x] Türkçe / İngilizce arayüz, aydınlık / karanlık tema

## v0.2: Eksikleri kapat
- [ ] Parolalı 7Z oluşturma (dosya adları dahil şifreleme)
- [ ] Arşivi parçalara bölme (ör. 100 MB, 700 MB, 4 GB)
- [ ] 7Z ve TAR arşivlerinde dosya ekleme / silme
- [ ] Arşiv içinde yeniden adlandırma
- [ ] Önizlemede PDF ve Office belgeleri
- [ ] İç içe arşivi çıkarmadan açma
- [ ] `zfx` komut satırı takma adı

## v0.3: Güç kullanıcısı
- [ ] Biçim dönüştürme (ör. RAR → ZIP)
- [ ] Kendi açılan arşiv (SFX) oluşturma
- [ ] Sıkıştırma profilleri (kayıtlı biçim + düzey + parola kuralı)
- [ ] Çok çekirdekli sıkıştırma (ZIP ve ZST)
- [ ] Bozuk arşivden kurtarılabilenleri çıkarma
- [ ] Toplu iş: klasördeki tüm arşivleri çıkar / her alt klasörü ayrı arşivle

## Zero File Commander ile bütünleşme
- [ ] Commander'da arşive Enter: Extractor kuruluysa onun motoruyla aç
- [ ] Commander panelinden "Sıkıştır…" ve "Çıkar…" Extractor'ın pencereleriyle
- [ ] İki uygulama arasında sürükle bırak ve ortak aktarım kuyruğu
- [ ] Ortak tema ve dil ayarı
