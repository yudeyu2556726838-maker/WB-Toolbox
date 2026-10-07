using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace WBToolbox.Native.Infrastructure
{
    internal sealed class SingleInstanceCoordinator : IDisposable
    {
        private const string MutexName = "Local\\WBToolbox.Native.4.SingleInstance";
        private const string ActivationMessageName = "WBToolbox.Native.4.Activate";
        private static readonly IntPtr BroadcastWindow = new IntPtr(0xffff);

        private readonly Mutex mutex;
        private readonly uint activationMessage;
        private bool ownsMutex;
        private HwndSource windowSource;
        private Action activationAction;

        internal SingleInstanceCoordinator()
        {
            mutex = new Mutex(false, MutexName);
            try
            {
                ownsMutex = mutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            activationMessage = RegisterWindowMessage(ActivationMessageName);
            if (activationMessage == 0)
            {
                throw new InvalidOperationException("无法注册应用唤醒消息");
            }
        }

        internal bool IsPrimaryInstance
        {
            get { return ownsMutex; }
        }

        internal void Attach(Window window, Action onActivation)
        {
            if (!ownsMutex)
            {
                throw new InvalidOperationException("只有主实例可以监听唤醒消息");
            }
            if (window == null)
            {
                throw new ArgumentNullException("window");
            }
            if (onActivation == null)
            {
                throw new ArgumentNullException("onActivation");
            }

            activationAction = onActivation;
            window.SourceInitialized += delegate
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                windowSource = HwndSource.FromHwnd(handle);
                if (windowSource != null)
                {
                    windowSource.AddHook(WindowProcedure);
                }
            };
        }

        internal void SignalPrimaryInstance()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                PostMessage(BroadcastWindow, activationMessage, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(120);
            }
        }

        private IntPtr WindowProcedure(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((uint)message == activationMessage)
            {
                handled = true;
                if (activationAction != null)
                {
                    activationAction();
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (windowSource != null)
            {
                windowSource.RemoveHook(WindowProcedure);
                windowSource = null;
            }
            if (ownsMutex)
            {
                mutex.ReleaseMutex();
                ownsMutex = false;
            }
            mutex.Dispose();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string messageName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
