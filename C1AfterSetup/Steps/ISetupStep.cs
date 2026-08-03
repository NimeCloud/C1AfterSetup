namespace C1AfterSetup.Steps
{
    /// <summary>
    /// Kurulum pipeline'ındaki tek bir adım. Execute gerçek kurulumu yapar;
    /// Plan yalnızca dry-run raporlaması için adımı anlatır.
    ///
    /// Her adım idempotent olmalıdır (aynı siteye iki kez çalıştırılabilmeli) ve
    /// "verify-then-execute" sözleşmesine uymalıdır:
    ///   1. Pipeline önce Verify çağırır - hedef durum zaten tam uygulanmışsa true döner
    ///      ve adım atlanır.
    ///   2. Verify false dönerse Execute çalışır; içerik aynıysa yazmaz (FileSyncUtil),
    ///      farklıysa yeniler.
    ///   3. Fingerprint, adımın kaynak girdilerinin içerik imzasını döndürür (state'e yazılır).
    /// </summary>
    public interface ISetupStep
    {
        string Name { get; }

        /// <summary>
        /// Bu adımın hedef durumu disk üzerinde zaten tam olarak uygulanmışsa true döner
        /// (atlanabilir); herhangi bir dosya eksik/eskimişse false döner (yeniden uygulanmalı).
        /// Kaynak yoksa (kontrol edilemiyorsa) true dönmek güvenlidir.
        /// </summary>
        bool Verify(SetupContext context);

        /// <summary>
        /// Bu adımın kaynak girdilerinin içerik imzası (ör. FileSyncUtil.SourceFingerprint).
        /// Yalnızca bilgi/raporlama içindir; asıl karar Verify ile verilir.
        /// </summary>
        string Fingerprint(SetupContext context);

        bool Execute(SetupContext context);
        void Plan(SetupContext context);
    }
}
