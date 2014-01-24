using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MapleLib.WzLib;
using System.IO;
using System.Threading;
using MapleLib.WzLib.WzProperties;
using MapleLib.WzLib.Util;
using System.Net;
using System.IO.Compression;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Collections.Concurrent;

namespace Patcher
{
    public partial class Form1 : Form
    {
        private List<WzFile> toPatch = new List<WzFile>();
        private ConcurrentDictionary<string, byte[]> checksums = new ConcurrentDictionary<string, byte[]>();
        private int totalWzCount = 0;

        public Form1()
        {
            InitializeComponent();
            System.Timers.Timer patchTimer = new System.Timers.Timer();
            patchTimer.Elapsed += new System.Timers.ElapsedEventHandler(patchTimer_Elapsed);
            patchTimer.AutoReset = false;
            patchTimer.Interval = 200;
            patchTimer.Start();
        }

        void patchTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (this.Visible)
            {
                System.IO.DirectoryInfo v90Folder = new DirectoryInfo(Directory.GetCurrentDirectory());
                foreach (FileInfo file in v90Folder.GetFiles())
                {
                    if (file.Name.EndsWith(".wz"))
                    {
                        totalWzCount++;
                        Thread checksumThread = new Thread(generateMD5);
                    }
                }
                Console.WriteLine("Total wz count: {0}", totalWzCount);
                CheckPatch();
            }
            else
            {
                Thread.Sleep(100);
                patchTimer_Elapsed(sender, e);
            }
        }

