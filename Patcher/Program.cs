using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MapleLib.WzLib;
using System.IO;
using System.Net;
using System.Threading;

namespace Patcher
{
    static class Program
    {

        public static string patchURL = "http://50.70.53.154/patches/";
        public static string versionFile = "patch";
        public static string appDir = "Server Client/";
        public static string serverIP = "50.184.119.215";
        public static int authPort = 8485;
        public static int version = 0;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
