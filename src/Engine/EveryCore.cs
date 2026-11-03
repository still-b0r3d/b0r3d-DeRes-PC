using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace B0r3dDeRes.Engine
{
    // Parallel.For, handing back what went wrong as itself rather than wrapped, so running out of
    // memory on one core is still an OutOfMemoryException and its callers can say so.
    static class EveryCore
    {
        public static void For(int from, int to, Action<int> body)
        {
            try
            {
                Parallel.For(from, to, body);
            }
            catch (AggregateException x)
            {
                Rethrow(x);
            }
        }

        // with something of each thread's own (its buffers)
        public static void For<T>(int from, int to, Func<T> own, Func<int, ParallelLoopState, T, T> body)
        {
            try
            {
                Parallel.For(from, to, own, body, _ => { });
            }
            catch (AggregateException x)
            {
                Rethrow(x);
            }
        }

        // what a core threw; when several did, running out of memory comes first, whichever core
        // got there first
        static void Rethrow(AggregateException x)
        {
            var all = x.Flatten().InnerExceptions;
            ExceptionDispatchInfo.Capture(all.FirstOrDefault(e => e is OutOfMemoryException) ?? all[0]).Throw();
        }
    }
}
