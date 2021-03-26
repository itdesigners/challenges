using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SubmissionEvaluation.Providers.CryptographyProvider
{
    public static class CryptographyProvider
    {
        private const string Name = "libsodium";
        private const int CryptoPwhashArgon2IdAlgArgon2Id13 = 2;
        private const long CryptoPwhashArgon2IdOpslimitSensitive = 4;
        private const int CryptoPwhashArgon2IdMemlimitSensitive = 33554432;

        static CryptographyProvider()
        {
            sodium_init();
        }

        public static string CreateArgon2Password(string password)
        {
            var saltArgon2 = CreateSalt();
            var pwdHashArgon2 = HashPassword(password, saltArgon2);
            return Convert.ToBase64String(pwdHashArgon2) + "$" + Convert.ToBase64String(saltArgon2);
        }

        private static byte[] CreateSalt()
        {
            var buffer = new byte[16];
            var rng = new RNGCryptoServiceProvider();
            rng.GetBytes(buffer);
            return buffer;
        }

        private static byte[] HashPassword(string password, byte[] salt)
        {
            var hash = new byte[16];

            var result = crypto_pwhash(hash, hash.Length, Encoding.UTF8.GetBytes(password), password.Length, salt, CryptoPwhashArgon2IdOpslimitSensitive,
                CryptoPwhashArgon2IdMemlimitSensitive, CryptoPwhashArgon2IdAlgArgon2Id13);

            if (result != 0)
            {
                throw new Exception("An unexpected error has occurred.");
            }

            return hash;
        }

        public static bool VerifyPassword(string password, string hash)
        {
            var verified = false;
            var subs = hash.Split("$");

            if (subs.Length == 2 && subs[1].Length > 0 && subs[0].Length > 0)
            {
                try
                {
                    var salt = Convert.FromBase64String(subs[1]);
                    var hashPassword = Convert.FromBase64String(subs[0]);
                    verified = VerifyHash(password, salt, hashPassword);
                }
                catch
                {
                }
            }

            return verified;
        }

        private static bool VerifyHash(string password, byte[] salt, byte[] hash)
        {
            var newHash = HashPassword(password, salt);
            return hash.SequenceEqual(newHash);
        }

#pragma warning disable IDE1006 // suppress naming convention for DLL import
        [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
        private static extern void sodium_init();

        [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void randombytes_buf(byte[] buffer, int size);

        [DllImport(Name, CallingConvention = CallingConvention.Cdecl)]
        private static extern int crypto_pwhash(byte[] buffer, long bufferLen, byte[] password, long passwordLen, byte[] salt, long opsLimit, int memLimit,
            int alg);
#pragma warning restore IDE1006
    }
}
