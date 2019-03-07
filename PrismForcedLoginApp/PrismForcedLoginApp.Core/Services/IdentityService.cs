﻿using System;
using System.Configuration;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.AppConfig;
using PrismForcedLoginApp.Core.Helpers;

namespace PrismForcedLoginApp.Core.Services
{
    public class IdentityService : IIdentityService
    {
        //// Read more about Microsoft Identity Client here
        //// https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki
        //// https://docs.microsoft.com/azure/active-directory/develop/v2-overview        
        private readonly string[] _scopes = new string[] { "user.read" };

        private bool _integratedAuthAvailable;
        private IPublicClientApplication _client;
        private AuthenticationResult _authenticationResult;

        // TODO WTS: Add your Identity Client ID in your App.config file
        // Follow these steps to register your application
        // with Azure Active Directory and obtain a new Client ID
        // https://docs.microsoft.com/azure/active-directory/develop/quickstart-register-app
        private string _clientId => ConfigurationManager.AppSettings["IdentityClientId"];

        public event EventHandler LoggedIn;
        public event EventHandler LoggedOut;

        public void InitializeWithAadAndPersonalMsAccounts()
        {
            _integratedAuthAvailable = false;
            _client = PublicClientApplicationBuilder.Create(_clientId)
                                                    .WithAuthority(AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
                                                    .Build();
        }

        public void InitializeWithAadMultipleOrgs(bool integratedAuth = false)
        {
            _integratedAuthAvailable = integratedAuth;
            _client = PublicClientApplicationBuilder.Create(_clientId)
                                                    .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
                                                    .Build();
        }

        public void InitializeWithAadSingleOrg(string tenant, bool integratedAuth = false)
        {
            _integratedAuthAvailable = integratedAuth;
            _client = PublicClientApplicationBuilder.Create(_clientId)
                                                    .WithAuthority(AzureCloudInstance.AzurePublic, tenant)
                                                    .Build();
        }

        public bool IsLoggedIn() => _authenticationResult != null;

        public async Task<LoginResultType> LoginAsync()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return LoginResultType.NoNetworkAvailable;
            }

            try
            {
                var accounts = await _client.GetAccountsAsync();
                _authenticationResult = await _client.AcquireTokenInteractive(_scopes, null)
                                                     .WithAccount(accounts.FirstOrDefault())
                                                     .ExecuteAsync();

                LoggedIn?.Invoke(this, EventArgs.Empty);
                return LoginResultType.Success;
            }
            catch (MsalClientException ex)
            {
                if (ex.ErrorCode == "authentication_canceled")
                {
                    return LoginResultType.CancelledByUser;
                }

                return LoginResultType.UnknownError;
            }
            catch (Exception)
            {
                return LoginResultType.UnknownError;
            }
        }

        public string GetAccountUserName()
        {
            return _authenticationResult?.Account?.Username;
        }

        public async Task LogoutAsync()
        {
            try
            {
                var accounts = await _client.GetAccountsAsync();
                var account = accounts.FirstOrDefault();
                if (account != null)
                {
                    await _client.RemoveAsync(account);
                }

                _authenticationResult = null;
                LoggedOut?.Invoke(this, EventArgs.Empty);
            }
            catch (MsalException)
            {
                // TODO WTS: LogoutAsync can fail please handle exceptions as appropriate to your scenario
                // For more info on MsalExceptions see
                // https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/exceptions
            }
        }

        public async Task<string> GetAccessTokenAsync()
        {
            if (!_authenticationResult.IsAccessTokenExpired())
            {
                return _authenticationResult.AccessToken;
            }

            var loginSuccess = await SilentLoginAsync();
            if (loginSuccess)
            {
                return _authenticationResult.AccessToken;
            }
            else
            {
                // The token has expired and we can't obtain a new one
                // The session will be closed.
                _authenticationResult = null;
                LoggedOut?.Invoke(this, EventArgs.Empty);
                return string.Empty;
            }
        }

        public async Task<bool> SilentLoginAsync()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return false;
            }
            try
            {
                if (_integratedAuthAvailable)
                {
                    _authenticationResult = await _client.AcquireTokenByIntegratedWindowsAuth(_scopes)
                                                         .ExecuteAsync();
                }
                else
                {
                    var accounts = await _client.GetAccountsAsync();
                    _authenticationResult = await _client.AcquireTokenSilent(_scopes)
                                                         .WithAccount(accounts.FirstOrDefault())
                                                         .ExecuteAsync();
                }

                return true;
            }
            catch (MsalUiRequiredException)
            {
                // Interactive authentication is required
                return false;
            }
            catch (MsalException)
            {
                // TODO WTS: Silentauth failed, please handle this exception as appropriate to your scenario
                // For more info on MsalExceptions see
                // https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/exceptions
                return false;
            }
        }
    }
}