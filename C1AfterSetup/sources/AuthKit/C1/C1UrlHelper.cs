using Composite.Data;
using Composite.Data.Types;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace AuthKit.C1
{
    /// <summary>
    /// C1 CMS URL yardimci metotlari.
    /// </summary>
    public static class C1UrlHelper
    {
        private static readonly ConcurrentDictionary<Guid, string> _urlCache = new ConcurrentDictionary<Guid, string>();

        /// <summary>
        /// Verilen bir Guid (Sayfa ID) için C1'den sayfanın URL'sini alır.
        /// Performans için bulunan URL'ler bellekte cache'lenir.
        /// </summary>
        public static string GetUrlFromPageId(Guid pageId, string fallbackUrl = "#")
        {
            if (pageId == Guid.Empty) return fallbackUrl;

            if (_urlCache.TryGetValue(pageId, out string cachedUrl))
                return cachedUrl;

            try
            {
                var page = DataFacade.GetData<IPage>().FirstOrDefault(p => p.Id == pageId);
                if (page != null && !string.IsNullOrEmpty(page.UrlTitle))
                {
                    string url = "~/page(" + pageId + ")";
                    _urlCache[pageId] = url;
                    return url;
                }
            }
            catch
            {
                // Sayfa bulunamazsa fallback döner
            }

            return fallbackUrl;
        }
    }
}
