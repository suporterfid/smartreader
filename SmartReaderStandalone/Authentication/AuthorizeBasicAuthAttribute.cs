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