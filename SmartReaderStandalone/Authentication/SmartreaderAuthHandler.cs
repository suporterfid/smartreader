#region copyright
//****************************************************************************************************
// Copyright ©2023 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

namespace SmartReaderStandalone.Authentication;

public class SmartreaderAuthHandler : AuthenticationHandler<SmartreaderAuthSchemeOptions>
{
    public SmartreaderAuthHandler(
        IServiceProvider services,
        IOptionsMonitor<SmartreaderAuthSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
        Services = services;
    }

    public IServiceProvider Services { get; }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // handle authentication logic here

        try
        {
            // validation comes in here
            if (!Request.Headers.ContainsKey(HeaderNames.Authorization))
                return Task.FromResult(AuthenticateResult.Fail("Header Not Found."));
            using var scope = Services.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

            var authHeader = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"]);
            var credentialBytes = Convert.FromBase64String(authHeader.Parameter);
            var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
            var username = credentials[0];
            var password = credentials[1];

            // authenticate credentials with user service and attach user to http context
            var user = userService.Authenticate(username, password);
            // success branch
            // generate authTicket
            // authenticate the request
            if (user != null)
            {
                // create claims array from the model
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Email, "admin@smartreader.com"),
                    new Claim(ClaimTypes.Name, "Admin")
                };

                // generate claimsIdentity on the name of the class
                var claimsIdentity = new ClaimsIdentity(claims, nameof(SmartreaderAuthHandler));

                // generate AuthenticationTicket from the Identity
                // and current authentication scheme
                var ticket = new AuthenticationTicket(
                    new ClaimsPrincipal(claimsIdentity), Scheme.Name);

                // pass on the ticket to the middleware
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return Task.FromResult(AuthenticateResult.Fail("Model is Empty"));
        }
        catch
        {
            // do nothing if invalid auth header
            // user is not attached to context so request won't have access to secure routes
            return Task.FromResult(AuthenticateResult.Fail("Model is Empty"));
        }
    }
}