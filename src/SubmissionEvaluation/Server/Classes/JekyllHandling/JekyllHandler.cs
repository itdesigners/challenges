using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using SubmissionEvaluation.Compilers;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Providers;
using SubmissionEvaluation.Providers;
using SubmissionEvaluation.Providers.FileProvider;
using SubmissionEvaluation.Providers.LogProvider;
using SubmissionEvaluation.Providers.MailProvider;
using SubmissionEvaluation.Providers.MemberProvider;
using SubmissionEvaluation.Providers.ProcessProvider;
using SubmissionEvaluation.Shared.Classes;
using SubmissionEvaluation.Shared.Classes.Config;
using SubmissionEvaluation.Shared.Models.Permissions;

namespace SubmissionEvaluation.Server.Classes.JekyllHandling
{
    public static class JekyllHandler
    {
        public static IMemberProvider MemberProvider { get; private set; }
        public static ISmtpProvider SmtpProvider { get; private set; }
        public static Domain.Domain Domain { get; private set; }
        public static ILog Log { get; private set; }

        public static void Initialize(bool logStatusChanges)
        {
            Log = new Logger(Settings.Application.PathToLogger);
            Log.Information("Programm wurde gestartet");
            SmtpProvider = new SmtpProvider(Log, Settings.Features.EnableSendMail, Settings.Mail.Username, Settings.Mail.Password, Settings.Mail.SmtpServer,
                Settings.Mail.SendMailAddress);
            var fileProvider = new FileProvider(Log, Settings.Application.PathToData, Settings.Application.PathToServerWwwRoot, logStatusChanges);
            MemberProvider = new MemberProvider(Log, fileProvider, Settings.Features.EnableLdap ? MemberType.LDAP : MemberType.Local,
                Settings.Features.RequiresMemberActivation);
            var processProvider = new ProcessProvider();
            var sandboxedProcessProvider = new DockerProcessProvider(Log);
            var challengeEstimator = new ChallengeEstimator.ChallengeEstimator();
            var compilers = new List<ICompiler>
            {
                new KotlinCompiler(Log, Settings.Paths.Java, Settings.Paths.KotlinCompiler),
                new JavaMavenCompiler(Log, Settings.Paths.Java, Settings.Paths.Maven),
                new CsCompiler(Log, Settings.Paths.DotNet),
                new FsCompiler(Log, Settings.Paths.DotNet),
                new HaskellCompiler(Log, Settings.Paths.HaskellCompiler),
                new ScalaCompiler(Log, Settings.Paths.Scala, Settings.Paths.ScalaCompiler),
                new JavaScriptCompiler(Log, Settings.Paths.NodeJS, Settings.Paths.Npm),
                new PerlCompiler(Log, Settings.Paths.Perl),
                new PythonCompiler(Log, Settings.Paths.Python),
                new CCMakeCompiler(Log, Settings.Paths.CCompiler, Settings.Paths.CppCheck, Settings.Paths.CMake),
                new CppCMakeCompiler(Log, Settings.Paths.CppCompiler, Settings.Paths.CppCheck, Settings.Paths.CMake),
                new GoCompiler(Log, Settings.Paths.Go),
                new RustCargoCompiler(Log, Settings.Paths.RustCargo, Settings.Paths.Rust),
                new TypeScriptCompiler(Log, Settings.Paths.TypeScriptCompiler, Settings.Paths.NodeJS, Settings.Paths.Npm),
                new JuliaCompiler(Log, Settings.Paths.Julia)
            };

            ((Logger) Log).ReportMails = MemberProvider.GetMembers().Where(x => x.IsAdmin).Select(x => x.Mail).ToList();
            ((Logger) Log).SmtpProvider = SmtpProvider;

            Domain = new Domain.Domain(fileProvider, MemberProvider, processProvider, sandboxedProcessProvider, challengeEstimator, SmtpProvider, compilers,
                Log, Settings.Features.EnableReview, Settings.Features.EnableReviewerPromotion, Settings.Features.EnableAchievements,
                Settings.Application.SiteUrl, Settings.Mail.HelpMailAddress);
            Domain.Interactions.LoadCompilerData();
        }

        public static IMember GetMemberByUid(string uid)
        {
            return MemberProvider.GetMemberByUid(uid);
        }

        public static string GetVersionHash()
        {
            return Environment.GetEnvironmentVariable("CURRENT_HASH");
        }

        public static IMember GetMemberForUser(ClaimsPrincipal user)
        {
            var id = user.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Sid)?.Value;
            return MemberProvider.GetMemberById(id);
        }

        public static Permissions GetPermissionsForMember(IMember member)
        {
            var permissions = new Permissions();
            if (member.IsAdmin)
            {
                permissions.isAdmin = true;
                return permissions;
            }

            if (member.IsCreator)
            {
                permissions.ViewPermissions.AddRange(Settings.Permissions.CreatorPermissions.ViewPermissions);
                permissions.CreatePermissions.AddRange(Settings.Permissions.CreatorPermissions.CreatePermissions);
                permissions.EditPermissions.AddRange(Settings.Permissions.CreatorPermissions.EditPermissions);
                var challenges = Domain.Query.GetAllChallenges(member);
                permissions.ChallengesAccessible.AddRange(challenges.Where(x => x.AuthorID.Equals(member.Id)).Select(x => x.Id));
                var bundles = Domain.Query.GetAllBundles(member, true);
                permissions.BundlesAccessible.AddRange(bundles.Where(x => x.Author.Equals(member.Id)).Select(x => x.Id));
            }

            if (member.IsGroupAdmin)
            {
                permissions.ViewPermissions.AddRange(Settings.Permissions.GroupAdminPermissions.ViewPermissions);
                permissions.CreatePermissions.AddRange(Settings.Permissions.GroupAdminPermissions.CreatePermissions);
                permissions.EditPermissions.AddRange(Settings.Permissions.GroupAdminPermissions.EditPermissions);
                var groups = Domain.Query.GetAllGroups();
                permissions.GroupsAccessible.AddRange(groups.Where(x => (x.GroupAdminIds ?? new List<string>()).Contains(member.Id)).Select(x => x.Id));
                permissions.GroupsAccessible.AddRange(groups.Where(x => (x.GroupAdminIds ?? new List<string>()).Contains(member.Id) && x.IsSuperGroup)
                    .SelectMany(x => x.SubGroups));
                var members = MemberProvider.GetMembers();
                permissions.MembersAccessible.AddRange(members.Where(x => x.Groups.Intersect(permissions.GroupsAccessible).Any()).Select(x => x.Id));
                permissions.ChallengesAccessible.AddRange(groups.SelectMany(x => x.AvailableChallenges));
                permissions.ChallengesAccessible.AddRange(groups.SelectMany(x => x.ForcedChallenges));
            }

            if (member.IsReviewer)
            {
                permissions.ViewPermissions.AddRange(Settings.Permissions.GroupReviewerPermissions.ViewPermissions);
                permissions.CreatePermissions.AddRange(Settings.Permissions.GroupReviewerPermissions.CreatePermissions);
                permissions.EditPermissions.AddRange(Settings.Permissions.GroupReviewerPermissions.EditPermissions);
                //TODO: When some day a reviewer list is added to group, add accessible groups here.
            }

            return permissions;
        }

        public static bool CheckPermissions(Actions action, string area, IMember member, Restriction accessibles = Restriction.NONE, string id = null)
        {
            var permissions = GetPermissionsForMember(member);
            return PermissionHelper.CheckPermissions(action, area, permissions, accessibles, id);
        }
    }
}
