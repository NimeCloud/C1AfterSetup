using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace C1AfterSetup.Detect
{
    /// <summary>
    /// Online modda, bir fazın C1 tarafından "sindirildiğini" (derlenip kabul edildiğini) bekler.
    /// Üç sinyale bakar:
    ///   1. C1 log dizininde yeni hata yok,
    ///   2. (URL verilmişse) site HTTP 200 dönüyor (500 değil),
    ///   3. (DataMetaData fazında) ~/bin/Composite.Generated.dll güncellendi.
    /// (C# 5 uyumlu.)
    /// </summary>
    public class CompilationMonitor
    {
        private readonly SetupContext _context;
        private readonly SiteProbe _probe;
        private readonly int _timeoutSeconds;
        private readonly string _logDir;
        private readonly DateTime _generatedDllTime;

        public CompilationMonitor(SetupContext context, int timeoutSeconds = 180)
        {
            _context = context;
            _probe = new SiteProbe(context);
            _timeoutSeconds = timeoutSeconds;
            _logDir = context.ResolveSite(Path.Combine("App_Data", "Composite", "Log"));
            string generated = context.ResolveSite(Path.Combine("bin", "Composite.Generated.dll"));
            _generatedDllTime = File.Exists(generated) ? File.GetLastWriteTimeUtc(generated) : DateTime.MinValue;
        }

        /// <summary>
        /// C1'in fazı kabul etmesini bekler.
        /// </summary>
        /// <param name="expectGeneratedDllUpdate">DataMetaData fazı için true; Composite.Generated.dll zaman damgası takip edilir.</param>
        /// <param name="error">Başarısızlık nedeni.</param>
        public bool WaitUntilStable(bool expectGeneratedDllUpdate, out string error)
        {
            error = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(_timeoutSeconds);
            int attempts = 0;

            _context.Log("C1 derlemesi bekleniyor...");

            while (DateTime.UtcNow < deadline)
            {
                attempts++;

                string newErrors = GetNewErrors();
                if (!string.IsNullOrEmpty(newErrors))
                {
                    error = "C1 log'da yeni hata tespit edildi: " + newErrors;
                    return false;
                }

                string probeError;
                bool healthy = _probe.IsHealthy(out probeError);

                if (expectGeneratedDllUpdate)
                {
                    string generated = _context.ResolveSite(Path.Combine("bin", "Composite.Generated.dll"));
                    if (File.Exists(generated) && File.GetLastWriteTimeUtc(generated) > _generatedDllTime)
                    {
                        _context.Log("Composite.Generated.dll güncellendi (deneme " + attempts + ").");
                        return true;
                    }
                }
                else if (healthy)
                {
                    _context.Log("Site sağlıklı (deneme " + attempts + ").");
                    return true;
                }

                Thread.Sleep(3000);
            }

            error = "Zaman aşımı (" + _timeoutSeconds + " sn). C1 fazı tamamlayamadı. Log kontrol edin: " + _logDir;
            return false;
        }

        /// <summary>
        /// C1 log dosyalarındaki en güncel hata/exception satırını döndürür (yoksa null).
        /// Basit bir sezgisel; C1 log formatına göre geliştirilebilir.
        /// </summary>
        private string GetNewErrors()
        {
            if (!Directory.Exists(_logDir)) return null;

            var files = Directory.GetFiles(_logDir, "*.log", SearchOption.TopDirectoryOnly)
                                 .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                                 .Take(3);

            foreach (string file in files)
            {
                try
                {
                    string[] lines = File.ReadAllLines(file);
                    foreach (string line in lines.Reverse().Take(30))
                    {
                        if (line.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return line;
                        }
                    }
                }
                catch
                {
                    // Log okunamadıysa yok say
                }
            }
            return null;
        }
    }
}
