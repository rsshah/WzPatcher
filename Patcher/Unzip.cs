using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using System.IO;

namespace Patcher
{
    public class Unzip
    {
        public static void UnzipFile(string directory, string zipFile, int patchNum)
        {
            using (ZipInputStream s = new ZipInputStream(File.OpenRead(directory + zipFile)))
            {
                ZipEntry theEntry;
                while ((theEntry = s.GetNextEntry()) != null)
                {
                    string directoryName = Path.GetDirectoryName(theEntry.Name);

                    // create directory
                    if (directoryName.Length > 0)
                    {
                        Directory.CreateDirectory(directory + directoryName);
                    }
                    string fileName = directory + "-" + patchNum + theEntry.Name;
                    if (fileName.Contains("patch" + patchNum + "info"))
                    {
                        fileName = directory + theEntry.Name;
                    }
                    Console.WriteLine("Unzipping file: {0}", fileName);
                    using (FileStream streamWriter = File.Create(fileName))
                    {
                        int size = 2048;
                        byte[] data = new byte[2048];
                        while (true)
                        {
                            size = s.Read(data, 0, data.Length);
                            if (size > 0) streamWriter.Write(data, 0, size);
                            else break;
                        }
                    }
                    Console.WriteLine("Unzipped: {0}", theEntry.Name);
                }
            }
        }
    }
}
