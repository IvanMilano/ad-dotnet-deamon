/*
 The MIT License (MIT)

Copyright (c) 2018 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using Microsoft.IdentityModel.Clients.ActiveDirectory;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Configuration;
// The following using statements were added for this sample.
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TodoListDaemon
{
    class Program
    {

        //
        // The Client ID is used by the application to uniquely identify itself to Azure AD.
        // The App Key is a credential used by the application to authenticate to Azure AD.
        // The Tenant is the name of the Azure AD tenant in which this application is registered.
        // The AAD Instance is the instance of Azure, for example public Azure or Azure China.
        // The Authority is the sign-in URL of the tenant.
        //
        private static string aadInstance = ConfigurationManager.AppSettings["ida:AADInstance"];
        private static string tenant = ConfigurationManager.AppSettings["ida:Tenant"];
        private static string appClientId = ConfigurationManager.AppSettings["ida:AppClientId"];
        private static string apiClientId = ConfigurationManager.AppSettings["ida:ApiClientId"];
        private static string appKey = ConfigurationManager.AppSettings["ida:AppKey"];
        private static string _user = ConfigurationManager.AppSettings["ida:User"];
        private static string _password = ConfigurationManager.AppSettings["ida:Password"];
        private static string _clientSecret = ConfigurationManager.AppSettings["ida:ApiKey"];
        private static string authority = String.Format(CultureInfo.InvariantCulture, aadInstance, tenant);

        //
        // To authenticate to the To Do list service, the client needs to know the service's App ID URI.
        // To contact the To Do list service we need it's URL as well.
        //
        private static string todoListBaseAddress = ConfigurationManager.AppSettings["ida:ApiEndpointBaseAddress"];

        private static HttpClient httpClient = new HttpClient();

        static void Main(string[] args)
        {
            //
            // Call the To Do service 10 times with short delay between calls.
            //

            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(3000);
                //PostTodo().Wait();
                Thread.Sleep(3000);
                GetTodo().Wait();
            }
        }

        static async Task PostTodo()
        {
            //
            // Get an access token from Azure AD using client credentials.
            // If the attempt to get a token fails because the server is unavailable, retry twice after 3 seconds each.
            //

            //var accessToken = await GetTokenWithClientCredential();
            //var accessToken = await GetTokenWithUserPasswordCredential();
            var accessToken = await GetTokenWithUserPasswordClientSecretCredential();

            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("Canceling attempt to contact To Do list service.\n");
                return;
            }

            // Add the access token to the authorization header of the request.
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Forms encode To Do item and POST to the todo list web api.
            string timeNow = DateTime.Now.ToString();
            Console.WriteLine("Posting to To Do list at {0}", timeNow);
            string todoText = "Task at time: " + timeNow;
            HttpContent content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Title", todoText) });
            HttpResponseMessage response = await httpClient.PostAsync(todoListBaseAddress + "/api/todolist", content);

            if (response.IsSuccessStatusCode == true)
            {
                Console.WriteLine("Successfully posted new To Do item:  {0}\n", todoText);
            }
            else
            {
                Console.WriteLine("Failed to post a new To Do item\nError:  {0}\n", response.ReasonPhrase);
            }
        }

        static async Task GetTodo()
        {
            //var accessToken = await GetTokenWithClientCredential();
            //var accessToken = await GetTokenWithUserPasswordCredential();
            var accessToken = await GetTokenWithUserPasswordClientSecretCredential();
            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("Canceling attempt to contact To Do list service.\n");
                return;
            }

            //
            // Read items from the To Do list service.
            //

            // Add the access token to the authorization header of the request.
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // Call the To Do list service.
            Console.WriteLine("Retrieving To Do list at {0}", DateTime.Now.ToString());
            HttpResponseMessage response = await httpClient.GetAsync(todoListBaseAddress + "/api/todolist");

            if (response.IsSuccessStatusCode)
            {
                // Read the response and output it to the console.
                string s = await response.Content.ReadAsStringAsync();
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                List<TodoItem> toDoArray = serializer.Deserialize<List<TodoItem>>(s);

                int count = 0;
                foreach (TodoItem item in toDoArray)
                {
                    Console.WriteLine(item.Title);
                    count++;
                }

                Console.WriteLine("Total item count:  {0}\n", count);
            }
            else
            {
                Console.WriteLine("Failed to retrieve To Do list\nError:  {0}\n", response.ReasonPhrase);
            }
        }

        static async Task<string> GetTokenWithClientCredential()
        {
            var authority = string.Format(CultureInfo.InvariantCulture, aadInstance, tenant);
            var authContext = new AuthenticationContext(authority);
            var clientCredential = new ClientCredential(appClientId, appKey);

            authContext.TokenCache.Clear();

            //
            // Get an access token from Azure AD using client credentials.
            // If the attempt to get a token fails because the server is unavailable, retry twice after 3 seconds each.
            //
            AuthenticationResult result = null;
            int retryCount = 0;
            bool retry = false;

            do
            {
                retry = false;
                try
                {
                    // ADAL includes an in memory cache, so this call will only send a message to the server if the cached token is expired.
                    authContext.TokenCache.Clear();
                    result = await authContext.AcquireTokenAsync(apiClientId, clientCredential);
                    return result.AccessToken;
                }
                catch (AdalException ex)
                {
                    if (ex.ErrorCode == "temporarily_unavailable")
                    {
                        retry = true;
                        retryCount++;
                        Thread.Sleep(3000);
                    }

                    Console.WriteLine(
                        string.Format("An error occurred while acquiring a token\nTime: {0}\nError: {1}\nRetry: {2}\n",
                            DateTime.Now.ToString(),
                            ex.ToString(),
                            retry.ToString()));
                }

            } while ((retry == true) && (retryCount < 3));

            return string.Empty;
        }

        static async Task<string> GetTokenWithUserPasswordCredential()
        {
            var authority = string.Format(CultureInfo.InvariantCulture, aadInstance, tenant);
            var authContext = new AuthenticationContext(authority);
            var userCredential = new UserPasswordCredential(_user, _password);

            AuthenticationResult result = null;
            int retryCount = 0;
            bool retry = false;

            do
            {
                retry = false;
                try
                {
                    // ADAL includes an in memory cache, so this call will only send a message to the server if the cached token is expired.
                    authContext.TokenCache.Clear();
                    result = await authContext.AcquireTokenAsync(apiClientId, appClientId, userCredential);
                    return result.AccessToken;
                }
                catch (AdalException ex)
                {
                    if (ex.ErrorCode == "temporarily_unavailable")
                    {
                        retry = true;
                        retryCount++;
                        Thread.Sleep(3000);
                    }

                    Console.WriteLine(
                        string.Format("An error occurred while acquiring a token\nTime: {0}\nError: {1}\nRetry: {2}\n",
                            DateTime.Now.ToString(),
                            ex.ToString(),
                            retry.ToString()));
                }

            } while ((retry == true) && (retryCount < 3));

            return string.Empty;
        }

        static async Task<string> GetTokenWithUserPasswordClientSecretCredential()
        {
            //
            // Get an access token from Azure AD using username and password.
            // If the attempt to get a token fails because the server is unavailable, retry twice after 3 seconds each.
            //

            int retryCount = 0;
            bool retry = false;

            do
            {
                var azureAdEndpoint = new Uri(authority + "/oauth2/token");
                var urlEncodedContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "password"),
                    new KeyValuePair<string, string>("scope", "openid"),
                    new KeyValuePair<string, string>("resource", apiClientId),
                    new KeyValuePair<string, string>("client_id", apiClientId), //using the api client id
                    new KeyValuePair<string, string>("username", _user),
                    new KeyValuePair<string, string>("password", _password),
                    new KeyValuePair<string, string>("client_secret", _clientSecret),
                });

                var result = await httpClient.PostAsync(azureAdEndpoint, urlEncodedContent);

                if (result.IsSuccessStatusCode)
                {
                    var content = await result.Content.ReadAsStringAsync();
                    var authResult = JsonConvert.DeserializeObject<dynamic>(content);
                    return authResult.access_token;
                }
                else
                {
                    retry = true;
                    retryCount++;
                    Thread.Sleep(3000);

                    Console.WriteLine(
                    string.Format("An error occurred while acquiring a token\nTime: {0}\nRetry: {1}\n",
                        DateTime.Now.ToString(),
                        retry.ToString()));
                }


            } while ((retry == true) && (retryCount < 3));

            return string.Empty;
        }
    }
}
