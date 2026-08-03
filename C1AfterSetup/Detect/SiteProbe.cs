using System;
using System.Net;

namespace C1AfterSetup.Detect
{
    /// <summary>
    /// Online modda sitenin ayakta ve sağlıklı olup olmadığını HTTP ile kontrol eder.
    /// C1 derleme hatası varsa tipik olarak HTTP 500 döner; bu da "henüz hazır değil" olarak yorumlanır.
    /// (C# 5 uyumlu.)
    /// </summary>
    public class SiteProbe
    {
        private readonly SetupContext _context;

        public SiteProbe(SetupContext context)
        {
            _context = context;
        }

        public bool HasUrl
        {
            get { return !string.IsNullOrWhiteSpace(_context.SiteUrl); }
        }

        public bool IsSiteReachable(out string error)
        {
            error = null;
            if (!HasUrl) { error = "Site URL tanımlı değil (-url verilmedi)"; return false; }
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "C1AfterSetup/1.0";
                    client.DownloadString(_context.SiteUrl);
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Sitenin sağlıklı (HTTP 200, 500 değil) olduğunu döndürür.
        /// URL verilmemişse true döner (HTTP kontrolü yapılmaz, dosya-temelli izlemeye bırakılır).
        /// </summary>
        public bool IsHealthy(out string error)
        {
            error = null;
            if (!HasUrl) return true;

            try
            {
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "C1AfterSetup/1.0";
                    client.DownloadString(_context.SiteUrl);
                    return true;
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null && (int)response.StatusCode == 500)
                {
                    error = "HTTP 500 (C1 hâlâ derliyor veya derleme hatası var)";
                    return false;
                }
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
