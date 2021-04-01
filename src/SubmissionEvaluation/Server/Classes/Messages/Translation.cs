using System.Collections.Generic;
using SubmissionEvaluation.Server.Models;

namespace SubmissionEvaluation.Server.Classes.Messages
{
    public static class Translation
    {
        private static readonly Dictionary<string, string> translations;

        static Translation()
        {
            translations = new Dictionary<string, string>
            {
                {ErrorMessages.WrongUserPassword, "Der eingegebene Nutzer oder das Passwort sind ungültig!"},
                {ErrorMessages.InvalidFile, "Entweder es wurden keine oder ungültige Dateien hochgeladen. Bitte prüfen und nochmals hochladen!"},
                {
                    ErrorMessages.InvalidJekyllUser,
                    "Es wurde kein Nutzer mit den entsprechenden Daten im Challenge System gefunden. <br/> Bitte wende dich an den Administrator!"
                },
                {ErrorMessages.GenericError, "Es ist ein Fehler aufgetreten! Sollte das Problem weiterhin bestehen, wende dich an den Administrator."},
                {ErrorMessages.ChallengeNameSpaces, "Der Projektname darf keine Leerstellen enthalten"},
                {ErrorMessages.NoPermission, "Du hast keine Berechtigung, diese Challenge zu bearbeiten! Nur Admins dürfen fremde Challenges bearbeiten."},
                {
                    ErrorMessages.ConfirmDeleteChallenge,
                    "Bitte bestätige, dass du die Challenge löschen möchtest. Scrolle dazu zum Seitenende und wählen den Button \"Entgültig löschen\" aus!"
                },
                {ErrorMessages.InvalidMarkdown, "<b>Ungültiges Markdown!</b>"},
                {
                    ErrorMessages.NoCompilerFound,
                    "Es wurde kein passender Compiler für diese Datei gefunden, entweder es existiert keiner oder er ist deaktivert. Falls das Problem weiterhin bestehen sollte, wende dich an den Administrator der Seite."
                },
                {
                    ErrorMessages.UploadError,
                    "Es ist ein Fehler beim Hochladen aufgetreten. Dies kann durch einen Verbindungsabbruch aufgrund einer langsamen Verbindung passieren. Eventuell ist auch deine Einreichung zu groß. Das Limit beträgt 100 MB."
                },
                {
                    ErrorMessages.IdError,
                    "Eine ungütige ID wurde übergeben. Falls du über einen Link hierhergekommen bist wende dich bitte an den Administrator!"
                },
                {ErrorMessages.IdAlreadyExists, "Die ID wird bereits verwendet. Probiere eine andere ID aus."},
                {
                    ErrorMessages.PreviousChallengesNotSolved,
                    "Du hast die vorherigen Aufgaben im Bundle nocht nicht gelöst. Du musst alle vorherigen Aufgaben gelöst haben um diese Aufgabe sehen zu können."
                },
                {ErrorMessages.MissingName, "Bitte einen Benutzernamen eingeben."},
                {ErrorMessages.MissingPassword, "Bitte ein Password eingeben."},
                {ErrorMessages.UserCreateFailed, "Anlegen des Benutzer fehlgeschlagen. Vermutlich ist der Benutzername oder Name (Vor- und Nachname) bereits vergeben."},
                {ErrorMessages.MissingMail, "Bitte eine E-Mailadresse eingeben"},
                {ErrorMessages.ActivationNeeded, "Der Account muss erst durch einen Administrator freigeschaltet werden."},
                {ErrorMessages.UserNotFound, "Gesuchter Nutzer konnte nicht gefunden werden."},
                {ErrorMessages.NoReview, "Für die Einreichung ist kein Review-Report verfügbar."},
                {ErrorMessages.ReviewGenerationFailed, "Generieren des Review-Reports fehlgeschlagen. Bitte bei Administrator melden."},
                {ErrorMessages.ReviewIsCurrentlyGenerated, "Review wird gerade generiert. Bitte versuche es in kürze erneut."},
                {ErrorMessages.MustJoinGroup, "Bitte mindestens eine Gruppe auswählen!"},
                {ErrorMessages.ChallengeLockedForUser, "Du wurdest für die Aufgabe noch nicht freigeschalten!"},
                {SuccessMessages.GenericSuccess, "Die Aktion wurde erfolgreich ausgeführt"},
                {SuccessMessages.EditChallenge, "Die Challenge wurde erfolgreich bearbeitet!"},
                {SuccessMessages.CreateChallenge, "Die Challenge wurde erfolgreich erstellt!"},
                {SuccessMessages.DeleteChallenge, "Die Challenge wurde erfolgreich gelöscht!"},
                {SuccessMessages.ChangeChallengeId, "Die Challenge ID wurde erfolgreich geändert!"},
                {SuccessMessages.CreateSubmission, "Die Einreichung wurde erfolgreich angelegt oder geupdated!"},
                {SuccessMessages.PublishTournament, "Die Challenge wurde erfolgreich veröffentlicht!"},
                {SuccessMessages.CreateTournament, "Das Turnier wurde erfolgreich erstellt!"},
                {SuccessMessages.EditTournament, "Das Turnier wurde erfolgreich bearbeitet!"},
                {SuccessMessages.DeleteTournament, "Das Turnier wurde erfolgreich gelöscht!"},
                {SuccessMessages.EditGame, "Das Spiel wurde erfolgreich bearbeitet!"},
                {SuccessMessages.DeleteGame, "Das Spiel wurde erfolgreich gelöscht!"},
                {SuccessMessages.PublishGame, "Das Spiel wurde erfolgreich veröffentlicht!"},
                {SuccessMessages.CreateGame, "Das Spiel wurde erfolgreich erstellt!"},
                {SuccessMessages.EditBundle, "Bundle wurde erfolgreich bearbeitet!"},
                {SuccessMessages.CreateBundle, "Bundle wurde erfolgreich erstellt!"},
                {SuccessMessages.RerunSubmission, "Bewertung von Submission erfolgreich neu gestartet"},
                {SuccessMessages.ReportedErrornuousSubmission, "Fehlerhafte Einreichung erfolgreich gemeldet"},
                {SuccessMessages.SubmitReview, "Review erfolgreich eingereicht"},
                {SuccessMessages.PasswordSent, "Das Password wurde an die Mail versandt"},
                {SuccessMessages.DeleteUser, "Nutzer erfolgreich gelöscht."},
                {SuccessMessages.DeadSubmissionsDeleted, "Nicht mehr lauffähige Submissions entfernt."}
            };
        }

        public static string GetMessage(GenericModel model)
        {
            if (!string.IsNullOrWhiteSpace(model.Message) && translations.TryGetValue(model.Message, out var text))
            {
                return text;
            }

            if (model.HasError)
            {
                return translations[ErrorMessages.GenericError];
            }

            return translations[SuccessMessages.GenericSuccess];
        }
    }
}
