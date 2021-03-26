using System;
using System.IO;

namespace SubmissionEvaluation.Compilers
{
    internal static class CompilerHelper
    {
        private static string MakeRelativePath(string workingDirectory, string fullPath)
        {
            string result;

            if (fullPath.StartsWith(workingDirectory + "/"))
            {
                result = fullPath.Replace(workingDirectory + "/", "");
            }
            else
            {
                throw new NotImplementedException("Do not know what to do! FIX ME!");
            }

            return result;
        }

        public static string GetRelativePath(string file, string folder)
        {
            // swap windows path to unix path style
            file = file.Replace("\\", "/");
            folder = folder.Replace("\\", "/");

            // finally determine the relative path
            return MakeRelativePath(folder, file);
        }

        public static string ConvertToSandboxPath(string path)
        {
            return path.Replace('\\', '/');
        }

        public static void Copy(string src, string dest, bool overwrite = true)
        {
            var target = Path.GetDirectoryName(dest);
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
            }

            File.Copy(src, dest, overwrite);
        }
    }
}
