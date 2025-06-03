#region copyright
//****************************************************************************************************
// Copyright ©2025 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

namespace SmartReaderStandalone.Authentication;

public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public string Realm = "R700";

    [Obsolete]
    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock
    ) : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers["Authorization"].ToString();
        if (authHeader != null && authHeader.StartsWith("basic", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader.Substring("Basic ".Length).Trim();
            Console.WriteLine(token);
            var credentialstring = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var credentials = credentialstring.Split(':');
            if (credentials[0] == "admin" && credentials[1] == "admin")
            {
                var claims = new[] { new Claim("name", credentials[0]), new Claim(ClaimTypes.Role, "Admin") };
                var identity = new ClaimsIdentity(claims, "Basic");
                var claimsPrincipal = new ClaimsPrincipal(identity);
                return Task.FromResult(
                    AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name)));
            }

            Response.StatusCode = 401;
            // Response.Headers["WWW-Authenticate"] = "Basic realm=\"\", charset=\"UTF-8\"";
            //Response.Headers.WWWAuthenticate = "Newauth realm=\"apps\", type=1, title=\"Login to \"apps\", Basic realm=\"simple\"";
            Response.Headers.Append("WWW-Authenticate", string.Format("Basic realm=\"{0}\"", Realm));
            //HttpContext.Current.Response.StatusCode = 401;
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header."));
        }

        Response.StatusCode = 401;
        //Response.Headers.Add("WWW-Authenticate", "Newauth realm=\"apps\", type=1, title=\"Login to \"apps\", Basic realm=\"simple\"");
        //Response.Headers.WWWAuthenticate = "Newauth realm=\"apps\", type=1, title=\"Login to \"apps\", Basic realm=\"simple\"";

        Response.Headers.Append("WWW-Authenticate", string.Format("Basic realm=\"{0}\"", Realm));
        //Response.Headers["WWW-Authenticate"] = "Basic realm=\"\", charset=\"UTF-8\"";
        //HttpContext.Current.Response.StatusCode = 401;
        return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.Append("WWW-Authenticate", string.Format("Basic realm=\"{0}\"", Realm));
        //Response.Headers["WWW-Authenticate"] = "Basic realm=\"\", charset=\"UTF-8\"";
        return base.HandleChallengeAsync(properties);
    }
}