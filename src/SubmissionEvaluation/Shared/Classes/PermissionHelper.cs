using System.Collections.Generic;
using SubmissionEvaluation.Shared.Models.Permissions;

namespace SubmissionEvaluation.Shared.Classes
{
    public static class PermissionHelper
    {
        public static bool CheckPermissions(Actions action, string area, Permissions permissions, Restriction accessibles = Restriction.NONE, string id = null)
        {
            if (permissions == null) { return false; }

            List<string> checkingPermissions;
            switch (action)
            {
                case Actions.CREATE:
                    checkingPermissions = permissions.CreatePermissions;
                    break;
                case Actions.VIEW:
                    checkingPermissions = permissions.ViewPermissions;
                    break;
                case Actions.EDIT:
                    checkingPermissions = permissions.EditPermissions;
                    break;
                default:
                    checkingPermissions = new List<string>();
                    break;
            }

            List<string> accessibleList;
            switch (accessibles)
            {
                case Restriction.CHALLENGES:
                    accessibleList = permissions.ChallengesAccessible;
                    break;
                case Restriction.GROUPS:
                    accessibleList = permissions.GroupsAccessible;
                    break;
                case Restriction.BUNDLES:
                    accessibleList = permissions.BundlesAccessible;
                    break;
                case Restriction.MEMBERS:
                    accessibleList = permissions.MembersAccessible;
                    break;
                case Restriction.NONE:
                    accessibleList = new List<string>();
                    break;
                default:
                    accessibleList = new List<string>();
                    break;
            }

            return permissions.isAdmin || checkingPermissions.Contains(area) && (accessibleList.Count == 0 || accessibleList.Contains(id));
        }
    }
}
