using System;
using System.Threading;
using System.Windows.Forms;

namespace San9AutoDomestic.UI
{
    internal static class Program
    {
        private const string ProcessMutexName =
            @"Local\San9AutoDomestic.San9Pk101.019FDA16";
        private const string ActivationEventName =
            @"Local\San9AutoDomestic.San9Pk101.019FDA16.Activate";
        private static MainForm mainForm;
        private static int handlingUiException;
        private static int showingFatalDialog;

        [STAThread]
        private static void Main()
        {
            try
            {
                RunSingleInstance();
            }
            catch (Exception exception)
            {
                ShowFatalDialog(
                    "助手无法继续启动，将安全退出。游戏不会被修改。",
                    exception);
            }
        }

        private static void RunSingleInstance()
        {
            using (Mutex processMutex = new Mutex(false, ProcessMutexName))
            using (EventWaitHandle activationEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                ActivationEventName))
            {
                bool ownsMutex;
                try
                {
                    ownsMutex = processMutex.WaitOne(0, false);
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                {
                    activationEvent.Set();
                    return;
                }

                try
                {
                    RunMessageLoop(activationEvent);
                }
                finally
                {
                    processMutex.ReleaseMutex();
                }
            }
        }

        private static void RunMessageLoop(EventWaitHandle activationEvent)
        {
            Application.SetUnhandledExceptionMode(
                UnhandledExceptionMode.CatchException);
            ThreadExceptionEventHandler threadExceptionHandler =
                delegate(object sender, ThreadExceptionEventArgs eventArgs)
                {
                    HandleUiThreadException(eventArgs.Exception);
                };
            UnhandledExceptionEventHandler domainExceptionHandler =
                delegate(object sender, UnhandledExceptionEventArgs eventArgs)
                {
                    Exception exception = eventArgs.ExceptionObject as Exception
                        ?? new InvalidOperationException(
                            "Unknown unhandled exception object: "
                            + Convert.ToString(eventArgs.ExceptionObject));
                    ShowFatalDialog(
                        eventArgs.IsTerminating
                            ? "助手发生不可恢复异常，即将安全退出。游戏不会被修改。"
                            : "助手发生未处理异常。游戏不会被修改。",
                        exception);
                };

            Application.ThreadException += threadExceptionHandler;
            AppDomain.CurrentDomain.UnhandledException += domainExceptionHandler;
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (MainForm form = new MainForm())
                using (SingleInstanceActivationMonitor activationMonitor =
                    new SingleInstanceActivationMonitor(
                        activationEvent,
                        delegate { QueueActivation(form); }))
                {
                    mainForm = form;
                    form.HandleCreated += delegate { activationMonitor.Start(); };
                    Application.Run(form);
                }
            }
            finally
            {
                mainForm = null;
                Application.ThreadException -= threadExceptionHandler;
                AppDomain.CurrentDomain.UnhandledException -= domainExceptionHandler;
            }
        }

        private static void QueueActivation(MainForm form)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated)
            {
                return;
            }

            try
            {
                form.BeginInvoke(new MethodInvoker(form.RestoreFromSecondInstance));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static void HandleUiThreadException(Exception exception)
        {
            if (Interlocked.Exchange(ref handlingUiException, 1) != 0)
            {
                return;
            }

            try
            {
                MainForm form = mainForm;
                if (form == null || form.IsDisposed)
                {
                    ShowFatalDialog(
                        "助手界面发生异常，无法保留主窗口。游戏不会被修改。",
                        exception);
                    return;
                }

                form.HandleRecoverableUiException(exception);
                MessageBox.Show(
                    form,
                    "助手遇到界面异常，但窗口会保留，所有执行能力仍关闭。\r\n\r\n"
                    + exception.Message
                    + "\r\n\r\n可点击“重新检测”重试。",
                    "San9AutoDomestic - 界面异常",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception handlingException)
            {
                ShowFatalDialog(
                    "助手界面异常，且恢复提示无法完成。游戏不会被修改。",
                    handlingException);
            }
            finally
            {
                Interlocked.Exchange(ref handlingUiException, 0);
            }
        }

        private static void ShowFatalDialog(string summary, Exception exception)
        {
            if (Interlocked.Exchange(ref showingFatalDialog, 1) != 0)
            {
                return;
            }

            try
            {
                MessageBox.Show(
                    summary + "\r\n\r\n" + (exception == null ? "未知错误" : exception.Message),
                    "San9AutoDomestic - 严重错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref showingFatalDialog, 0);
            }
        }
    }
}
