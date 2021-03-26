using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SubmissionEvaluation.Contracts.Interfaces;
using SubmissionEvaluation.Contracts.Providers;
using SubmissionEvaluation.Providers.CryptographyProvider;
using SubmissionEvaluation.Server.Classes.JekyllHandling;

namespace SubmissionEvaluation.Server.Classes.Authentication
{
    internal class LocalAuthentication : IUserAuthentication
    {
        private readonly IMemberProvider memberProvider;

        public LocalAuthentication(IMemberProvider memberProvider)
        {
            this.memberProvider = memberProvider;
        }

        public void SetLogger(ILogger logger)
        {
        }

        public Dictionary<string, string> VerifyUser(string username, string password)
        {
            try
            {
                var member = memberProvider.GetMemberByUid(username) ?? memberProvider.GetMemberByMail(username);
                if (member == null) { return new Dictionary<string, string>(); }

                if (CryptographyProvider.VerifyPassword(password, member.Password))
                {
                    return new Dictionary<string, string> {{"uid", member.Uid}, {"sn", member.Name}, {"givenName", ""}, {"mail", member.Mail}};
                }

                if (!BCrypt.Net.BCrypt.EnhancedVerify(password, member.Password))
                {
                    return new Dictionary<string, string>();
                }

                var pwdHash = CryptographyProvider.CreateArgon2Password(password);
                JekyllHandler.MemberProvider.UpdatePassword(member, pwdHash);

                return new Dictionary<string, string> {{"uid", member.Uid}, {"sn", member.Name}, {"givenName", ""}, {"mail", member.Mail}};
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
