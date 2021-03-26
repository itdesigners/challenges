using System.Collections.Generic;
using System.Linq;

namespace SubmissionEvaluation.Client.Services
{
    public class PasswordRequirementsService
    {
        private readonly bool gotDigit = true;
        private readonly bool gotLowerCase = true;
        private readonly bool gotUpperCase = true;
        private readonly int passwordLength = 12;
        private List<string> requirementsStringsList = new List<string>();

        public List<string> GetRequirementsString()
        {
            return requirementsStringsList;
        }

        public bool CheckRequirements(string password)
        {
            var result = true;
            requirementsStringsList.Clear();

            result &= CheckPasswordLength(password);
            result &= CheckUpperCase(password);
            result &= CheckLowerCase(password);
            result &= CheckDigit(password);

            return result;
        }

        private bool CheckPasswordLength(string password)
        {
            var result = true;

            if (password.Length < passwordLength || password.Length >= 128)
            {
                requirementsStringsList.Add("lengthFalse");
                result = false;
            }

            return result;
        }

        private bool CheckUpperCase(string password)
        {
            var result = true;

            if (password.Any(char.IsUpper) != (gotUpperCase || true))
            {
                requirementsStringsList.Add("upperFalse");
                result = false;
            }

            return result;
        }

        private bool CheckLowerCase(string password)
        {
            var result = true;

            if (password.Any(char.IsLower) != (gotLowerCase || true))
            {
                requirementsStringsList.Add("lowerFalse");
                result = false;
            }

            return result;
        }

        private bool CheckDigit(string password)
        {
            var result = true;

            if (password.Any(char.IsDigit) != (gotDigit || true))
            {
                requirementsStringsList.Add("digitFalse");
                result = false;
            }

            return result;
        }
    }
}
