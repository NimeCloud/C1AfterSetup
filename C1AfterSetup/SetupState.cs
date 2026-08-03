using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace C1AfterSetup
{
    /// <summary>
    /// Bir kurulum adımının kalıcı durum kaydı (state.json içinde saklanır).
    /// </summary>
    public class StepRecord
    {
        /// <summary>Adımın görünen adı (ISetupStep.Name).</summary>
        public string Name { get; set; }

        /// <summary>Adım başarıyla tamamlandı mı?</summary>
        public bool Completed { get; set; }

        /// <summary>Son çalışmada bu adım hata verdi mi? (True ise bir sonraki çalışma verify atlayıp yeniden dener.)</summary>
        public bool Failed { get; set; }

        /// <summary>Son başarılı tamamlanma zamanı (yerel saat, "yyyy-MM-dd HH:mm:ss").</summary>
        public string CompletedAt { get; set; }

        /// <summary>
        /// Adımın kaynak girdilerinin içerik imzası. Kaynaklar değişince farklılaşır;
        /// kaynak değişimini raporlamak için kullanılır (gerçek karar hep hedef disk üzerinden verify ile verilir).
        /// </summary>
        public string Fingerprint { get; set; }

        public StepRecord()
        {
            Completed = false;
            Failed = false;
        }
    }

    /// <summary>
    /// Çalışma ilerlemesini hedef sitede kalıcı olarak saklar.
    ///
    /// Script bir adımda hata verip durursa, sonraki çalışma bu state'i okuyup:
    ///   - tamamlanmış adımları hedef disk üzerinden "verify" eder (dosyalar güncelse atlar, değilse yeniler),
    ///   - FAILED kayıtlı adımı verify'a takılmadan yeniden çalıştırır,
    ///   - böylece düzeltme sonrası yeniden çalıştırma hızlı ve güvenli olur.
    /// (C# 5 uyumlu - System.Web.Script.Serialization.JavaScriptSerializer kullanır.)
    /// </summary>
    public sealed class SetupState
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        /// <summary>State dosyasının site köküne göreli yolu.</summary>
        public const string StateRelativePath = @"App_Data\Composite\C1AfterSetup\state.json";

        public string Version { get; set; }
        public DateTime LastRunAt { get; set; }
        public List<StepRecord> Steps { get; set; }

        /// <summary>State dosyasının mutlak yolu (LoadOrCreate tarafından atanır; diske yazılmaz).</summary>
        [System.Web.Script.Serialization.ScriptIgnore]
        public string StateFilePath { get; private set; }

        public SetupState()
        {
            Version = "1";
            LastRunAt = DateTime.MinValue;
            Steps = new List<StepRecord>();
        }

        public static bool Exists(string sitePath)
        {
            return File.Exists(Path.Combine(sitePath, StateRelativePath));
        }

        /// <summary>
        /// Site için state'i yükler; yoksa (veya bozuksa) boş yeni state üretir.
        /// Bozuk state dosyası üzerine yazılmaz; güvenlik için olduğu gibi bırakılır.
        /// </summary>
        public static SetupState LoadOrCreate(string sitePath)
        {
            string file = Path.Combine(sitePath, StateRelativePath);
            var state = new SetupState { StateFilePath = file };

            if (File.Exists(file))
            {
                try
                {
                    SetupState loaded = Serializer.Deserialize<SetupState>(File.ReadAllText(file));
                    if (loaded != null)
                    {
                        state = loaded;
                        state.StateFilePath = file;
                        if (state.Steps == null) state.Steps = new List<StepRecord>();
                    }
                }
                catch
                {
                    // Bozuk/uyumsuz state.json -> temizden başla. Eski dosya korunur; hedef disk
                    // verify'ın tek doğruluk kaynağı olduğu için bu güvenlidir.
                }
            }

            return state;
        }

        public StepRecord GetOrAdd(string name)
        {
            foreach (StepRecord r in Steps)
            {
                if (r.Name == name) return r;
            }
            var rec = new StepRecord { Name = name };
            Steps.Add(rec);
            return rec;
        }

        public bool IsCompleted(string name)
        {
            foreach (StepRecord r in Steps)
                if (r.Name == name) return r.Completed;
            return false;
        }

        public bool IsFailed(string name)
        {
            foreach (StepRecord r in Steps)
                if (r.Name == name) return r.Failed;
            return false;
        }

        public void MarkCompleted(string name, string fingerprint)
        {
            StepRecord rec = GetOrAdd(name);
            rec.Completed = true;
            rec.Failed = false;
            rec.CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            rec.Fingerprint = fingerprint;
            Save();
        }

        public void MarkFailed(string name)
        {
            StepRecord rec = GetOrAdd(name);
            rec.Completed = false;
            rec.Failed = true;
            Save();
        }

        public void Save()
        {
            if (StateFilePath == null) return;
            try
            {
                LastRunAt = DateTime.Now;
                Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath));
                File.WriteAllText(StateFilePath, Serializer.Serialize(this));
            }
            catch
            {
                // State yazılamadıysa kurulum durmaz; sadece resume bilgisi kaybolur.
            }
        }
    }
}
