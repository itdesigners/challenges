using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Text.RegularExpressions;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Contracts.Providers;
using SubmissionEvaluation.Domain.Comparers;
using Group = SubmissionEvaluation.Contracts.Data.Group;

namespace SubmissionEvaluation.Domain.Operations
{
    public static class ChallengeOperations
    {
        private static MemoryCache ChallengesCanBeViewedByMemberCache = new MemoryCache("ChallengesCanBeViewedByMemberCache");

        internal static void RemoveAdditionalFileFromChallenge(ProviderStore providerStore, string challengeName, string filename)
        {
            using var writeLock = providerStore.FileProvider.GetLock();
            var challenge = providerStore.FileProvider.LoadChallenge(challengeName, writeLock);
            providerStore.FileProvider.DeleteAdditionalFile(challenge, filename);
            var tests = providerStore.FileProvider.LoadTestProperties(challenge).ToList();
            var changed = false;
            foreach (var test in tests)
            {
                if (test.InputFiles != null)
                {
                    var oldcount = test.InputFiles.Count;
                    test.InputFiles = test.InputFiles.Where(x => x.ContentFile != filename).ToList();
                    if (oldcount != test.InputFiles.Count)
                    {
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                providerStore.FileProvider.SaveTestProperties(challenge, tests, writeLock);
            }
        }

        internal static void ChangeAdditionalFilenameOfChallenge(ProviderStore providerStore, string challengeName, string oldname, string newname)
        {
            using var writeLock = providerStore.FileProvider.GetLock();
            var challenge = providerStore.FileProvider.LoadChallenge(challengeName, writeLock);
            providerStore.FileProvider.RenameAdditionalFile(challenge, oldname, newname);
            var tests = providerStore.FileProvider.LoadTestProperties(challenge).ToList();
            var changed = false;
            foreach (var test in tests)
            {
                if (test.InputFiles != null)
                {
                    foreach (var input in test.InputFiles)
                    {
                        if (input.ContentFile == oldname)
                        {
                            input.ContentFile = newname;
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                providerStore.FileProvider.SaveTestProperties(challenge, tests, writeLock);
            }
        }

        internal static IEnumerable<Challenge> LoadAllChallengeProperties(IFileProvider fileProvider)
        {
            return fileProvider.GetChallengeIds().Select(s =>
            {
                try
                {
                    return fileProvider.LoadChallenge(s);
                }
                catch
                {
                    return new Challenge {Id = s, State = {HasError = true}};
                }
            });
        }

        private static void MarkChallengeAsWorking(ProviderStore providerStore, string challenge)
        {
            using var writeLock = providerStore.FileProvider.GetLock();
            var challengeInfos = providerStore.FileProvider.LoadChallenge(challenge, writeLock);
            challengeInfos.State.HasError = false;
            challengeInfos.State.ErrorDescription = "";
            providerStore.FileProvider.SaveChallenge(challengeInfos, writeLock);
        }

        private static void MarkChallengeAsDefect(ProviderStore providerStore, string challenge, string errorMessage)
        {
            using var writeLock = providerStore.FileProvider.GetLock();
            var challengeInfos = providerStore.FileProvider.LoadChallenge(challenge, writeLock);
            challengeInfos.State.HasError = true;
            challengeInfos.State.ErrorDescription = errorMessage;
            providerStore.FileProvider.SaveChallenge(challengeInfos, writeLock);
        }

        internal static bool VerifyChallenge(Domain domain, string challenge)
        {
            var providerStore = domain.ProviderStore;
            try
            {
                var properties = providerStore.FileProvider.LoadChallenge(challenge);
                VerifyChallengeProperties(properties, domain.Compilers);
                var tests = providerStore.FileProvider.LoadTestProperties(properties).ToList();
                VerifyTestProperties(providerStore, properties, tests);
                MarkChallengeAsWorking(providerStore, challenge);
                return true;
            }
            catch (Exception ex)
            {
                MarkChallengeAsDefect(providerStore, challenge, ex.Message);
                return false;
            }
        }

        private static void VerifyTestProperties(ProviderStore providerStore, Challenge properties, IList<TestParameters> tests)
        {
            var customTests = tests.Any(x => x.CustomTestRunner != null);
            if (!customTests)
            {
                if (tests.Count < 3)
                {
                    throw new DeserializationException("Die Challenge hat zu wenig Tests. Mindestens 3 Tests sind erforderlich");
                }
            }
            else
            {
                if (!tests.All(x => Directory.Exists(providerStore.FileProvider.GetCustomTestRunnerPath(properties, x.CustomTestRunner.Path))))
                {
                    throw new DeserializationException("Custom Test Path nicht gefunden");
                }
            }
        }

        private static void VerifyChallengeProperties(Challenge properties, List<ICompiler> compilers)
        {
            if (string.IsNullOrWhiteSpace(properties.Title))
            {
                throw new DeserializationException("Die Challenge hat keinen Titel");
            }

            if (properties.Title.Length < 3)
            {
                throw new DeserializationException("Der Titel der Challenge ist zu kurz");
            }

            if (properties.Languages.Count > 0 && properties.Languages.Any(x => compilers.All(y => y.Name != x)))
            {
                throw new DeserializationException("Ungültige Programmiersprache angegeben");
            }
        }

        public static void ValidateChallengeName(IChallenge challenge)
        {
            if (string.IsNullOrWhiteSpace(challenge.Id))
            {
                throw new Exception("Challenge ID darf nicht leer sein");
            }

            if (challenge.Id.StartsWith("tn_"))
            {
                throw new Exception("Challenge ID darf nicht mit tn_ beginnen");
            }

            if (!Regex.IsMatch(challenge.Id, "^[a-zA-Z0-9]*$"))
            {
                throw new Exception("Die ID darf keine Leer- oder Sonderzeichen enthalten!");
            }
        }

        public static IReadOnlyList<IChallenge> GetAllChallengesForMember(IMember member, IFileProvider fileProvider, bool containUnavailable)
        {
            var allowed = GetChallengesCanBeViewedByMember(member, fileProvider);
            return allowed.Values.Where(x => containUnavailable || x.IsAvailable).ToList();
        }

        public static IReadOnlyList<IChallenge> GetAllChallengesForMember(IFileProvider fileProvider, bool containUnavailable)
        {
            var allowed = GetChallengesCanBeViewedByMember(null, fileProvider);
            return allowed.Values.Where(x => containUnavailable || x.IsAvailable).ToList();
        }

        /// <summary>
        ///     Get all challenges that should be visible for the provided user.
        /// </summary>
        /// <param name="member">the user for which the check should take place</param>
        /// <param name="fileProvider">file provider to access challenges</param>
        /// <returns>a dictionary containing the challenges with id string as lookup</returns>
        private static IReadOnlyDictionary<string, IChallenge> GetChallengesCanBeViewedByMember(IMember member, IFileProvider fileProvider)
        {
            // If no member given return an empty dictionary
            if (member == null)
            {
                return new List<IChallenge>().ToDictionary(x => x.Id);
            }

            if (member.Id != null && ChallengesCanBeViewedByMemberCache.Contains(member.Id))
            {
                return ChallengesCanBeViewedByMemberCache.Get(member.Id) as IReadOnlyDictionary<string, IChallenge>;
            }

            var visibleChallenges = FetchDefaultVisibleChallenges(member, fileProvider);

            #region determine challenges visible for an user that is not an admin or groupadmin

            if (!(member.IsAdmin || member.IsGroupAdmin))
            {
                // Please, be aware that there are several dependencies which must be considered
                // 1) There are bundles of challenges - although always the first challenge of a bundle should be visible
                // 2) There could be an overlap of challenge across the different groups
                //    Nevertheless, group constraints of forced challenges should be guaranteed
                //    In addition, the maximum count of visible challenges of the group should be guaranteed, too
                // 3) Challenges within a group should be assigned randomly
                // 4) Once assigned challenges should be stable
                //    First, changes in the member rating should not affect assigned challenges
                //    Second, further added challenges to a group should not affect the visible challenges

                // Get groups of the member
                var groupsOfMember = (member.Groups ?? new string[] { }).Select(x => fileProvider.LoadGroup(x));

                // it is possible to treat each group independently ;)
                foreach (var groupOfMember in groupsOfMember)
                {
                    if (groupOfMember != null)
                    {
                        AddChallengesForGroup(groupOfMember, member, visibleChallenges, fileProvider);
                    }
                }
            }

            #endregion

            var res = visibleChallenges.Distinct(new IChallengeComparer()).ToDictionary(x => x.Id);

            if (member.Id != null)
            {
                ChallengesCanBeViewedByMemberCache.Set(member.Id, res, DateTimeOffset.Now.AddMinutes(5));
            }

            // ensure list of challenges is unique before returning as dictionary
            return res;
        }

        /// <summary>
        ///     Get all challenges that should be visible for the provided user.
        /// </summary>
        /// <param name="member">the user for which the check should take place</param>
        /// <param name="fileProvider">file provider to access challenges</param>
        /// <returns>a dictionary containing the challenges with id string as lookup</returns>
        private static IReadOnlyDictionary<string, IChallenge> UnCachedGetChallengesCanBeViewedByMember(IMember member, IFileProvider fileProvider)
        {
            // If no member given return an empty dictionary
            if (member == null)
            {
                return new List<IChallenge>().ToDictionary(x => x.Id);
            }

            var visibleChallenges = FetchDefaultVisibleChallenges(member, fileProvider);

            #region determine challenges visible for an user that is not an admin or groupadmin

            if (!(member.IsAdmin || member.IsGroupAdmin))
            {
                // Please, be aware that there are several dependencies which must be considered
                // 1) There are bundles of challenges - although always the first challenge of a bundle should be visible
                // 2) There could be an overlap of challenge across the different groups
                //    Nevertheless, group constraints of forced challenges should be guaranteed
                //    In addition, the maximum count of visible challenges of the group should be guaranteed, too
                // 3) Challenges within a group should be assigned randomly
                // 4) Once assigned challenges should be stable
                //    First, changes in the member rating should not affect assigned challenges
                //    Second, further added challenges to a group should not affect the visible challenges

                // Get groups of the member
                var groupsOfMember = (member.Groups ?? new string[] { }).Select(x => fileProvider.LoadGroup(x));

                // it is possible to treat each group independently ;)
                foreach (var groupOfMember in groupsOfMember)
                {
                    if (groupOfMember != null)
                    {
                        AddChallengesForGroup(groupOfMember, member, visibleChallenges, fileProvider);
                    }
                }
            }

            #endregion

            // ensure list of challenges is unique before returning as dictionary
            return visibleChallenges.Distinct(new IChallengeComparer()).ToDictionary(x => x.Id);
        }

        /*
         * Returns all by default visible challenges for given member.
         */
        private static List<IChallenge> FetchDefaultVisibleChallenges(IMember member, IFileProvider fileProvider)
        {
            var visibleChallenges = new List<IChallenge>();

            // If admin or group admin append all challenges
            // Group admin needs to see all challenges to check which one he/she wants to add to his group!
            if (member.IsAdmin || member.IsGroupAdmin)
            {
                visibleChallenges.AddRange(fileProvider.LoadChallenges());
                return visibleChallenges;
            }

            // If creator append all challenges he*she has created
            if (member.IsCreator)
            {
                visibleChallenges.AddRange(fileProvider.LoadChallenges().Where(x => x.AuthorID.Equals(member.Id)));
            }

            // Append all solved and unlocked challenges
            visibleChallenges.AddRange((member.SolvedChallenges ?? new string[] { }).Select(x => fileProvider.LoadChallenge(x)));
            visibleChallenges.AddRange((member.UnlockedChallenges ?? new string[] { }).Select(x => fileProvider.LoadChallenge(x)));

            return visibleChallenges;
        }

        /// <summary>
        ///     Sums up all characters of a string
        /// </summary>
        /// <param name="data">string to be summed up</param>
        /// <returns>sum of all characters in string</returns>
        private static int GetAsciiSumOfString(string data)
        {
            return data.Aggregate(0, (sum, c) => sum + c);
        }

        /// <summary>
        ///     Shuffle enumerables based on a pseudo random generator and a passed seed.
        /// </summary>
        /// <param name="unshuffledEnumerable">IEnumerable to be shuffeld</param>
        /// <param name="seed">The seed for the random generator</param>
        /// <returns>Return a new shuffled IEnumerable with the given data from unshuffledEnumerable</returns>
        private static IEnumerable<T> Shuffle<T>(this IEnumerable<T> unshuffledEnumerable, int seed)
        {
            var random = new Random(seed);
            var unshuffledList = unshuffledEnumerable.ToList();

            while (unshuffledList.Count > 0)
            {
                var nextIndex = random.Next(0, unshuffledList.Count - 1);
                var nextItem = unshuffledList.ElementAt(nextIndex);
                unshuffledList.RemoveAt(nextIndex);
                yield return nextItem;
            }
        }

        private static void AddChallengesForGroup(Group group, IMember member, List<IChallenge> visibleChallenges, IFileProvider fileProvider)
        {
            #region handle forced challenges

            // Unlock only the first not solved forced challenge of a group
            // And ensure that no further challenges are visible if not all forced challenges of the group are solved
            var notYetSolvedForcedChallenges = group.ForcedChallenges.Where(x => !member.SolvedChallenges.Contains(x)).ToList();
            if (notYetSolvedForcedChallenges.Count > 0)
            {
                visibleChallenges.Add(fileProvider.LoadChallenge(notYetSolvedForcedChallenges.First()));
                return;
            }

            #endregion

            #region handle non-forced challenges

            // determine the first unsolved challenge of each bundle and
            // determine all unsolved challenges which are not part of a bundle
            var bundles = fileProvider.LoadAllBundles();
            var unlockableChallengeIdsOfBundles = bundles.Select(x => x.Challenges.FirstOrDefault(y => !member.SolvedChallenges.Contains(y)))
                .Where(x => !string.IsNullOrEmpty(x)).Where(x => group.AvailableChallenges.Contains(x));
            var challengesWithinBundles = bundles.SelectMany(x => x.Challenges).ToList();
            var unlockableChallengeIdsNotPartOfBundles = group.AvailableChallenges.Where(x => !challengesWithinBundles.Contains(x));
            // If there is no maximum count of visible challenges defined, than add all challenges to the group
            if (group.MaxUnlockedChallenges == 0)
            {
                visibleChallenges.AddRange(unlockableChallengeIdsOfBundles.Select(item => fileProvider.LoadChallenge(item)));
                visibleChallenges.AddRange(unlockableChallengeIdsNotPartOfBundles.Select(item => fileProvider.LoadChallenge(item)));
                return;
            }

            var alreadySolvedChallengeIds = member.SolvedChallenges;
            var alreadyUnlockedChallengeIds = member.UnlockedChallenges;
            var maxCountOfChallenges = group.MaxUnlockedChallenges;

            // verify that there are not already enough challenges of the group unlocked
            //numberOfMissingChallenges is max amount minus the amount of unlocked challengeIds, that are also in the group available
            var numberOfMissingChallenges = maxCountOfChallenges - alreadyUnlockedChallengeIds.Where(x => group.AvailableChallenges.Contains(x)).ToList().Count;
            if (numberOfMissingChallenges <= 0)
            {
                return;
            }

            // aggregate list of all possible challenges
            var challengeIdsOfGroup = new List<string>();
            challengeIdsOfGroup.AddRange(unlockableChallengeIdsOfBundles);
            challengeIdsOfGroup.AddRange(unlockableChallengeIdsNotPartOfBundles);
            var challengesWhichAreNotAlreadySolvedOrUnlocked = challengeIdsOfGroup.Where(x => !alreadySolvedChallengeIds.Contains(x))
                .Where(x => !alreadyUnlockedChallengeIds.Contains(x)).Select(x => fileProvider.LoadChallenge(x));

            // remove challenges which are too simple for a member, except they are part of a bundle or have no rating
            var ongoingChallengesOfBundle = bundles.SelectMany(x => x.Challenges.GetRange(1, x.Challenges.Count - 1));
            var challengesWhichAreNotTooEasyOrPartOfABundle = challengesWhichAreNotAlreadySolvedOrUnlocked.Where(x =>
                x.State.DifficultyRating >= member.AverageDifficultyLevel * 0.9 || !x.State.DifficultyRating.HasValue && member.AverageDifficultyLevel > 40 ||
                ongoingChallengesOfBundle.Contains(x.Id));

            // shuffle list of challenges using the random seed from the name of the user
            var availableChallengesShuffled = challengesWhichAreNotTooEasyOrPartOfABundle.Shuffle(GetAsciiSumOfString(member.Name)).ToList();

            // ensure next challenge of a bundle is unlocked if at least one of the bundle challenges is solved
            var nextUnlockableChallenges = new List<IChallenge>();
            nextUnlockableChallenges.AddRange(availableChallengesShuffled.Where(x => ongoingChallengesOfBundle.Contains(x.Id)));
            nextUnlockableChallenges.AddRange(availableChallengesShuffled.Where(x => !ongoingChallengesOfBundle.Contains(x.Id)));

            // select only as much challenges as required or available
            var nextUnlockedChallenges = nextUnlockableChallenges.Count >= numberOfMissingChallenges
                ? nextUnlockableChallenges.GetRange(0, numberOfMissingChallenges)
                : nextUnlockableChallenges;

            // verify that at least one of the challenges has a suitable difficulty rating (if such a challenge exist)
            bool IsChallengeSolvable(IChallenge x)
            {
                return x.State.DifficultyRating.HasValue && x.State.DifficultyRating.Value < member.AverageDifficultyLevel + 20;
            }

            var nextChallenges = member.UnlockedChallenges.Select(x => fileProvider.LoadChallenge(x)).ToList<IChallenge>();
            nextChallenges.AddRange(nextUnlockedChallenges);
            if (nextUnlockedChallenges.Count != 0 && !nextChallenges.Any(IsChallengeSolvable))
            {
                var solvableChallenge = nextUnlockableChallenges.FirstOrDefault(IsChallengeSolvable);
                if (solvableChallenge != default(IChallenge))
                {
                    nextUnlockedChallenges.RemoveAt(nextUnlockedChallenges.Count - 1);
                    nextUnlockedChallenges.Add(solvableChallenge);
                }
            }

            // finally unlock challenges
            visibleChallenges.AddRange(nextUnlockedChallenges);

            #endregion
        }

        public static IEnumerable<IBundle> GetAllBundles(IMember member, bool includeDraft, IFileProvider fileProvider)
        {
            var bundles = fileProvider.LoadAllBundles().Where(x => includeDraft || !x.IsDraft);
            var allowed = GetChallengesCanBeViewedByMember(member, fileProvider);
            bundles = bundles.Where(x => x.Challenges.Any(c => allowed.ContainsKey(c))).ToList();
            return bundles;
        }

        public static IBundle GetBundle(IMember member, string id, IFileProvider fileProvider)
        {
            var bundle = fileProvider.LoadBundle(id);
            var count = bundle.Challenges.Count;
            var allowed = GetChallengesCanBeViewedByMember(member, fileProvider);
            bundle.Challenges = bundle.Challenges.Where(x => allowed.ContainsKey(x)).ToList();
            if (bundle.Challenges.Count == 0 && count > 0)
            {
                throw new ChallengeLockedForUserException($"All challenges of bundle {bundle.Id} are locked for user {member.Name}");
            }

            return bundle;
        }

        public static bool CanAccessChallenge(IMember member, Challenge challenge, IFileProvider fileProvider)
        {
            return GetChallengesCanBeViewedByMember(member, fileProvider).ContainsKey(challenge.Id);
        }

        public static void UpdateIsBundleFlag(IFileProvider fileProvider)
        {
            var bundles = fileProvider.LoadAllBundles().SelectMany(x => x.Challenges).Distinct().ToDictionary(x => x);

            foreach (var challengeId in fileProvider.GetChallengeIds())
            {
                var isInBundle = bundles.ContainsKey(challengeId);
                using var writeLock = fileProvider.GetLock();
                var challenge = fileProvider.LoadChallenge(challengeId, writeLock);
                if (challenge.State.IsPartOfBundle != isInBundle)
                {
                    challenge.State.IsPartOfBundle = isInBundle;
                    fileProvider.SaveChallenge(challenge, writeLock);
                }
            }
        }

        public static void RenameChallengeInGroups(IChallenge challenge, string newId, IFileProvider fileProvider)
        {
            var groups = fileProvider.LoadAllGroups();
            foreach (var group in groups)
            {
                using var writeLock = fileProvider.GetLock();
                var updated = fileProvider.LoadGroup(@group.Id, writeLock);
                var indexAvailable = Array.IndexOf(updated.AvailableChallenges, challenge.Id);
                if (indexAvailable >= 0)
                {
                    updated.AvailableChallenges[indexAvailable] = newId;
                }

                var indexForced = Array.IndexOf(updated.ForcedChallenges, challenge.Id);
                if (indexForced >= 0)
                {
                    updated.ForcedChallenges[indexForced] = newId;
                }

                if (indexForced >= 0 || indexAvailable >= 0)
                {
                    fileProvider.SaveGroup(updated, writeLock);
                }
            }
        }

        public static void RenameChallengeInBundles(IChallenge challenge, string newId, IFileProvider fileProvider)
        {
            var bundlesWithChallenge = fileProvider.LoadAllBundles().Where(p => p.Challenges.Any(y => y == challenge.Id));
            foreach (var bundle in bundlesWithChallenge)
            {
                using var writeLock = fileProvider.GetLock();
                var updated = fileProvider.LoadBundle(bundle.Id, writeLock);
                var index = bundle.Challenges.IndexOf(challenge.Id);
                updated.Challenges.RemoveAt(index);
                updated.Challenges.Insert(index, newId);
                fileProvider.SaveBundle(updated, writeLock);
            }
        }

        public static void RenameChallengeInRecentActivities(IChallenge challenge, string newId, IFileProvider fileProvider)
        {
            using var writeLock = fileProvider.GetLock();
            var activities = fileProvider.LoadRecentActivities(writeLock);
            foreach (var activity in activities)
            {
                if (activity.Reference == challenge.Id)
                {
                    activity.Reference = newId;
                }
            }

            fileProvider.SaveRecentActivities(activities, writeLock);
        }

        public static void RenameChallengeInMembers(IChallenge challenge, string newId, IFileProvider fileProvider)
        {
            var members = fileProvider.LoadMembers();
            foreach (var member in members)
            {
                var indexSolved = Array.IndexOf(member.SolvedChallenges, challenge.Id);
                var indexUnlocked = Array.IndexOf(member.UnlockedChallenges, challenge.Id);
                var indexCanRate = Array.IndexOf(member.CanRate, challenge.Id);
                if (indexUnlocked >= 0 || indexSolved >= 0 || indexCanRate >= 0)
                {
                    using var writeLock = fileProvider.GetLock();
                    var updated = fileProvider.LoadMember(member.Id, writeLock);
                    if (indexSolved >= 0)
                    {
                        updated.SolvedChallenges[indexSolved] = newId;
                    }

                    if (indexUnlocked >= 0)
                    {
                        updated.UnlockedChallenges[indexUnlocked] = newId;
                    }

                    if (indexCanRate >= 0)
                    {
                        updated.CanRate[indexCanRate] = newId;
                    }

                    fileProvider.SaveMember(updated, writeLock);
                }
            }
        }

        public static void DeleteChallengeInRecentActivities(IChallenge challenge, IFileProvider fileProvider)
        {
            using var writeLock = fileProvider.GetLock();
            var activities = fileProvider.LoadRecentActivities(writeLock);
            var filtered = activities.Where(x => x.Reference != challenge.Id).ToList();
            fileProvider.SaveRecentActivities(filtered, writeLock);
        }

        public static void DeleteChallengeInBundles(IChallenge challenge, IFileProvider fileProvider)
        {
            var bundlesWithChallenge = fileProvider.LoadAllBundles().Where(p => p.Challenges.Any(y => y == challenge.Id));
            foreach (var bundle in bundlesWithChallenge)
            {
                using var writeLock = fileProvider.GetLock();
                var updated = fileProvider.LoadBundle(bundle.Id, writeLock);
                bundle.Challenges.Remove(challenge.Id);
                fileProvider.SaveBundle(updated, writeLock);
            }
        }

        public static void DeleteChallengeInGroups(IChallenge challenge, IFileProvider fileProvider)
        {
            var groups = fileProvider.LoadAllGroups();
            foreach (var group in groups)
            {
                using var writeLock = fileProvider.GetLock();
                var updated = fileProvider.LoadGroup(@group.Id, writeLock);
                var indexAvailable = Array.IndexOf(updated.AvailableChallenges, challenge.Id);
                if (indexAvailable >= 0)
                {
                    updated.AvailableChallenges = updated.AvailableChallenges.Where(x => x != challenge.Id).ToArray();
                }

                var indexForced = Array.IndexOf(updated.ForcedChallenges, challenge.Id);
                if (indexForced >= 0)
                {
                    updated.ForcedChallenges = updated.ForcedChallenges.Where(x => x != challenge.Id).ToArray();
                }

                if (indexForced >= 0 || indexAvailable >= 0)
                {
                    fileProvider.SaveGroup(updated, writeLock);
                }
            }
        }

        public static void DeleteChallengeInMembers(IChallenge challenge, IFileProvider fileProvider)
        {
            var members = fileProvider.LoadMembers();
            using var memberLock = fileProvider.GetLock();
            foreach (var member in members)
            {
                var indexSolved = Array.IndexOf(member.SolvedChallenges, challenge.Id);
                if (indexSolved >= 0)
                {
                    member.SolvedChallenges = member.SolvedChallenges.Where(x => x != challenge.Id).ToArray();
                }

                var indexUnlocked = Array.IndexOf(member.UnlockedChallenges, challenge.Id);
                if (indexUnlocked >= 0)
                {
                    member.UnlockedChallenges = member.UnlockedChallenges.Where(x => x != challenge.Id).ToArray();
                }

                var indexCanRate = Array.IndexOf(member.CanRate, challenge.Id);
                if (indexCanRate >= 0)
                {
                    member.CanRate = member.CanRate.Where(x => x != challenge.Id).ToArray();
                }

                if (indexUnlocked >= 0 || indexSolved >= 0 || indexCanRate >= 0)
                {
                    fileProvider.SaveMember(member, memberLock);
                }
            }
        }

        public static void UpdateTestsForChallenge(Domain domain, IChallenge challenge, IEnumerable<ITest> tests)
        {
            using (var writeLock = domain.ProviderStore.FileProvider.GetLock())
            {
                var updateChallenge = domain.ProviderStore.FileProvider.LoadChallenge(challenge.Id, writeLock);
                domain.ProviderStore.FileProvider.SaveTestProperties(updateChallenge, tests.Cast<TestParameters>(), writeLock);
            }

            VerifyChallenge(domain, challenge.Id);
            domain.StatisticsOperations.LogChallengeActivity(challenge);
            var submissions = domain.ProviderStore.FileProvider.LoadAllSubmissionsFor(challenge);
            foreach (var submission in submissions)
            {
                domain.ProviderStore.FileProvider.MarkSubmissionForRerun(submission);
            }
        }
    }
}
