using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Plexim.dNetTools.PlxAsamXilTool
{
    class EventLoop
    {
        private Queue<Action> _actionQueue = new Queue<Action>(); // FIFO
        private int _numPendingAsyncActions = 0;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private Thread _childThread;

        public EventLoop()
        {
        }

        public void Post(Action aAction)
        {
            lock (_actionQueue) 
            {
                _actionQueue.Enqueue(aAction);
            }
        }

        public CancellationTokenSource BeginAsyncAction()
        {
            Interlocked.Increment(ref _numPendingAsyncActions);
            return _cts;
        }

        public void EndAsyncAction()
        {
            Interlocked.Decrement(ref _numPendingAsyncActions);
        }

        public void EventManager()
        {
            System.Threading.Thread th = Thread.CurrentThread;
            //Console.WriteLine("Running event manager in {0}", th.Name);
            Console.WriteLine("Event loop started.");

            while (true)
            {
                Action action = null;
                lock (_actionQueue)
                {
                    if(_actionQueue.Count == 0){
                        if (_numPendingAsyncActions == 0)
                        {
                            break;
                        }      
                    }
                    else
                    {
                        action = _actionQueue.Dequeue();
                    } 
                }
                action?.Invoke();
                Thread.Yield();
            }

            Console.WriteLine("No more events.");
        }

        public void Cancel()
        {
            _cts.Cancel();
        }

        public void Run()
        {
            ThreadStart childref = new ThreadStart(EventManager);
            _childThread = new Thread(childref)
            {
                Name = "Event Manager Thread"
            };
            _childThread.Start();
        }

        public void Join()
        {
            _childThread.Join();
        }

        public void AssertRunningInEventThread()
        {
            Debug.Assert(Thread.CurrentThread == _childThread, "This code should be running in event loop thread.");
        }
    }
}
