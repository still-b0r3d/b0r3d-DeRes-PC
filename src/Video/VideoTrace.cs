using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace B0r3dDeRes.Video
{
    // What each thread is doing in Windows' video system right now, and for how long. Windows'
    // video calls can't be timed out, so when one never comes back this is what says which one
    // (the tests' watchdog prints it).
    static class VideoTrace
    {
        static readonly ConcurrentDictionary<int, (string What, long Since)> doing = new ConcurrentDictionary<int, (string What, long Since)>();
        static readonly Stopwatch clock = Stopwatch.StartNew();

        // `what` is under way on this thread until the scope ends (one inside another puts the outer one back)
        public static Scope In(string what)
        {
            int id = Thread.CurrentThread.ManagedThreadId;
            bool had = doing.TryGetValue(id, out (string What, long Since) outer);
            string name = Thread.CurrentThread.Name;
            doing[id] = ((name != null ? name + ": " : "") + what, clock.ElapsedMilliseconds);
            return new Scope(id, had, outer);
        }

        public readonly struct Scope : IDisposable
        {
            readonly int id;
            readonly bool had;
            readonly (string What, long Since) outer;

            internal Scope(int id, bool had, (string What, long Since) outer)
            {
                this.id = id;
                this.had = had;
                this.outer = outer;
            }

            public void Dispose()
            {
                if (had) doing[id] = outer;
                else doing.TryRemove(id, out _);
            }
        }

        // every call under way, the longest first ("thread 9, b0r3d DeRes video: ReadSample (picture)
        // of clip.mp4, 12.3 s"); "" when there are none
        public static string Now()
        {
            long now = clock.ElapsedMilliseconds;
            return string.Join("; ", doing.ToArray().OrderBy(p => p.Value.Since)
                .Select(p => "thread " + p.Key + ", " + p.Value.What + ", " + ((now - p.Value.Since) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s"));
        }

        // a time in the video (100 ns steps) as seconds, for the trace's words
        public static string Seconds(long time) => (time / 1e7).ToString("0.000", CultureInfo.InvariantCulture) + " s";
    }
}
