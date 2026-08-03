using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace C1AfterSetup
{
    /// <summary>
    /// Dosya eşitleme ve içerik imzası yardımcıları.
    ///
    /// Temel prensip: hedef dosya, kaynakla **içerik olarak aynıysa** yazılmaz (idempotent,
    /// hızlı atla); **farklıysa** üzerine yazılır (yenileme). Böylece script hata sonrası
    /// yeniden çalıştığında yalnızca bozuk/eksik/eski dosyalar güncellenir.
    /// (C# 5 uyumlu - .NET Framework msbuild ile derlenebilir.)
    /// </summary>
    public static class FileSyncUtil
    {
        /// <summary>Bir dosyanın MD5 içerik özeti (hex). Okunamazsa "?" döner; istisna fırlatmaz.</summary>
        public static string HashFile(string path)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var fs = File.OpenRead(path))
                {
                    return BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "");
                }
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>Bir metnin MD5 içerik özeti (hex).</summary>
        public static string HashText(string text)
        {
            using (var md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }

        /// <summary>İki dosyanın içerik olarak aynı olup olmadığı (boyut + hash).</summary>
        public static bool FilesEqual(string a, string b)
        {
            try
            {
                if (!File.Exists(a) || !File.Exists(b)) return false;
                if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
                return HashFile(a) == HashFile(b);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// src -> dst kopyalar; yalnızca içerik farklıysa yazar. Gerekli klasörleri oluşturur.
        /// Bir şey kopyalandıysa true, zaten aynıysa false döner.
        /// </summary>
        public static bool CopyIfDifferent(string src, string dst)
        {
            if (FilesEqual(src, dst)) return false;

            string dir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(src, dst, true);
            return true;
        }

        /// <summary>
        /// Bir dizin ağacını hedefe eşitler (yalnızca değişen dosyaları yazar).
        /// Değişen dosya sayısını döndürür.
        /// </summary>
        public static int SyncDirectory(string srcDir, string dstDir)
        {
            int changed = 0;
            foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(Path.GetFullPath(srcDir).Length).TrimStart('\\', '/');
                string dst = Path.Combine(dstDir, rel);
                if (CopyIfDifferent(file, dst)) changed++;
            }
            return changed;
        }

        /// <summary>
        /// Bir dosya/dizin listesinin toplu içerik imzası. Dizinlerde (dosya adı + hash) çiftleri
        /// sıralı birleştirilir. Kaynakların değişip değişmediğini anlamak için kullanılır.
        /// </summary>
        public static string SourceFingerprint(params string[] paths)
        {
            var parts = new List<string>();
            if (paths != null)
            {
                foreach (string p in paths)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    try
                    {
                        if (File.Exists(p))
                        {
                            parts.Add(Path.GetFileName(p) + ":" + HashFile(p));
                        }
                        else if (Directory.Exists(p))
                        {
                            foreach (string f in Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                            {
                                string rel = f.Substring(Path.GetFullPath(p).Length).TrimStart('\\', '/');
                                parts.Add(rel + ":" + HashFile(f));
                            }
                        }
                    }
                    catch
                    {
                        // Erişilemeyen kaynak imzaya katılmaz; verify zaten hedefi esas alır.
                    }
                }
            }

            parts.Sort(StringComparer.OrdinalIgnoreCase);

            StringBuilder sb = new StringBuilder();
            foreach (string part in parts) sb.Append(part).Append(';');

            using (var md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }
    }
}
