using System.Net.Http.Headers;
using System.Text;

namespace SmartReaderStandalone.Authentication;

public class BasicAuthMiddleware
{
    private readonly IConfiguration configuration;
    private readonly ILogger<BasicAuthMiddleware> logger;
    private readonly RequestDelegate next;

    public BasicAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<BasicAuthMiddleware> logger)
    {
        this.next = next;
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        string authHeader = httpContext.Request.Headers["Authorization"];
        if (authHeader != null)
        {
            var authHeaderVal = AuthenticationHeaderValue.Parse(authHeader);
            if (authHeaderVal.Scheme.Equals("basic", StringComparison.OrdinalIgnoreCase) &&
                authHeaderVal.Parameter != null)
            {
                try
                {
                    var encoding = Encoding.GetEncoding("iso-8859-1");
                    var usernameAndPassword = encoding.GetString(Convert.FromBase64String(authHeaderVal.Parameter));
                    var username = usernameAndPassword.Split(new[] {':'})[0];
                    var password = usernameAndPassword.Split(new[] {':'})[1];
                    if (username == configuration.GetValue<string>("BasicAuth:UserName") &&
                        password == configuration.GetValue<string>("BasicAuth:Password"))
                        httpContext.Items["BasicAuth"] = true;
                }
                catch (Exception ex)
                {
                    logger.LogError($"{ex.Message}");
                }
            }
            else
            {
                httpContext.Response.StatusCode = 401;
                httpContext.Response.Headers.Add("WWW-Authenticate", string.Format("Basic realm=\"{0}\"", "R700"));
                return;
            }
        }
        else
        {
            httpContext.Response.StatusCode = 401;
            httpContext.Response.Headers.Add("WWW-Authenticate", string.Format("Basic realm=\"{0}\"", "R700"));
            return;
        }

        await next(httpContext).ConfigureAwait(false);
    }
}