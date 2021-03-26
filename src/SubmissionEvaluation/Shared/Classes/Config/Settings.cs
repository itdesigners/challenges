namespace SubmissionEvaluation.Shared.Classes.Config
{
    public static class Settings
    {
        internal static bool Initialized { get; set; } = false;

        public static string SecurityToken { get; internal set; } = string.Empty;

        //In days
        public static int DuplicateCheckWindow { get; internal set; }
        public static AuthenticationSettings Authentication { get; } = new AuthenticationSettings();
        public static ToolPathsSettings Paths { get; } = new ToolPathsSettings();
        public static ApplicationSettings Application { get; } = new ApplicationSettings();
        public static MailSettings Mail { get; } = new MailSettings();
        public static LdapSettings Ldap { get; } = new LdapSettings();
        public static FeatureSettings Features { get; } = new FeatureSettings();
        public static CustomizationSettings Customization { get; } = new CustomizationSettings();
        public static PermissionSettings Permissions { get; } = new PermissionSettings();
    }
}
