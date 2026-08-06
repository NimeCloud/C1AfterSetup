using System;
using System.Web;

public class ApiHandler : IHttpHandler
{
    public void ProcessRequest(HttpContext context)
    {
        context.Response.ContentType = "application/json";
        context.Response.Cache.SetCacheability(HttpCacheability.NoCache);

        var action = (context.Items["RouteAction"] as string)
                     ?? context.Request.QueryString["action"]
                     ?? "time";
        var name = (context.Items["RouteName"] as string)
                   ?? context.Request.QueryString["name"]
                   ?? "Guest";

        if (action == "time")
        {
            var now = DateTime.Now;
            var response = new
            {
                success = true,
                servertime = now.ToString("yyyy-MM-dd HH:mm:ss"),
                timezone = TimeZoneInfo.Local.DisplayName,
                timestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
                utc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
            context.Response.Write(Newtonsoft.Json.JsonConvert.SerializeObject(response));
        }
        else if (action == "hello")
        {
            var response = new
            {
                success = true,
                message = $"Merhaba {name}! C1 API calisiyor.",
                servertime = DateTime.Now.ToString("HH:mm:ss")
            };
            context.Response.Write(Newtonsoft.Json.JsonConvert.SerializeObject(response));
        }
        else if (action == "status")
        {
            var response = new
            {
                success = true,
                server = Environment.MachineName,
                runtime = Environment.Version.ToString(),
                authenticated = context.User?.Identity?.IsAuthenticated ?? false,
                username = context.User?.Identity?.Name ?? "anonymous"
            };
            context.Response.Write(Newtonsoft.Json.JsonConvert.SerializeObject(response));
        }
        else
        {
            context.Response.StatusCode = 400;
            context.Response.Write(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                success = false,
                error = $"Bilinmeyen action: '{action}'. Kullanilabilir: time, hello, status"
            }));
        }
    }

    public bool IsReusable => false;
}
