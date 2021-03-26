using System;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Contracts.Providers;

namespace SubmissionEvaluation.Domain.Operations
{
    internal class GroupOperations
    {
        internal static void RenameGroupInAllMembers(IFileProvider fileProvider, IGroup group, string newId)
        {
            var members = fileProvider.LoadMembers();
            foreach (var member in members)
            {
                var indexGroup = Array.IndexOf(member.Groups, group.Id);
                if (indexGroup >= 0)
                {
                    using var writeLock = fileProvider.GetLock();
                    var updated = fileProvider.LoadMember(member.Id, writeLock);
                    updated.Groups[indexGroup] = newId;
                    fileProvider.SaveMember(updated, writeLock);
                }
            }
        }

        internal static bool VerifyGroup(Domain domain, string group)
        {
            var providerStore = domain.ProviderStore;
            try
            {
                var properties = providerStore.FileProvider.LoadGroup(group);
                VerifyGroupProperties(properties);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void VerifyGroupProperties(Group properties)
        {
            if (string.IsNullOrWhiteSpace(properties.Title))
            {
                throw new DeserializationException("Die Gruppe hat keinen Titel");
            }

            if (properties.Title.Length < 3)
            {
                throw new DeserializationException("Der Titel der Gruppe ist zu kurz");
            }
        }
    }
}
