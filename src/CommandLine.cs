using System;

namespace B0r3dDeRes
{
    // b0r3dDeRes.exe [--data <folder>] [picture]
    sealed class CommandLine
    {
        public const string Usage =
            "b0r3dDeRes.exe [--data <folder>] [picture]\n\n" +
            "--data <folder>   keep the settings in <folder> instead of \"b0r3dDeRes data\" next to b0r3dDeRes.exe\n" +
            "picture           a picture to open (a picture dropped on b0r3dDeRes.exe comes this way)";

        public string DataFolder;       // --data: where settings live (the PortableApps.com launcher passes its own)
        public string SelfTestReport;   // --selftest <file>: no window; see SelfTest
        public string Picture;          // a picture to open at the start
        public string Problem;          // what was wrong with the command line, or null

        public static CommandLine Parse(string[] args)
        {
            var line = new CommandLine();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value = null;
                int eq = arg.IndexOf('=');
                if (arg.StartsWith("--", StringComparison.Ordinal) && eq > 0)
                {
                    value = arg.Substring(eq + 1);
                    arg = arg.Substring(0, eq);
                }
                switch (arg.ToLowerInvariant())
                {
                    case "--data":
                        line.DataFolder = value ?? Next(args, ref i);
                        if (string.IsNullOrWhiteSpace(line.DataFolder)) line.Problem = "--data needs a folder after it.";
                        break;
                    case "--selftest":
                        line.SelfTestReport = value ?? Next(args, ref i);
                        if (string.IsNullOrWhiteSpace(line.SelfTestReport)) line.Problem = "--selftest needs a file after it.";
                        break;
                    default:
                        // anything else that looks like an option is a mistake; a plain word is a
                        // picture to open (the first one: a second is left alone)
                        if (arg.StartsWith("-", StringComparison.Ordinal) || arg.StartsWith("/", StringComparison.Ordinal) && arg.Length > 1 && arg.IndexOf('\\') < 0)
                            line.Problem = "b0r3d DeRes doesn't know " + args[i] + ".";
                        else if (line.Picture == null && !string.IsNullOrWhiteSpace(arg))
                            line.Picture = arg;
                        break;
                }
                if (line.Problem != null) break;
            }
            return line;
        }

        static string Next(string[] args, ref int i)
        {
            return i + 1 < args.Length ? args[++i] : null;
        }
    }
}
