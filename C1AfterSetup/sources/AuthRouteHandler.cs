using System.Web;
using System.Web.Routing;

public class AuthRouteHandler : IRouteHandler
{
    public IHttpHandler GetHttpHandler(RequestContext requestContext)
    {
        HttpContext.Current.Items["RouteAction"] = requestContext.RouteData.Values["action"] as string;
        return new AuthApi();
    }
}
