using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SmartReaderStandalone.Authentication;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
//public class AuthorizeBasicAuthAttribute : Attribute, AuthorizeAttribute, IAuthorizationFilter
public class AuthorizeBasicAuthAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.Items["BasicAuth"] is not true) context.Result = new UnauthorizedResult();
    }
}