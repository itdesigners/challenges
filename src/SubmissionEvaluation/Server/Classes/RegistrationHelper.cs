using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Providers.CryptographyProvider;
using SubmissionEvaluation.Server.Classes.Authentication;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Shared.Classes.Config;
using SubmissionEvaluation.Shared.Classes.Messages;
using SubmissionEvaluation.Shared.Models.Account;

namespace SubmissionEvaluation.Server.Classes
{
    public class RegistrationHelper
    {
        private RegistrationHelper()
        {
        }

        public static RegistrationHelper registrationHelper { get; } = new RegistrationHelper();

        public RegisterModel RegisterMember(RegisterModel model, HttpContext context, ILogger logger)
        {
            if (Settings.Features.EnableLdap)
            {
                throw new Exception("Manual registration is disabled");
            }

            if (Settings.Features.EnableSendMail)
            {
                if (string.IsNullOrWhiteSpace(model.Mail))
                {
                    model.Message = ErrorMessages.MissingMail;
                    model.HasError = true;
                    return model;
                }

                var pwdHash = CryptographyProvider.CreateArgon2Password(model.Password);
                JekyllHandler.Domain.Interactions.RegisterMemberViaMail(model.Mail, pwdHash);

                JekyllHandler.SmtpProvider.SendMail(model.Mail, "Login-Daten für Challenges Webseite",
                    "Username: " + model.Mail + Environment.NewLine + "Password: " + pwdHash);
                model.Message = SuccessMessages.PasswordSent;
                model.HasSuccess = true;
                return model;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(model.Username))
                {
                    model.Message = ErrorMessages.MissingName;
                    model.HasError = true;
                    return model;
                }

                if (string.IsNullOrWhiteSpace(model.Password))
                {
                    model.Message = ErrorMessages.MissingPassword;
                    model.HasError = true;
                    return model;
                }

                if (string.IsNullOrWhiteSpace(model.Fullname))
                {
                    model.Message = ErrorMessages.MissingName;
                    model.HasError = true;
                    return model;
                }

                var hashAsString = CryptographyProvider.CreateArgon2Password(model.Password);
                var member = JekyllHandler.Domain.Interactions.RegisterMemberViaUsername(model.Username, hashAsString, model.Fullname);

                if (member.State == MemberState.Pending)
                {
                    return new RegisterModel {HasError = true, Message = ErrorMessages.ActivationNeeded};
                }

                var authentication = new LocalAuthentication(JekyllHandler.MemberProvider);
                var attributeTable = authentication.VerifyUser(model.Username, model.Password);
                if (attributeTable != null)
                {
                    new IdentityAuthenticator().WriteIdentityCookie(model.Username, attributeTable, context).Wait(5000);
                    return new RegisterModel {HasSuccess = true};
                }
            }
            catch (Exception ex)
            {
                JekyllHandler.Log.Error(ex, "Anlegen des Benutzers {user} fehlgeschlagen", model.Username);
                model.Message = ErrorMessages.UserCreateFailed;
                model.HasError = true;
                return model;
            }

            return model;
        }
    }
}
