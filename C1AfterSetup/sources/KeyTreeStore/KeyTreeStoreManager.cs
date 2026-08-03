using Composite.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyTreeStore
{
    /// <summary>
    /// Hiyerarşik key/value store (AuthKit.KeyTreeStore.Data.KeyTreeItem tablosu).
    ///
    /// Path tabanlı çalışır; ayraç "/" tir. Hibrit grup/key erişimi desteklenir:
    ///   "Root/SMTP Settings/Password"  -> açık Root
    ///   "/SMTP Settings/Password"      -> baştaki / Root anlamına gelir (otomatik temizlenir)
    ///   "SMTP Settings/Password"       -> Root yazılmadan da çalışır
    ///
    /// Root Sentinel (top-level, Key = "Root") VARSA tüm çözümleme onun altından yapılır;
    /// YOKSA çözümleme top-level'den (RefParentId == null) yapılır. Böylece Root'lu ve
    /// Root'suz yapı birlikte desteklenir; kullanıcı C1'den elle parent item ekleyebilir.
    /// </summary>
    public static class KeyTreeStoreManager
    {

        #region --- Yardımcı Metotlar ---

        /// <summary>
        /// Yolu normalize eder: kenardaki "/" ve boş segmentleri temizler.
        /// "/SMTP Settings/Password" -> ["SMTP Settings", "Password"].
        /// </summary>
        private static string[] NormalizePath(string path)
        {
            return path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Varsa Root Sentinel'in Id'sini döndürür (top-level, Key = "Root", harf duyarsız).
        /// Yoksa null döner -> çözümleme top-level'den (RefParentId == null) yapılır.
        /// </summary>
        private static string ResolveRootId(DataConnection connection)
        {
            var root = connection.Get<AuthKit.KeyTreeStore.Data.KeyTreeItem>()
                .FirstOrDefault(s => s.RefParentId == null && string.Equals(s.Key, "Root", StringComparison.OrdinalIgnoreCase));
            return root != null ? root.Id : null;
        }

        /// <summary>
        /// Yolun SON segmenti hariç tüm segmentleri çözer ve son segmentin parentId'sini döndürür.
        /// - Root Sentinel varsa ve yol "Root/..." ile başlıyorsa ilk segment (Root) atlanır;
        ///   sentinel yoksa "Root" top-level bir grup olarak oluşturulur (Root'lu yapı isteğe bağlı kurulur).
        /// - createMissing=true ise eksik ara düğümler oluşturulur; false ise yol yoksa false döner.
        /// </summary>
        private static bool ResolvePathParent(DataConnection connection, string[] pathParts, bool createMissing, out string parentId)
        {
            parentId = ResolveRootId(connection);

            int startIndex = 0;
            if (parentId != null && pathParts.Length > 0 &&
                string.Equals(pathParts[0], "Root", StringComparison.OrdinalIgnoreCase))
            {
                startIndex = 1; // sentinel zaten Root; açık "Root" segmentini atla
            }

            for (int i = startIndex; i < pathParts.Length - 1; i++)
            {
                var item = connection.Get<AuthKit.KeyTreeStore.Data.KeyTreeItem>()
                    .FirstOrDefault(s => s.RefParentId == parentId && s.Key == pathParts[i]);
                if (item == null)
                {
                    if (!createMissing) return false; // yol yok
                    item = DataFacade.BuildNew<AuthKit.KeyTreeStore.Data.KeyTreeItem>();
                    item.Key = pathParts[i];
                    item.Value = string.Empty; // ara düğümler gruptur; değeri olmaz
                    item.RefParentId = parentId;
                    item = DataFacade.AddNew(item);
                }
                parentId = item.Id.ToString();
            }
            return true;
        }

        /// <summary>
        /// Root sentinel'in (top-level, Key = "Root") var olduğundan emin olur; yoksa oluşturur.
        /// C1 Data sekmesinde tüm parent/child'lar tek kök altında toplansın diye başlangıçta çağrılır.
        /// </summary>
        public static void EnsureRoot()
        {
            using (var connection = new DataConnection())
            {
                if (ResolveRootId(connection) != null) return;
                var root = DataFacade.BuildNew<AuthKit.KeyTreeStore.Data.KeyTreeItem>();
                root.Key = "Root";
                root.Value = string.Empty; // root değeri olmaz
                root.RefParentId = null;
                DataFacade.AddNew(root);
            }
        }

        #endregion --- Yardımcı Metotlar ---

        #region --- Ayar Okuma (Read) ---

        /// <summary>
        /// Belirtilen yoldaki TEK bir ayarın değerini getirir.
        /// Eğer aynı yolda birden fazla ayar varsa, sadece ilk bulduğunu döndürür.
        /// </summary>
        public static T GetValue<T>(string path, T defaultValue = default(T))
        {
            if (string.IsNullOrWhiteSpace(path)) return defaultValue;

            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0) return defaultValue;

            using (var connection = new DataConnection())
            {
                string parentId;
                if (!ResolvePathParent(connection, pathParts, false, out parentId)) return defaultValue;

                // Son segmenti (asıl anahtarı) kullanarak ayarı bul.
                string key = pathParts[pathParts.Length - 1];
                var item = connection.Get<AuthKit.KeyTreeStore.Data.KeyTreeItem>().FirstOrDefault(s => s.RefParentId == parentId && s.Key == key);

                if (item != null && !string.IsNullOrEmpty(item.Value))
                {
                    try
                    {
                        return (T)Convert.ChangeType(item.Value, typeof(T));
                    }
                    catch
                    {
                        return defaultValue;
                    }
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Belirtilen yoldaki TÜM ayarların değerlerini bir liste olarak getirir.
        /// Bu metot, aynı anahtar altında birden fazla değer saklamak için kullanılır.
        /// </summary>
        public static List<T> GetValues<T>(string path)
        {
            var values = new List<T>();
            if (string.IsNullOrWhiteSpace(path)) return values;

            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0) return values;

            using (var connection = new DataConnection())
            {
                string parentId;
                if (!ResolvePathParent(connection, pathParts, false, out parentId)) return values;

                // Son segmenti (asıl anahtarı) kullanarak TÜM eşleşen ayarları bul.
                string key = pathParts[pathParts.Length - 1];
                var items = connection.Get<AuthKit.KeyTreeStore.Data.KeyTreeItem>().Where(s => s.RefParentId == parentId && s.Key == key).ToList();

                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.Value))
                    {
                        try
                        {
                            values.Add((T)Convert.ChangeType(item.Value, typeof(T)));
                        }
                        catch
                        {
                            // Tip dönüşümü başarısız olanları atla
                        }
                    }
                }
            }
            return values;
        }


        /// <summary>
        /// Belirtilen bir düğümün (grup/key) ALTINDAKİ tüm ayarların anahtar-değer çiftlerini getirir.
        /// Otomatik temizlik gibi işlemler için kullanılır.
        /// </summary>
        /// <param name="path">Ayarların hiyerarşik yolu.</param>
        /// <returns>Anahtar ve Değer içeren bir KeyValuePair listesi.</returns>
        public static List<KeyValuePair<string, string>> GetKeyValuePairsByPath(string path)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(path)) return pairs;

            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0) return pairs;

            using (var connection = new DataConnection())
            {
                // Yolun son segmentine kadar çöz, sonra o düğümü bul.
                string parentId;
                if (!ResolvePathParent(connection, pathParts, false, out parentId)) return pairs;

                string key = pathParts[pathParts.Length - 1];
                var node = connection.Get<AuthKit.KeyTreeStore.Data.KeyTreeItem>().FirstOrDefault(s => s.RefParentId == parentId && s.Key == key);
                if (node == null) return pairs;

                // O düğümün altındaki tüm çocukları al ve listeye ekle.
                var children = connection.Get<AuthKit.KeyTreeStore.Data.KeyTreeItem>().Where(s => s.RefParentId == node.Id).ToList();
                foreach (var child in children)
                {
                    pairs.Add(new KeyValuePair<string, string>(child.Key, child.Value));
                }
            }
            return pairs;
        }

        #endregion --- Ayar Okuma (Read) ---

        #region --- Ayar Ekleme ve Güncelleme (Create & Update) ---

        /// <summary>
        /// Belirtilen yola YENİ bir ayar ekler.
        /// Bu metot, aynı anahtar altında mükerrer kayıtlara izin verir.
        /// </summary>
        public static void AddValue(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Ayar yolu boş olamaz.", nameof(path));

            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0)
                throw new ArgumentException("Ayar yolu boş olamaz.", nameof(path));

            using (var connection = new DataConnection())
            {
                // Yolun son parçası hariç tüm klasör/grup yapısını bul veya oluştur.
                string parentId;
                ResolvePathParent(connection, pathParts, true, out parentId);

                // Her zaman yeni bir ayar oluştur.
                string key = pathParts[pathParts.Length - 1];
                var newItem = DataFacade.BuildNew<AuthKit.KeyTreeStore.Data.KeyTreeItem>();
                //newItem.Id = Guid.NewGuid();
                newItem.Key = key;
                newItem.Value = value?.ToString() ?? string.Empty;
                newItem.RefParentId = parentId;
                DataFacade.AddNew(newItem);
            }
        }

        /// <summary>
        /// Belirtilen yoldaki bir ayarı günceller veya yoksa oluşturur (UPSERT).
        /// Eğer aynı yolda birden fazla ayar varsa, sadece ilk bulduğunu günceller.
        /// </summary>
        public static void SetValue(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Ayar yolu boş olamaz.", nameof(path));

            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0)
                throw new ArgumentException("Ayar yolu boş olamaz.", nameof(path));

            using (var connection = new DataConnection())
            {
                // Yolun son parçası hariç tüm klasör/grup yapısını bul veya oluştur.
                string parentId;
                ResolvePathParent(connection, pathParts, true, out parentId);

                string key = pathParts[pathParts.Length - 1];
                var existing = connection.Get<AuthKit.KeyTreeStore.Data.KeyTreeItem>().FirstOrDefault(s => s.RefParentId == parentId && s.Key == key);

                if (existing != null)
                {
                    // Ayar var, güncelle.
                    existing.Value = value?.ToString() ?? string.Empty;
                    DataFacade.Update(existing);
                }
                else
                {
                    // Ayar yok, oluştur.
                    var newItem = DataFacade.BuildNew<AuthKit.KeyTreeStore.Data.KeyTreeItem>();
                    //newItem.Id = Guid.NewGuid();
                    newItem.Key = key;
                    newItem.Value = value?.ToString() ?? string.Empty;
                    newItem.RefParentId = parentId;
                    DataFacade.AddNew(newItem);
                }
            }
        }

        /// <summary>
        /// Belirtilen yoldaki TÜM mevcut ayarları siler ve yerine verilen YENİ değerleri ekler.
        /// Bir anahtar altındaki listeyi komple yeniden yazmak için kullanılır.
        /// </summary>
        /// <param name="path">Ayarların hiyerarşik yolu. Örnek: "Guvenlik/IzinVerilenIPler"</param>
        /// <param name="newValues">Eklenecek yeni değerlerin listesi.</param>
        /// <example>
        /// Bu metot, bir anahtar altındaki tüm eski değerleri silip yenileriyle değiştirmek için kullanılır.
        /// <code>
        /// // Örnek: "IzinVerilenIPler" listesini temizleyip sadece iki yeni IP adresi eklemek.
        ///
        /// // Yeni IP listemizi hazırlıyoruz.
        /// var yeniIpListesi = new List<string> { "1.1.1.1", "2.2.2.2" };
        ///
        /// // ReplaceAllValues metodunu çağırıyoruz.
        /// KeyTreeStoreManager.ReplaceAllValues("Guvenlik/IzinVerilenIPler", yeniIpListesi);
        ///
        /// // Bu işlemden sonra "Guvenlik/IzinVerilenIPler" altında sadece "1.1.1.1" ve "2.2.2.2" kalacaktır.
        /// // Eski IP'lerin hepsi silinmiş olur.
        /// </code>
        /// </example>
        public static void ReplaceAllValues(string path, IEnumerable<object> newValues)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Ayar yolu boş olamaz.", nameof(path));

            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0)
                throw new ArgumentException("Ayar yolu boş olamaz.", nameof(path));

            using (var connection = new DataConnection())
            {
                // 1. Adım: Yolun son segmentine kadar çöz (yoksa yapılacak bir şey yok).
                string parentId;
                if (!ResolvePathParent(connection, pathParts, false, out parentId)) return;

                string key = pathParts[pathParts.Length - 1];
                var oldItems = connection.Get<AuthKit.KeyTreeStore.Data.KeyTreeItem>().Where(s => s.RefParentId == parentId && s.Key == key).ToList();

                // 2. Adım: Bulunan TÜM eski ayarları sil.
                foreach (var oldItem in oldItems)
                {
                    connection.Delete(oldItem);
                }

                // 3. Adım: Verilen YENİ değerleri listeye tek tek ekle.
                foreach (var newValue in newValues)
                {
                    var newItem = DataFacade.BuildNew<AuthKit.KeyTreeStore.Data.KeyTreeItem>();
                    //newItem.Id = Guid.NewGuid();
                    newItem.Key = key;
                    newItem.Value = newValue?.ToString() ?? string.Empty;
                    newItem.RefParentId = parentId;
                    connection.Add(newItem);
                }
            }
        }

        #endregion

        #region --- Ayar Silme (Delete) ---

        /// <summary>
        /// Belirtilen yoldaki ve değere sahip TEK bir ayarı siler.
        /// </summary>
        public static void DeleteValue(string path, object valueToDelete)
        {
            if (string.IsNullOrWhiteSpace(path) || valueToDelete == null) return;

            List<AuthKit.KeyTreeStore.Data.KeyTreeItem> items = GetItemsByPath(path);
            string valueStr = valueToDelete.ToString();

            var itemToDelete = items.FirstOrDefault(s => s.Value == valueStr);

            if (itemToDelete != null)
            {
                DataFacade.Delete(itemToDelete);
            }
        }

        /// <summary>
        /// Belirtilen yoldaki TÜM ayarları siler.
        /// </summary>
        public static void DeleteAllValues(string path)
        {
            List<AuthKit.KeyTreeStore.Data.KeyTreeItem> items = GetItemsByPath(path);

            foreach (var item in items)
            {
                DataFacade.Delete(item);
            }
        }

        /// <summary>
        /// Belirtilen yoldaki tüm ayarları döndürür (silme işlemleri için yardımcı).
        /// </summary>
        private static List<AuthKit.KeyTreeStore.Data.KeyTreeItem> GetItemsByPath(string path)
        {
            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0) return new List<AuthKit.KeyTreeStore.Data.KeyTreeItem>();

            using (var connection = new DataConnection())
            {
                string parentId;
                if (!ResolvePathParent(connection, pathParts, false, out parentId)) return new List<AuthKit.KeyTreeStore.Data.KeyTreeItem>();

                // Son anahtara uyan tüm ayarları bul ve döndür.
                string key = pathParts[pathParts.Length - 1];
                return connection.Get<AuthKit.KeyTreeStore.Data.KeyTreeItem>().Where(s => s.RefParentId == parentId && s.Key == key).ToList();
            }
        }

        #endregion

        #region --- Flat Key/Value Kolaylık (C1 Geneli) ---

        /// <summary>
        /// Key bazlı basit okuma (grupsuz). Bulunamazsa null döner.
        /// Örnek: Get("Auth.LoginPageId")
        /// </summary>
        public static string Get(string key)
        {
            return GetValue<string>(key, null);
        }

        /// <summary>
        /// Key bazlı basit okuma; bulunamazsa defaultValue döner.
        /// Örnek: Get("Auth.LoginPageId", "")
        /// </summary>
        public static string Get(string key, string defaultValue)
        {
            return GetValue(key, defaultValue);
        }

        /// <summary>
        /// Key bazlı basit yazma (UPSERT). Tek parçalı key'ler için ("Auth.LoginPageId").
        /// </summary>
        public static bool Set(string key, string value)
        {
            try
            {
                SetValue(key, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Key bazlı silme. Tek parçalı key'ler için ("Auth.LoginPageId").
        /// </summary>
        public static bool Delete(string key)
        {
            try
            {
                DeleteAllValues(key);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
