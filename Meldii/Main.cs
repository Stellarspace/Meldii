﻿using Microsoft.Win32;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;

namespace Meldii
{
    public class Program
    {
        [STAThreadAttribute]
        public static void Main(string[] arguments)
        {
            Func<bool> net45 = () => {
                // Class "ReflectionContext" exists from .NET 4.5 onwards.
                return Type.GetType("System.Reflection.ReflectionContext", false) != null;
            };

            // .NET 4.5 is an in-place upgrade to .NET 4.0.  For some reason some .NET 4.0 systems are
            // running Meldii, even though we are targetting .NET 4.5.  This will check to see if we are
            // being run on a system that has the .NET 4.5 framework installed.
            if (!net45())
            {
                if (MessageBox.Show("Meldii only supports .NET 4.5 or greater.\nDo you wish to download .NET 4.5?", "Runtime Version Error", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start("http://www.microsoft.com/en-us/download/details.aspx?id=42643");
                return;
            }

            LoadNatives();

            string args = "";
            for (int i = 0; i < arguments.Length; i++)
                args += arguments[i] + " ";
            args = args.Trim();

            Statics.LaunchArgs = args;

            ParseProtcol(args);

            // Update as an admin
            if (args == "--update")
                Statics.ShouldUpdate = true;

            AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(ErrorHandler);
            App.Main();
        }

        static void ParseProtcol(string args)
        {
            try
            {
                if (args == "--enable-one-click")
                {
                    try
                    {
                        RegistryKey key = Registry.ClassesRoot.CreateSubKey("melder", RegistryKeyPermissionCheck.ReadWriteSubTree);
                        key.SetValue("", "URL:Melder Protocol");
                        key.SetValue("URL Protocol", "");
                        key.CreateSubKey("DefaultIcon").SetValue("", "Meldii.exe,1");
                        key.CreateSubKey("shell").CreateSubKey("open").CreateSubKey("command").SetValue("", "\"" + Assembly.GetExecutingAssembly().Location + "\" \"%1\"");
                        key.Close();
                    }
                    catch (Exception)
                    {
                        Environment.Exit(1);
                    }

                    Environment.Exit(0);
                }
                else if (args == "--disable-one-click")
                {
                    try
                    {
                        Registry.ClassesRoot.DeleteSubKeyTree("melder");
                    }
                    catch (Exception)
                    {
                        Environment.Exit(1);
                    }

                    Environment.Exit(0);
                }
                else
                {
                    Match match = Regex.Match(args, Statics.MelderProtcolRegex);
                    if (match.Success)
                    {
                        string action = match.Groups[1].Value;
                        string provider = match.Groups[2].Value;
                        string url = match.Groups[3].Value;

                        if (action == "download")
                        {
                            if (provider == "forum") // Backwards Melder compat
                            {
                                Statics.OneClickInstallProvider = AddonProviders.AddonProviderType.FirefallForums;
                                Statics.OneClickAddonToInstall = url;
                            }
                            else // New stuff
                            {
                                try
                                {
                                    Statics.OneClickInstallProvider = (AddonProviders.AddonProviderType)Enum.Parse(typeof(AddonProviders.AddonProviderType), provider, true);
                                    Statics.OneClickAddonToInstall = url;
                                }
                                catch (Exception)
                                {

                                }
                            }
                        }
                    }
                }
            }
            catch(Exception)
            {

            }
        }

        public static string ParseException(Exception ex)
        {
            var sep = "---------------------------------------------------------";
            var ret = new System.Text.StringBuilder();
            var cur = ex;
            int num = 1;
            do
            {
                ret.AppendFormat("\r\n\r\n{0} - Exception\r\n{1}", num.ToString(), sep);
                ret.AppendFormat("\r\n.NET Runtime Version: {0}", Environment.Version.ToString());
                ret.AppendFormat("\r\nOS: {0}", Environment.OSVersion.ToString());
                ret.AppendFormat("\r\nType: {0}", cur.GetType().FullName);

                var ignore = new[] { "InnerException", "StackTrace", "Data", "HelpLink" };
                var properties = ex.GetType().GetProperties();
                foreach (var property in properties)
                {
                    if (ignore.Any(x => property.Name.Contains(x)))
                        continue;

                    var val = property.GetValue(cur, null);
                    if (val == null)
                        ret.AppendFormat("\r\n{0}: NULL", property.Name);
                    else ret.AppendFormat("\r\n{0}: {1}", property.Name, val);
                }

                if (cur.StackTrace != null)
                {
                    ret.AppendFormat("\r\n\r\nStackTrace\r\n{0}", sep);
                    ret.AppendFormat("\r\n{0}", cur.StackTrace);
                }

                cur = cur.InnerException;
                ++num;
            }
            while (cur != null);

            return ret.ToString();
        }

        static void ErrorHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            File.WriteAllText(@"Meldii Errors make Arkii sad.txt", ParseException(e));
            MessageBox.Show("Meldii has encountered an error.  Please check the exception logs.", "Error");
        }

        private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();
            AssemblyName assemblyName = new AssemblyName(args.Name);

            string path = assemblyName.Name + ".dll";
            if (assemblyName.CultureInfo.Equals(CultureInfo.InvariantCulture) == false)
            {
                path = String.Format(@"{0}\{1}", assemblyName.CultureInfo, path);
            }

            using (Stream stream = executingAssembly.GetManifestResourceStream(path))
            {
                if (stream == null)
                    return null;

                byte[] assemblyRawBytes = new byte[stream.Length];
                stream.Read(assemblyRawBytes, 0, assemblyRawBytes.Length);
                return Assembly.Load(assemblyRawBytes);
            }
        }

        #region Loading & Unloading native dlls (git binaries)

        private static void WriteResourceToFile(string resourceName, string fileName)
        {
            string dir = new FileInfo(fileName).Directory.FullName;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                using (var file = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                {
                    resource.CopyTo(file);
                }
            }
        }

        private static void LoadNatives()
        {
            // Extract native git lib
            string libPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meldii", "bin");
            string libgit2 = libPath + "\\git2-4d6362b.dll";

            if(!File.Exists(libgit2))
            {
                WriteResourceToFile("Meldii.Resources.git2-4d6362b.dll", libgit2);
            }

            // Append ...AppData\Local\Meldii\bin to PATH
            Environment.SetEnvironmentVariable("PATH", string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}", libPath, Path.PathSeparator, Environment.GetEnvironmentVariable("PATH")));
        }

        #endregion
    }
}
