# AuthKit Yönetim Sayfaları

Bu dizin, AuthKit paketiyle birlikte gelen hazır yönetim sayfalarını içerir.
Tüm sayfa şablonları C1 CMS Layout perspektifinde gruplu görünmesi için `AuthKit.` önekiyle isimlendirilmiştir.

## Orijinal Sayfalardan Yapılan Değişiklikler

| Orijinal (Araç Takip) | AuthKit (Temiz) |
|---|---|
| `Auth.Authentication.*` | `AuthKit.Authentication.*` |
| `Auth.Authorization.*` | `AuthKit.Authorization.*` |
| `Auth.Navigation.PageIds.Login` (hardcoded GUID) | `AuthKit.Authentication.AuthenticationManager.LoginPageId` |
| `C1.UrlHelper.GetUrlFromPageId` | `AuthKit.C1.C1UrlHelper.GetUrlFromPageId` |
| `PermissionKeys.LocaFleet.*` | KALDIRILDI (araç takip spesifik) |
| Sabit login GUID'leri (`870ff503-...`) | KeyTreeStoreManager üzerinden yapılandırılabilir |

## CSS/JS Bağımlılıkları

`AuthKit.PanelLayout.cshtml` ana layout olup Bootstrap 5, jQuery, DataTables (Editor/Buttons/Select),
SweetAlert2 ve Tabler Icons kütüphanelerini CDN üzerinden içerir. Bu sayede paket kendi kendine yeterlidir;
ayrıca harici tema dosyalarına bağımlılık yoktur.

## Sayfa Listesi

| Sayfa | Amaç |
|---|---|
| `AuthKit.SetupPage.cshtml` | Kurulum sayfası - "Sayfaları Oluştur" ile 9 sayfayı otomatik açar |
| `AuthKit.PanelLayout.cshtml` | Sidebar'lı yönetim paneli ana layout'u |
| `AuthKit.AuthLayout.cshtml` | Login/Register/Şifremi Unuttum/Şifre Sıfırla/Çıkış sayfaları için layout |
| `AuthKit.UserManagementPage.cshtml` | Kullanıcı listesi, ekle/sil/düzenle, grup atama |
| `AuthKit.GroupManagementPage.cshtml` | Grup listesi, ekle/sil/düzenle, üye yönetimi |
| `AuthKit.GroupPermissionPage.cshtml` | Grup bazlı Allow/Deny yetki matrisi |
| `AuthKit.UserPermissionPage.cshtml` | Kullanıcı bazlı Allow/Deny + inheritance görünümü |
