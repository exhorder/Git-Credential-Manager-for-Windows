﻿using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace Microsoft.TeamFoundation.Git.Helpers.Authentication
{
    public class VsoAadAuthentication : BaseVsoAuthentication, IVsoAadAuthentication
    {
        public const string DefaultAuthorityHost = "https://login.windows.net/common";
        private const string AuthorityHostFormat = "https://login.windows.net/{0:D}";

        public VsoAadAuthentication()
            : base(DefaultAuthorityHost)
        { }
        public VsoAadAuthentication(Guid tenantId, string resource, Guid clientId)
            : base(String.Format(CultureInfo.InvariantCulture, AuthorityHostFormat, tenantId), resource, clientId)
        { }
        public VsoAadAuthentication(string resource, Guid clientId)
            : base(DefaultAuthorityHost, resource, clientId)
        { }
        /// <summary>
        /// Test constructor which allows for using fake credential stores
        /// </summary>
        /// <param name="personalAccessToken"></param>
        /// <param name="userCredential"></param>
        /// <param name="adaRefresh"></param>
        internal VsoAadAuthentication(ICredentialStore personalAccessToken, ICredentialStore userCredential, ITokenStore adaRefresh)
            : base(DefaultAuthorityHost, personalAccessToken, userCredential, adaRefresh)
        { }

        public override async Task<bool> InteractiveLogon(Uri targetUri, Credentials credentials)
        {
            BaseCredentialStore.ValidateTargetUri(targetUri);
            BaseCredentialStore.ValidateCredentials(credentials);

            Trace.TraceInformation("Begin InteractiveLogon for {0}", targetUri);

            try
            {
                string clientId = this.ClientId.ToString("D");
                string resource = this.Resource;

                UserCredential userCredential = new UserCredential(credentials.Username, credentials.Password);
                AuthenticationContext authCtx = new AuthenticationContext(this.AuthorityHostUrl, TokenCache.DefaultShared);
                AuthenticationResult authResult = await authCtx.AcquireTokenAsync(resource, clientId, userCredential);

                this.StoreRefreshToken(targetUri, authResult);
                this.UserCredentialStore.WriteCredentials(targetUri, credentials);

                return await this.GeneratePersonalAccessToken(targetUri, authResult);
            }
            catch (AdalException exception)
            {
                Debug.Write(exception);
            }

            Trace.TraceInformation("InteractiveLogon failed");
            return false;
        }

        public async Task<bool> NoninteractiveLogon(Uri targetUri)
        {
            BaseCredentialStore.ValidateTargetUri(targetUri);

            try
            {
                string clientId = this.ClientId.ToString("D");
                string resource = this.Resource;

                UserCredential userCredential = new UserCredential();
                AuthenticationContext authCtx = new AuthenticationContext(this.AuthorityHostUrl, TokenCache.DefaultShared);
                AuthenticationResult authResult = await authCtx.AcquireTokenAsync(resource, clientId, userCredential);

                this.StoreRefreshToken(targetUri, authResult);

                return await this.GeneratePersonalAccessToken(targetUri, authResult);
            }
            catch (AdalServiceException exception)
            {
                Debug.WriteLine(exception);
            }

            return false;
        }

        public override async Task<bool> RefreshCredentials(Uri targetUri)
        {
            BaseCredentialStore.ValidateTargetUri(targetUri);

            try
            {
                string clientId = this.ClientId.ToString("D");
                string resource = this.Resource;

                Token refreshToken = null;
                if (this.AdaRefreshTokenStore.ReadToken(targetUri, out refreshToken))
                {
                    AuthenticationContext authCtx = new AuthenticationContext(this.AuthorityHostUrl, TokenCache.DefaultShared);
                    AuthenticationResult authResult = await authCtx.AcquireTokenByRefreshTokenAsync(refreshToken.Value, clientId, resource);

                    return await this.GeneratePersonalAccessToken(targetUri, authResult);
                }
                else
                {
                    Credentials credentials = null;
                    if (this.UserCredentialStore.ReadCredentials(targetUri, out credentials))
                    {
                        return await this.InteractiveLogon(targetUri, credentials);
                    }
                }
            }
            catch (AdalException exception)
            {
                Debug.WriteLine(exception);
            }

            return true;
        }

        public override bool SetCredentials(Uri targetUri, Credentials credentials)
        {
            BaseCredentialStore.ValidateTargetUri(targetUri);
            BaseCredentialStore.ValidateCredentials(credentials);

            var task = Task.Run<bool>(async () => { return await this.InteractiveLogon(targetUri, credentials); });
            task.Wait();

            if (task.IsFaulted)
                throw task.Exception;

            return task.Result;
        }
    }
}
