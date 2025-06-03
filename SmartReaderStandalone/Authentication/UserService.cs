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
using SmartReaderStandalone.Entities;

namespace SmartReaderStandalone.Authentication;

public interface IUserService
{
    Task<User> Authenticate(string username, string password);
    Task<IEnumerable<User>> GetAll();
}

public class UserService : IUserService
{

    // users hardcoded for simplicity, store in a db with hashed passwords in production applications
    //private readonly List<User> _users = new()
    //{
    //    new User {Id = 1, FirstName = "Admin", LastName = "User", Username = "admin", Password = "admin"}
    //};
    public UserService()
    {
        try
        {
            using IHost host = Host.CreateDefaultBuilder().Build();

            IConfiguration config = host.Services.GetRequiredService<IConfiguration>();

            string basicAuthUserName = config.GetValue<string>("BasicAuth:UserName");

            string basicAuthPassword = config.GetValue<string>("BasicAuth:Password");

            var user = new User { Id = 1, FirstName = "Admin", LastName = "User", Username = basicAuthUserName, Password = basicAuthPassword };

            _users.Add(user);
        }
        catch (Exception)
        {


        }
    }
    private readonly List<User> _users = [];
    //{
    //    new User {Id = 1, FirstName = "Admin", LastName = "User", Username = "admin", Password = "admin"}
    //};

    public async Task<User> Authenticate(string username, string password)
    {

        // wrapped in "await Task.Run" to mimic fetching user from a db
        var user = await Task.Run(() => _users.SingleOrDefault(x => x.Username == username && x.Password == password));
        // on auth fail: null is returned because user is not found
        // on auth success: user object is returned
        return user;

    }

    public async Task<IEnumerable<User>> GetAll()
    {
        // wrapped in "await Task.Run" to mimic fetching users from a db
        return await Task.Run(() => _users);
    }
}