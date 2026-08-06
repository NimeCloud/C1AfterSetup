using System.Web;
using System.Web.Routing;

public class ApiRouteHandler : IRouteHandler
{
    public IHttpHandler GetHttpHandler(RequestContext requestContext)
    {
        HttpContext.Current.Items["RouteAction"] = requestContext.RouteData.Values["action"] as string;
        HttpContext.Current.Items["RouteName"] = requestContext.RouteData.Values["name"] as string;
        return new ApiHandler();
    }
}
