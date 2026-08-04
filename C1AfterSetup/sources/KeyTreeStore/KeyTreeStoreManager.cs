using Composite.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyTreeStoreKit
{
    /// <summary>
    /// HiyerarÅŸik key/value store (KeyTreeStoreKit.Data.KeyTreeItem tablosu).
    ///
    /// Path tabanlÄ± Ã§alÄ±ÅŸÄ±r; ayraÃ§ "/" tir. Hibrit grup/key eriÅŸimi desteklenir:
    ///   "Root/SMTP Settings/Password"  -> aÃ§Ä±k Root
    ///   "/SMTP Settings/Password"      -> baÅŸtaki / Root anlamÄ±na gelir (otomatik temizlenir)
    ///   "SMTP Settings/Password"       -> Root yazÄ±lmadan da Ã§alÄ±ÅŸÄ±r
    ///
    /// Root Sentinel (top-level, Key = "Root") VARSA tÃ¼m Ã§Ã¶zÃ¼mleme onun altÄ±ndan yapÄ±lÄ±r;
    /// YOKSA Ã§Ã¶zÃ¼mleme top-level'den (RefParentId == null) yapÄ±lÄ±r. BÃ¶ylece Root'lu ve
    /// Root'suz yapÄ± birlikte desteklenir; kullanÄ±cÄ± C1'den elle parent item ekleyebilir.
    /// </summary>
    public static class KeyTreeStoreManager
    {

        #region --- YardÄ±mcÄ± Metotlar ---

        /// <summary>
        /// Yolu normalize eder: kenardaki "/" ve boÅŸ segmentleri temizler.
        /// "/SMTP Settings/Password" -> ["SMTP Settings", "Password"].
        /// </summary>
        private static string[] NormalizePath(string path)
        {
            return path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Varsa Root Sentinel'in Id'sini dÃ¶ndÃ¼rÃ¼r (top-level, Key = "Root", harf duyarsÄ±z).
        /// Yoksa null dÃ¶ner -> Ã§Ã¶zÃ¼mleme top-level'den (RefParentId == null) yapÄ±lÄ±r.
        /// </summary>
        private static string ResolveRootId(DataConnection connection)
        {
            var root = connection.Get<KeyTreeStoreKit.Data.KeyTreeItem>()
                .FirstOrDefault(s => s.RefParentId == null && string.Equals(s.Key, "Root", StringComparison.OrdinalIgnoreCase));
            return root != null ? root.Id : null;
        }

        /// <summary>
        /// Yolun SON segmenti hariÃ§ tÃ¼m segmentleri Ã§Ã¶zer ve son segmentin parentId'sini dÃ¶ndÃ¼rÃ¼r.
        /// - Root Sentinel varsa ve yol "Root/..." ile baÅŸlÄ±yorsa ilk segment (Root) atlanÄ±r;
        ///   sentinel yoksa "Root" top-level bir grup olarak oluÅŸturulur (Root'lu yapÄ± isteÄŸe baÄŸlÄ± kurulur).
        /// - createMissing=true ise eksik ara dÃ¼ÄŸÃ¼mler oluÅŸturulur; false ise yol yoksa false dÃ¶ner.
        /// </summary>
        private static bool ResolvePathParent(DataConnection connection, string[] pathParts, bool createMissing, out string parentId)
        {
            parentId = ResolveRootId(connection);

            int startIndex = 0;
            if (parentId != null && pathParts.Length > 0 &&
                string.Equals(pathParts[0], "Root", StringComparison.OrdinalIgnoreCase))
            {
                startIndex = 1; // sentinel zaten Root; aÃ§Ä±k "Root" segmentini atla
            }

            for (int i = startIndex; i < pathParts.Length - 1; i++)
            {
                string currentParentId = parentId; // out parametre lambda iÃ§inde kullanÄ±lamaz; yerel kopyaya al
                var item = connection.Get<KeyTreeStoreKit.Data.KeyTreeItem>()
                    .FirstOrDefault(s => s.RefParentId == currentParentId && s.Key == pathParts[i]);
                if (item == null)
                {
                    if (!createMissing) return false; // yol yok
                    item = DataFacade.BuildNew<KeyTreeStoreKit.Data.KeyTreeItem>();
                    item.Key = pathParts[i];
                    item.Value = string.Empty; // ara dÃ¼ÄŸÃ¼mler gruptur; deÄŸeri olmaz
                    item.RefParentId = parentId;
                    item = DataFacade.AddNew(item);
                }
                parentId = item.Id.ToString();
            }
            return true;
        }

        /// <summary>
        /// Root sentinel'in (top-level, Key = "Root") var olduÄŸundan emin olur; yoksa oluÅŸturur.
        /// C1 Data sekmesinde tÃ¼m parent/child'lar tek kÃ¶k altÄ±nda toplansÄ±n diye baÅŸlangÄ±Ã§ta Ã§aÄŸrÄ±lÄ±r.
        /// </summary>
        public static void EnsureRoot()
        {
            using (var connection = new DataConnection())
            {
                if (ResolveRootId(connection) != null) return;
                var root = DataFacade.BuildNew<KeyTreeStoreKit.Data.KeyTreeItem>();
                root.Key = "Root";
                root.Value = string.Empty; // root deÄŸeri olmaz
                root.RefParentId = null;
                DataFacade.AddNew(root);
            }
        }

        #endregion --- YardÄ±mcÄ± Metotlar ---

        #region --- Ayar Okuma (Read) ---

        /// <summary>
        /// Belirtilen yoldaki TEK bir ayarÄ±n deÄŸerini getirir.
        /// EÄŸer aynÄ± yolda birden fazla ayar varsa, sadece ilk bulduÄŸunu dÃ¶ndÃ¼rÃ¼r.
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

                // Son segmenti (asÄ±l anahtarÄ±) kullanarak ayarÄ± bul.
                string key = pathParts[pathParts.Length - 1];
                var item = connection.Get<KeyTreeStoreKit.Data.KeyTreeItem>().FirstOrDefault(s => s.RefParentId == parentId && s.Key == key);

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
        /// Belirtilen yoldaki TÃœM ayarlarÄ±n deÄŸerlerini bir liste olarak getirir.
        /// Bu metot, aynÄ± anahtar altÄ±nda birden fazla deÄŸer saklamak iÃ§in kullanÄ±lÄ±r.
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

                // Son segmenti (asÄ±l anahtarÄ±) kullanarak TÃœM eÅŸleÅŸen ayarlarÄ± bul.
                string key = pathParts[pathParts.Length - 1];
                var items = connection.Get<KeyTreeStoreKit.Data.KeyTreeItem>().Where(s => s.RefParentId == parentId && s.Key == key).ToList();

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
                            // Tip dÃ¶nÃ¼ÅŸÃ¼mÃ¼ baÅŸarÄ±sÄ±z olanlarÄ± atla
                        }
                    }
                }
            }
            return values;
        }


        /// <summary>
        /// Belirtilen bir dÃ¼ÄŸÃ¼mÃ¼n (grup/key) ALTINDAKÄ° tÃ¼m ayarlarÄ±n anahtar-deÄŸer Ã§iftlerini getirir.
        /// Otomatik temizlik gibi iÅŸlemler iÃ§in kullanÄ±lÄ±r.
        /// </summary>
        /// <param name="path">AyarlarÄ±n hiyerarÅŸik yolu.</param>
        /// <returns>Anahtar ve DeÄŸer iÃ§eren bir KeyValuePair listesi.</returns>
        public static List<KeyValuePair<string, string>> GetKeyValuePairsByPath(string path)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(path)) return pairs;

            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0) return pairs;

            using (var connection = new DataConnection())
            {
                // Yolun son segmentine kadar Ã§Ã¶z, sonra o dÃ¼ÄŸÃ¼mÃ¼ bul.
                string parentId;
                if (!ResolvePathParent(connection, pathParts, false, out parentId)) return pairs;

                string key = pathParts[pathParts.Length - 1];
                var node = connection.Get<KeyTreeStoreKit.Data.KeyTreeItem>().FirstOrDefault(s => s.RefParentId == parentId && s.Key == key);
                if (node == null) return pairs;

                // O dÃ¼ÄŸÃ¼mÃ¼n altÄ±ndaki tÃ¼m Ã§ocuklarÄ± al ve listeye ekle.
                var children = connection.Get<KeyTreeStoreKit.Data.KeyTreeItem>().Where(s => s.RefParentId == node.Id).ToList();
                foreach (var child in children)
                {
                    pairs.Add(new KeyValuePair<string, string>(child.Key, child.Value));
                }
            }
            return pairs;
        }

        #endregion --- Ayar Okuma (Read) ---

        #region --- Ayar Ekleme ve GÃ¼ncelleme (Create & Update) ---

        /// <summary>
        /// Belirtilen yola YENÄ° bir ayar ekler.
        /// Bu metot, aynÄ± anahtar altÄ±nda mÃ¼kerrer kayÄ±tlara izin verir.
        /// </summary>
        public static void AddValue(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Ayar yolu boÅŸ olamaz.", nameof(path));

            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0)
                throw new ArgumentException("Ayar yolu boÅŸ olamaz.", nameof(path));

            using (var connection = new DataConnection())
            {
                // Yolun son parÃ§asÄ± hariÃ§ tÃ¼m klasÃ¶r/grup yapÄ±sÄ±nÄ± bul veya oluÅŸtur.
                string parentId;
                ResolvePathParent(connection, pathParts, true, out parentId);

                // Her zaman yeni bir ayar oluÅŸtur.
                string key = pathParts[pathParts.Length - 1];
                var newItem = DataFacade.BuildNew<KeyTreeStoreKit.Data.KeyTreeItem>();
                //newItem.Id = Guid.NewGuid();
                newItem.Key = key;
                newItem.Value = value?.ToString() ?? string.Empty;
                newItem.RefParentId = parentId;
                DataFacade.AddNew(newItem);
            }
        }

        /// <summary>
        /// Belirtilen yoldaki bir ayarÄ± gÃ¼nceller veya yoksa oluÅŸturur (UPSERT).
        /// EÄŸer aynÄ± yolda birden fazla ayar varsa, sadece ilk bulduÄŸunu gÃ¼nceller.
        /// </summary>
        public static void SetValue(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Ayar yolu boÅŸ olamaz.", nameof(path));

            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0)
                throw new ArgumentException("Ayar yolu boÅŸ olamaz.", nameof(path));

            using (var connection = new DataConnection())
            {
                // Yolun son parÃ§asÄ± hariÃ§ tÃ¼m klasÃ¶r/grup yapÄ±sÄ±nÄ± bul veya oluÅŸtur.
                string parentId;
                ResolvePathParent(connection, pathParts, true, out parentId);

                string key = pathParts[pathParts.Length - 1];
                var existing = connection.Get<KeyTreeStoreKit.Data.KeyTreeItem>().FirstOrDefault(s => s.RefParentId == parentId && s.Key == key);

                if (existing != null)
                {
                    // Ayar var, gÃ¼ncelle.
                    existing.Value = value?.ToString() ?? string.Empty;
                    DataFacade.Update(existing);
                }
                else
                {
                    // Ayar yok, oluÅŸtur.
                    var newItem = DataFacade.BuildNew<KeyTreeStoreKit.Data.KeyTreeItem>();
                    //newItem.Id = Guid.NewGuid();
                    newItem.Key = key;
                    newItem.Value = value?.ToString() ?? string.Empty;
                    newItem.RefParentId = parentId;
                    DataFacade.AddNew(newItem);
                }
            }
        }

        /// <summary>
        /// Belirtilen yoldaki TÃœM mevcut ayarlarÄ± siler ve yerine verilen YENÄ° deÄŸerleri ekler.
        /// Bir anahtar altÄ±ndaki listeyi komple yeniden yazmak iÃ§in kullanÄ±lÄ±r.
        /// </summary>
        /// <param name="path">AyarlarÄ±n hiyerarÅŸik yolu. Ã–rnek: "Guvenlik/IzinVerilenIPler"</param>
        /// <param name="newValues">Eklenecek yeni deÄŸerlerin listesi.</param>
        /// <example>
        /// Bu metot, bir anahtar altÄ±ndaki tÃ¼m eski deÄŸerleri silip yenileriyle deÄŸiÅŸtirmek iÃ§in kullanÄ±lÄ±r.
        /// <code>
        /// // Ã–rnek: "IzinVerilenIPler" listesini temizleyip sadece iki yeni IP adresi eklemek.
        ///
        /// // Yeni IP listemizi hazÄ±rlÄ±yoruz.
        /// var yeniIpListesi = new List<string> { "1.1.1.1", "2.2.2.2" };
        ///
        /// // ReplaceAllValues metodunu Ã§aÄŸÄ±rÄ±yoruz.
        /// KeyTreeStoreManager.ReplaceAllValues("Guvenlik/IzinVerilenIPler", yeniIpListesi);
        ///
        /// // Bu iÅŸlemden sonra "Guvenlik/IzinVerilenIPler" altÄ±nda sadece "1.1.1.1" ve "2.2.2.2" kalacaktÄ±r.
        /// // Eski IP'lerin hepsi silinmiÅŸ olur.
        /// </code>
        /// </example>
        public static void ReplaceAllValues(string path, IEnumerable<object> newValues)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Ayar yolu boÅŸ olamaz.", nameof(path));

            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0)
                throw new ArgumentException("Ayar yolu boÅŸ olamaz.", nameof(path));

            using (var connection = new DataConnection())
            {
                // 1. AdÄ±m: Yolun son segmentine kadar Ã§Ã¶z (yoksa yapÄ±lacak bir ÅŸey yok).
                string parentId;
                if (!ResolvePathParent(connection, pathParts, false, out parentId)) return;

                string key = pathParts[pathParts.Length - 1];
                var oldItems = connection.Get<KeyTreeStoreKit.Data.KeyTreeItem>().Where(s => s.RefParentId == parentId && s.Key == key).ToList();

                // 2. AdÄ±m: Bulunan TÃœM eski ayarlarÄ± sil.
                foreach (var oldItem in oldItems)
                {
                    connection.Delete(oldItem);
                }

                // 3. AdÄ±m: Verilen YENÄ° deÄŸerleri listeye tek tek ekle.
                foreach (var newValue in newValues)
                {
                    var newItem = DataFacade.BuildNew<KeyTreeStoreKit.Data.KeyTreeItem>();
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
        /// Belirtilen yoldaki ve deÄŸere sahip TEK bir ayarÄ± siler.
        /// </summary>
        public static void DeleteValue(string path, object valueToDelete)
        {
            if (string.IsNullOrWhiteSpace(path) || valueToDelete == null) return;

            List<KeyTreeStoreKit.Data.KeyTreeItem> items = GetItemsByPath(path);
            string valueStr = valueToDelete.ToString();

            var itemToDelete = items.FirstOrDefault(s => s.Value == valueStr);

            if (itemToDelete != null)
            {
                DataFacade.Delete(itemToDelete);
            }
        }

        /// <summary>
        /// Belirtilen yoldaki TÃœM ayarlarÄ± siler.
        /// </summary>
        public static void DeleteAllValues(string path)
        {
            List<KeyTreeStoreKit.Data.KeyTreeItem> items = GetItemsByPath(path);

            foreach (var item in items)
            {
                DataFacade.Delete(item);
            }
        }

        /// <summary>
        /// Belirtilen yoldaki tÃ¼m ayarlarÄ± dÃ¶ndÃ¼rÃ¼r (silme iÅŸlemleri iÃ§in yardÄ±mcÄ±).
        /// </summary>
        private static List<KeyTreeStoreKit.Data.KeyTreeItem> GetItemsByPath(string path)
        {
            var pathParts = NormalizePath(path);
            if (pathParts.Length == 0) return new List<KeyTreeStoreKit.Data.KeyTreeItem>();

            using (var connection = new DataConnection())
            {
                string parentId;
                if (!ResolvePathParent(connection, pathParts, false, out parentId)) return new List<KeyTreeStoreKit.Data.KeyTreeItem>();

                // Son anahtara uyan tÃ¼m ayarlarÄ± bul ve dÃ¶ndÃ¼r.
                string key = pathParts[pathParts.Length - 1];
                return connection.Get<KeyTreeStoreKit.Data.KeyTreeItem>().Where(s => s.RefParentId == parentId && s.Key == key).ToList();
            }
        }

        #endregion

        #region --- Flat Key/Value KolaylÄ±k (C1 Geneli) ---

        /// <summary>
        /// Key bazlÄ± basit okuma (grupsuz). Bulunamazsa null dÃ¶ner.
        /// Ã–rnek: Get("Auth.LoginPageId")
        /// </summary>
        public static string Get(string key)
        {
            return GetValue<string>(key, null);
        }

        /// <summary>
        /// Key bazlÄ± basit okuma; bulunamazsa defaultValue dÃ¶ner.
        /// Ã–rnek: Get("Auth.LoginPageId", "")
        /// </summary>
        public static string Get(string key, string defaultValue)
        {
            return GetValue(key, defaultValue);
        }

        /// <summary>
        /// Key bazlÄ± basit yazma (UPSERT). Tek parÃ§alÄ± key'ler iÃ§in ("Auth.LoginPageId").
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
        /// Key bazlÄ± silme. Tek parÃ§alÄ± key'ler iÃ§in ("Auth.LoginPageId").
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


