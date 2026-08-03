using System;
using System.Web;

public class HeaderCleanupModule : IHttpModule
{
    public void Init(HttpApplication context)
    {
        context.PreSendRequestHeaders += OnPreSendRequestHeaders;
    }

    private void OnPreSendRequestHeaders(object sender, EventArgs e)
    {
        if (HttpContext.Current == null)
        {
            return;
        }
        var response = HttpContext.Current.Response;

        string[] headersToRemove = {
            "X-Powered-By",
            "X-AspNet-Version",
            "X-AspNetMvc-Version",
            "X-SourceFiles",
            "Server"
        };

        foreach (string header in headersToRemove)
        {
            try
            {
                response.Headers.Remove(header);
            }
            catch
            {
                // Ignore errors
            }
        }
    }

    public void Dispose() { }
}
