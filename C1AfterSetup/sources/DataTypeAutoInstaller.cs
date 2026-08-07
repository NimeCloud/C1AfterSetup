using System;
using System.IO;
using System.Web.Hosting;
using System.Xml.Linq;
using Composite.Core.Application;
using Composite.Data.DynamicTypes;

/// <summary>
/// C1 [ApplicationStartup] hook'u: ~/App_Data/Composite/PendingDataTypes klasöründeki DataMetaData
/// XML'lerini okur ve C1'in RESMİ API'si (DynamicTypeManager.CreateStore) ile tipleri kaydeder.
///
/// Neden gerekli? DataMetaData klasörüne elle atılan (orpha/henüz kayıtlı olmayan) XML'ler,
/// başlatılmış bir sitede C1 tarafından silinir ve Composite.Generated.dll'e girmez. Bu hook,
/// tipleri resmi olarak kaydettirdiği için C1 bunları derler.
///
/// İş akışı:
///   1. Araç, XML'leri ~/App_Data/Composite/PendingDataTypes içine kopyalar.
///   2. Site ilk açıldığında bu hook devreye girer, her XML'i DataTypeDescriptor'a çevirir
///      ve DynamicTypeManager.CreateStore ile kaydeder.
///   3. Kayıt başarılıysa geçici XML silinir; C1 kapanışta Composite.Generated.dll'i
///      yeni tiplerle yeniden üretir.
/// </summary>
[ApplicationStartup]
public static class DataTypeAutoInstaller
{
    public static void OnInitialized()
    {
        string pendingFolder;
        try
        {
            pendingFolder = HostingEnvironment.MapPath("~/App_Data/Composite/PendingDataTypes");
        }
        catch
        {
            return;
        }
        if (string.IsNullOrEmpty(pendingFolder) || !Directory.Exists(pendingFolder))
        {
            return;
        }

        string[] files = Directory.GetFiles(pendingFolder, "*.xml", SearchOption.TopDirectoryOnly);
        if (files.Length == 0) return;

        foreach (string file in files)
        {
            try
            {
                var descriptor = DataTypeDescriptor.FromXml(XElement.Load(file));
                if (descriptor == null)
                {
                    Log("DataTypeAutoInstaller: DataTypeDescriptor.FromXml null döndü: " + Path.GetFileName(file));
                    continue;
                }

                bool created = false;
                try
                {
                    // C1'in resmi API'si ile tipi kaydet (store oluştur + derlemeyi tetikle).
                    DynamicTypeManager.CreateStore(descriptor, true);
                    created = true;
                }
                catch (Exception ex)
                {
                    // Store zaten var ya da tip DLL'de kayıtlı olabilir
                    Log("DataTypeAutoInstaller: CreateStore HATA (" + Path.GetFileName(file) + "): " + ex.Message);
                }

                if (!created)
                {
                    DataTypeDescriptor existing;
                    if (DynamicTypeManager.TryGetDataTypeDescriptor(descriptor.DataTypeId, out existing))
                    {
                        Type iface = existing.GetInterfaceType();
                        if (iface != null) DynamicTypeManager.EnsureCreateStore(iface);
                    }
                    else
                    {
                        Log("DataTypeAutoInstaller: TryGetDataTypeDescriptor false, tip bulunamadı: " + descriptor.Name);
                    }
                }

                File.Delete(file);
                Log("DataTypeAutoInstaller: " + Path.GetFileName(file) + " başarıyla işlendi.");
            }
            catch (Exception ex)
            {
                // Başarısız XML'leri klasörde bırak; tekrar denemesi için.
                Log("DataTypeAutoInstaller: GENEL HATA (" + Path.GetFileName(file) + "): " + ex.ToString());
            }
        }
    }

    private static void Log(string message)
    {
        try
        {
            string logDir = HostingEnvironment.MapPath("~/App_Data/Composite/LogFiles");
            if (!string.IsNullOrEmpty(logDir))
            {
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "DataTypeAutoInstaller.log");
                File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
        }
        catch
        {
            // log yazılamazsa sessizce devam et
        }
    }
}
