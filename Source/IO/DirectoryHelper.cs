using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;

namespace ExDevilLee.IO
{
    /// <summary>
    /// Provide some useful helper class to extend Directory.
    /// </summary>
    public class DirectoryHelper
    {
        public static string[] GetFilesWithinLimits(string path, string searchPattern, int limits)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            Contract.Requires(!string.IsNullOrEmpty(searchPattern));

            if (!Directory.Exists(path)) return null;
            if (limits < 1) return null;

            List<string> result = new List<string>();
            foreach (string folder in Directory.GetDirectories(path))
            {
                if (result.Count >= limits) break;
                string[] files = GetFilesWithinLimits(folder, searchPattern, limits - result.Count);
                if (null != files && files.Length > 0)
                {
                    result.AddRange(GetArrayWithMaxLength(files, limits - result.Count));
                }
            }
            if (result.Count < limits)
            {
                string[] files = Directory.GetFiles(path, searchPattern);
                if (null != files && files.Length > 0)
                {
                    result.AddRange(GetArrayWithMaxLength(files, limits - result.Count));
                }
            }
            return result.ToArray();

            string[] GetArrayWithMaxLength(string[] source, int maxLength)
            {
                if (null == source || source.Length == 0 || maxLength < 1)
                    return new string[0];
                  
                if (source.Length <= maxLength)
                    return source;

                string[] destination = new string[maxLength];
                Array.Copy(source, destination, maxLength);
                return destination;
            }
        }
    }
}
