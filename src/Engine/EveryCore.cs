using System;
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
                ExceptionDispatchInfo.Capture(x.Flatten().InnerExceptions[0]).Throw();
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
                ExceptionDispatchInfo.Capture(x.Flatten().InnerExceptions[0]).Throw();
            }
        }
    }
}
