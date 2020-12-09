﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fido2NetLib;
using Fido2NetLib.Development;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Okta.Sdk;
using Okta.Sdk.Configuration;
using OktaWebAuthn.Models;

namespace OktaWebAuthn.Controllers
{
    public class AccountController : Controller
    {
        private readonly IFido2 fido2;
        private OktaClient oktaClient;

        public AccountController(IConfiguration configuration, IFido2 fido2)
        {
            this.fido2 = fido2;

            oktaClient = new OktaClient(new OktaClientConfiguration
            {
                OktaDomain = configuration["Okta:Domain"],
                Token = configuration["Okta:ApiToken"]
            });
        }

        public ActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public ActionResult CredentialOptions([FromBody] RegisterModel model)
        {
            var user = new Fido2User
            {
                DisplayName = $"{model.FirstName} {model.LastName}",
                Name = model.Email,
                Id = Encoding.UTF8.GetBytes(model.Email)
            };

            var options = fido2.RequestNewCredential(user, new List<PublicKeyCredentialDescriptor>());

            HttpContext.Session.SetString("fido2.attestationOptions", options.ToJson());

            return Json(options);
        }

        [HttpPost]
        public async Task<JsonResult> SaveCredentials([FromBody] AuthenticatorAttestationRawResponse attestationResponse)
        {
            try
            {
                var jsonOptions = HttpContext.Session.GetString("fido2.attestationOptions");
                var options = CredentialCreateOptions.FromJson(jsonOptions);

                var fidoCredentials = await fido2.MakeNewCredentialAsync(attestationResponse, options, IsCredentialUnique);

                var storedCredential = new StoredCredential
                {
                    Descriptor = new PublicKeyCredentialDescriptor(fidoCredentials.Result.CredentialId),
                    PublicKey = fidoCredentials.Result.PublicKey,
                    UserHandle = fidoCredentials.Result.User.Id,
                    SignatureCounter = fidoCredentials.Result.Counter,
                    CredType = fidoCredentials.Result.CredType,
                    RegDate = DateTime.Now,
                    AaGuid = fidoCredentials.Result.Aaguid
                };

                var result = await oktaClient.Users.CreateUserAsync(new CreateUserWithoutCredentialsOptions
                {
                    Profile = new UserProfile
                    {
                        Login = options.User.Name,
                        Email = options.User.Name,
                        DisplayName = options.User.DisplayName,
                        ["CredentialId"] = Convert.ToBase64String(fidoCredentials.Result.CredentialId),
                        ["PasswordlessPublicKey"] = JsonConvert.SerializeObject(storedCredential)
                    }
                });

                return Json(fidoCredentials);
            }
            catch (Exception e)
            {
                return Json(new Fido2.CredentialMakeResult { Status = "error", ErrorMessage = e.Message });
            }
        }

        
        public ActionResult SignIn()
        {
            return View();
        }

        async Task<bool> IsCredentialUnique(IsCredentialIdUniqueToUserParams userParams)
        {
            var listUsers = oktaClient.Users.ListUsers(search: $"profile.CredentialId eq \"{Convert.ToBase64String(userParams.CredentialId)}\"");
            var users = await listUsers.CountAsync();
            return users == 0;
        }
    }
}