        public void CheckPatch()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string versionFilePath = appDataPath + "/" + Program.appDir + Program.versionFile;
            Console.WriteLine("versionFilePath: " + versionFilePath);
            // create app data directory if it doesn't exist (first time launching the client)
            if (!Directory.Exists(appDataPath + "/" + Program.appDir))
            {
                Directory.CreateDirectory(appDataPath + "/" + Program.appDir);
                File.WriteAllText(versionFilePath, ((object)Program.version).ToString());
            }
            if (!File.Exists(versionFilePath))
            {
                File.WriteAllText(versionFilePath, ((object)Program.version).ToString());
            }
            // get client's patch version
            string clientVersion = File.ReadAllText(versionFilePath);
            Console.WriteLine("clientVersion: " + clientVersion);
            if (clientVersion.Length == 0)
            {
                //someone modified the local file
                Application.Exit();
            }
            Program.version = Int32.Parse(clientVersion);
            // download server patch version
            using (WebClient webClient = new WebClient())
            {
                try
                {
                    webClient.DownloadFile(Program.patchURL + Program.versionFile, versionFilePath + "dl");
                }
                catch (WebException)
                {
                    Console.WriteLine("Unable to connect! D:");
                    return;
                }
            }
            int currentVersion = Int32.Parse(File.ReadAllText(versionFilePath + "dl"));
            Console.WriteLine("serverVersion: {0}", currentVersion);
            // compare differences
            if (Program.version > currentVersion)
            {
                // someone modified the local file
                Program.version = 0; // force a complete patch
            }
            if (Program.version < currentVersion)
            {
                Console.WriteLine("Patching.");
                using (WebClient webClient = new WebClient())
                {
                    for (int i = Program.version + 1; i <= currentVersion; i++)
                    {
                        Console.WriteLine("Downloading patch {0} / {1}", i - Program.version, currentVersion - Program.version);
                        string zip = "patch" + i + ".zip";
                        string url = Program.patchURL + zip;
                        string path = appDataPath + "/" + Program.appDir + zip;
                        webClient.DownloadFile(url, path);
                        applyPatch(appDataPath + "/" + Program.appDir, zip, i);
                    }
                    // Download MD5 checksums of updated wz files

                    string checksumPath = Program.patchURL + "checksums";
                    MemoryStream checksumStream = new MemoryStream(webClient.DownloadData(checksumPath));
                    // TODO
                    checksumStream.Dispose();
                }
                for (int i = 0; i < toPatch.Count; i++)
                {
                    Console.WriteLine("Saving Files ({0} / {1})", i + 1, toPatch.Count);
                    Console.WriteLine("Saving wzFile: {0} to {1}", toPatch[i].Name, toPatch[i].FilePath);
                    string wzPath = toPatch[i].FilePath;
                    toPatch[i].SaveToDisk(toPatch[i].FilePath + ".PATCH");
                    toPatch[i].Dispose();
                    while (!tryDelete(wzPath))
                    {
                        Thread.Sleep(1000);
                    }
                    File.Move(wzPath + ".PATCH", wzPath);
                    Console.WriteLine("Done Saving {0}", wzPath);
                }
            }
            System.IO.DirectoryInfo downloadedMessageInfo = new DirectoryInfo(appDataPath + "/" + Program.appDir);
            foreach (FileInfo file in downloadedMessageInfo.GetFiles())
            {
                while (!tryDelete(file.FullName))
                {
                    Thread.Sleep(1000);
                }
            }
            Program.version = currentVersion;
            File.WriteAllText(versionFilePath, "" + Program.version);
            Console.WriteLine("Ready to launch MS");
            launchMS();
            // Change thing to 'Play' button here from patching info
        }

        private void button1_Click(object sender, EventArgs e)
        {
            loadDifferences();
        }

        public void applyPatch(string dir, string zipFile, int patchNum)
        {
            Unzip.UnzipFile(dir, zipFile, patchNum);
            string[] patchInfo = File.ReadAllLines(dir + "patch" + patchNum + "info");
            Dictionary<String, List<String>> patchFiles = new Dictionary<String, List<String>>();
            for (int i = 1; i < patchInfo.Length; i++)
            {
                if (i % 2 == 1) //.img info
                {
                    string[] imgPaths = patchInfo[i].Split(new char[] { ' ' });
                    string rootFile = patchInfo[i - 1];
                    Console.WriteLine("Root: {0}", rootFile);
                    patchFiles.Add(rootFile, new List<String>());
                    foreach (string imgPath in imgPaths)
                    {
                        patchFiles[rootFile].Add(imgPath);
                        Console.WriteLine("Added {0}/{1}", rootFile, imgPath);
                    }
                }
            }
            patch(patchFiles, dir, patchNum);
            Console.WriteLine("Done applying patch {0}", patchNum);
        }

        public void patch(Dictionary<String, List<String>> patches, string dir, int patchNum)
        {
            WzMapleVersion vrs = WzMapleVersion.GMS;
            foreach (String wzName in patches.Keys)
            {
                WzFile wzFile = null;
                foreach (WzFile patching in toPatch)
                {
                    if (patching.Name == wzName)
                    {
                        Console.WriteLine("Currently patching: {0}, nextToPatch: {1}", patching.Name, wzName);
                        wzFile = patching;
                        break;
                    }
                }
                if (wzFile == null)
                {
                    wzFile = new WzFile(wzName, vrs);
                    wzFile.ParseWzFile();
                }
                foreach (string img in patches[wzName])
                {
                    WzImage patchedImg = null;
                    string[] subdirs = img.Split(new char[] { '/' });
                    WzDirectory targetDir = wzFile.WzDirectory;
                    string targetImgName = null;
                    foreach (string subdir in subdirs)
                    {
                        Console.WriteLine("subdir: {0}", subdir);
                        if (!subdir.EndsWith(".img")) // if this isn't the img
                        {
                            targetDir = targetDir.GetDirectoryByName(subdir);
                            if (targetDir == null)
                            {
                                Console.WriteLine("ERROR: {0} is not a valid directory.", subdir);
                                return;
                            }
                        }
                        else
                        {
                            targetImgName = subdir;
                        }

                    }
                    patchedImg = new WzImage(targetImgName, File.OpenRead(dir + "-" + patchNum + targetImgName), vrs);
                    patchedImg.ParseImage();
                    WzImage targetImg = targetDir.GetImageByName(targetImgName);
                    if (targetImg != null) // patching an existing .img
                    {
                        targetDir.RemoveImage(targetImg);
                        targetDir.AddImage(patchedImg);
                        patchedImg.changed = true;
                        Console.WriteLine("Existing wz img found for name: {0} and successfully applied patch.", img);
                    }
                    else
                    {
                        targetDir.AddImage(patchedImg);
                        patchedImg.changed = true;
                        Console.WriteLine("Added new wz img with name: {0} in dir: {1} and sucessfully applied patch.", patchedImg.Name, targetDir.Name);
                    }
                }
                if (!toPatch.Contains(wzFile))
                {
                    toPatch.Add(wzFile);
                }
            }
        }

        private void launchMS()
        {  
            // Create DnsEndPoint. The hostName and port are passed in to this method.
            DnsEndPoint hostEntry = new DnsEndPoint(Program.serverIP, Program.authPort);
            // Create a stream-based, TCP socket using the InterNetwork Address Family. 
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(hostEntry);
            // wait until server registers the IP and closes connection
            socket.Close();
            System.IO.DirectoryInfo v90Folder = new DirectoryInfo(Directory.GetCurrentDirectory());
            // Check if MD5 checksums are correct
            foreach (FileInfo f in v90Folder.GetFiles())
            {
                if (f.Name.EndsWith(".wz"))
                {

                }
            }
            Process p = new Process();
            
            bool foundExe = false;
            foreach (FileInfo file in v90Folder.GetFiles())
            {
                if (file.Name.ToLower().EndsWith(".exe") && file.Name.Contains("v90") && file.Name.Contains("rahul"))
                {
                    p.StartInfo = new ProcessStartInfo(file.Name);
                    foundExe = true;
                }
            }
            if (!foundExe)
            {
                Console.WriteLine("Place in MS folder and re-open, or re-name the MapleStory client to v90-rahul.exe");
            }
            else 
            {
                p.Start();
            }
        }

        void loadDifferences()
        {
            // here we would query a website for latest patch rev.
            WzMapleVersion vrs = WzMapleVersion.GMS; // is classic old GMS??
            string imgPath = textBox1.Text;
            string wzPath = textBox2.Text;
            Console.WriteLine("imgPath: " + imgPath + ", wzPath: " + wzPath);
            WzFile affected = new WzFile(wzPath, vrs);
            affected.ParseWzFile();
            char[] split = { '\\', '/'};
            string imgName = imgPath.Split(split)[imgPath.Split(split).Length - 1].Trim();
            Console.WriteLine("imgName: " + imgName);
            WzImage toPatch = affected.WzDirectory.GetImageByName(imgName);
            FileStream stream = File.OpenRead(imgPath);
            WzImage img = new WzImage("-" + imgName, stream, vrs);
            img.ParseImage();
            toPatch.ParseImage();
            toPatch.ClearProperties();
            toPatch.AddProperties(img.WzProperties);
            affected.WzDirectory.GetImageByName(imgName).changed = true;
            affected.SaveToDisk(wzPath + ".new");
            affected.Dispose();
            stream.Close();
            while (!tryDelete(wzPath))
            {
                Thread.Sleep(1000); // ensure that we can rename the file
            }
            File.Move(wzPath + ".new", wzPath); // rewrite w/ patched file
            button1.Text = "Done!";
        }

        bool tryDelete(string fileName)
        {
            try
            {
                File.Delete(fileName);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void generateMD5(object filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead((string)filename))
                {
                    byte[] checksum = md5.ComputeHash(stream);
                    string[] split = ((string)filename).Split(new char[] { '\\' });
                    checksums.TryAdd(split[split.Length - 1], checksum);
                }
            }
        }
    }
}
