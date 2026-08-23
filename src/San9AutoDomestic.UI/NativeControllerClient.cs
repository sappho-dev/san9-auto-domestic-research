using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace San9AutoDomestic.UI
{
    internal enum NativeBatchKind
    {
        Basic = 0,
        Wealthy = 1
    }

    internal enum NativeControllerState
    {
        Idle = 0,
        Inspecting = 1,
        Ready = 2,
        Starting = 3,
        WaitingForCurrentCityMenu = 4,
        Running = 5,
        Succeeded = 6,
        RebindRequired = 7,
        Declined = 8,
        Failed = 9
    }

    internal enum NativeProtocolAction
    {
        None = 0,
        ConfirmCurrentCityMenu = 1,
        ProtocolFailure = 2
    }

    internal interface INativeControllerClient
    {
        NativeInspectResult Inspect(
            NativeBatchDiagnosticLog diagnosticLog,
            Action<string> stageChanged);

        NativeBatchResult RunBatch(
            NativeBatchKind kind,
            Func<bool> confirmCurrentCityMenu,
            Action<string> jsonReceived,
            NativeBatchDiagnosticLog diagnosticLog,
            Action<string> stageChanged);
    }

    internal sealed class NativeInspectResult
    {
        public bool Ready { get; internal set; }

        public int ExitCode { get; internal set; }

        public string Status { get; internal set; }

        public string RawJson { get; internal set; }

        public string Error { get; internal set; }

        public int GameProcessId { get; internal set; }

        public long GameGeneration { get; internal set; }

        public int DetailFlags { get; internal set; }

        public int FirstFailurePoint { get; internal set; }

        public bool RequiresGameRestart { get; internal set; }
    }

    internal sealed class NativeBatchResult
    {
        public NativeControllerState State { get; internal set; }

        public int ExitCode { get; internal set; }

        public int Executed { get; internal set; }

        public int Skipped { get; internal set; }

        public bool RebindRequired { get; internal set; }

        public IList<string> RawJsonLines { get; internal set; }

        public string Stderr { get; internal set; }

        public string Error { get; internal set; }

        public int GameProcessId { get; internal set; }

        public bool ControllerProcessStarted { get; internal set; }

        public bool RequiresGameRestart { get; internal set; }
    }

    internal sealed class NativeBatchDiagnosticLog
    {
        private readonly object sync;
        private readonly string filePath;
        private readonly string latestFilePath;

        private NativeBatchDiagnosticLog(string filePath, string latestFilePath)
        {
            this.filePath = filePath;
            this.latestFilePath = latestFilePath;
            sync = new object();
        }

        internal string FilePath
        {
            get { return filePath; }
        }

        internal string LatestFilePath
        {
            get { return latestFilePath; }
        }

        internal static bool TryCreate(
            NativeBatchKind kind,
            out NativeBatchDiagnosticLog diagnosticLog,
            out string error)
        {
            return TryCreateInDirectory(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"),
                kind,
                out diagnosticLog,
                out error);
        }

        internal static bool TryCreateInDirectory(
            string logDirectory,
            NativeBatchKind kind,
            out NativeBatchDiagnosticLog diagnosticLog,
            out string error)
        {
            diagnosticLog = null;
            error = string.Empty;
            try
            {
                string fullDirectory = Path.GetFullPath(logDirectory);
                Directory.CreateDirectory(fullDirectory);
                string sessionName = string.Format(
                    CultureInfo.InvariantCulture,
                    "native-batch-{0:yyyyMMdd-HHmmssfff}-ui{1}-{2}-{3}.log",
                    DateTimeOffset.UtcNow,
                    Process.GetCurrentProcess().Id,
                    kind,
                    Guid.NewGuid().ToString("N").Substring(0, 8));
                string path = Path.Combine(fullDirectory, sessionName);
                string latestPath = Path.Combine(fullDirectory, "native-batch-latest.log");
                CreateEmptyDurableFile(path);
                CreateEmptyDurableFile(latestPath);

                diagnosticLog = new NativeBatchDiagnosticLog(path, latestPath);
                diagnosticLog.Write(
                    "SESSION_START",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "ui_pid={0} profile={1}",
                        Process.GetCurrentProcess().Id,
                        kind));
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal void Write(string stage, string detail)
        {
            string safeStage = string.IsNullOrWhiteSpace(stage) ? "UNKNOWN" : stage.Trim();
            string safeDetail = (detail ?? string.Empty)
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:O}\t{1}\t{2}{3}",
                DateTimeOffset.UtcNow,
                safeStage,
                safeDetail,
                Environment.NewLine);
            try
            {
                lock (sync)
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(line);
                    AppendDurably(filePath, bytes);
                    AppendDurably(latestFilePath, bytes);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void CreateEmptyDurableFile(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Flush(true);
            }
        }

        private static void AppendDurably(string path, byte[] bytes)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }
    }

    internal sealed class NativeControllerClient : INativeControllerClient
    {
        internal const string InspectArguments = "--inspect";
        internal const string BasicArguments =
            "--s8-basic-batch --confirm "
            + "I_ACCEPT_BASIC_BATCH_COMMERCE_CULTIVATE_PINNED_UNTIL_GAME_RESTART";
        internal const string WealthyArguments =
            "--s8-wealthy-batch --confirm "
            + "I_ACCEPT_WEALTHY_BATCH_PATROL_COMMERCE_CULTIVATE_TRAIN_REPAIR_PINNED_UNTIL_GAME_RESTART";
        internal const string MenuSignal = "OPEN_CURRENT_CITY_MENU";

        private const string ControllerSha256 =
            "7533734E1AACB3C52561C495CA54670F99DCC8AC4B6C43F5531F3E2D4522F90F";
        private const string BasicBridgeSha256 =
            "61F7D085D6DA47B9D6D1FDBEF73348D9E68EBA518553FFA0846C5D1F40E3ECCE";
        private const string WealthyBridgeSha256 =
            "224D8A8CC750002ECCD4D0D659B3514DB4C36BD783EC6A30574F4FCCEB3E98FA";

        public NativeInspectResult Inspect(
            NativeBatchDiagnosticLog diagnosticLog,
            Action<string> stageChanged)
        {
            ReportStage(
                diagnosticLog,
                stageChanged,
                "INSPECT_BEGIN",
                "正在检查原生运行环境和目标游戏。面板日志：" + LogPath(diagnosticLog));
            NativeInspectResult result = new NativeInspectResult();
            result.Status = string.Empty;
            result.RawJson = string.Empty;
            result.Error = string.Empty;
            string runtimeDirectory;
            string controllerPath;
            string validationError;
            if (!TryResolveAndValidateRuntime(
                    out runtimeDirectory,
                    out controllerPath,
                    out validationError))
            {
                result.ExitCode = -1;
                result.Error = validationError;
                ReportStage(
                    diagnosticLog,
                    stageChanged,
                    "INSPECT_RUNTIME_REJECTED",
                    "原生运行文件校验失败：" + validationError);
                return result;
            }

            StringBuilder stderr = new StringBuilder();
            object stderrLock = new object();
            try
            {
                ReportStage(
                    diagnosticLog,
                    stageChanged,
                    "INSPECT_PROCESS_STARTING",
                    "正在启动隐藏的只读 inspect 进程。没有加载批次 Bridge。 ");
                ProcessStartInfo startInfo = CreateStartInfo(
                    runtimeDirectory,
                    controllerPath,
                    InspectArguments,
                    false);
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        result.ExitCode = -1;
                        result.Error = "原生检测进程未能启动。";
                        ReportStage(
                            diagnosticLog,
                            stageChanged,
                            "INSPECT_PROCESS_NULL",
                            result.Error);
                        return result;
                    }

                    ReportStage(
                        diagnosticLog,
                        stageChanged,
                        "INSPECT_PROCESS_STARTED",
                        "只读 inspect 已启动，controller_pid="
                            + process.Id.ToString(CultureInfo.InvariantCulture));

                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (args.Data != null)
                        {
                            lock (stderrLock)
                            {
                                stderr.AppendLine(args.Data);
                            }
                            if (diagnosticLog != null)
                            {
                                diagnosticLog.Write("INSPECT_STDERR", args.Data);
                            }
                        }
                    };
                    process.BeginErrorReadLine();
                    string line;
                    int nonEmptyLines = 0;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        if (line.Length == 0)
                        {
                            continue;
                        }

                        nonEmptyLines++;
                        result.RawJson = line;
                        if (diagnosticLog != null)
                        {
                            diagnosticLog.Write("INSPECT_STDOUT", line);
                        }
                        Dictionary<string, object> json;
                        if (!NativeJson.TryParse(line, out json)
                            || !string.Equals(
                                NativeJson.StringValue(json, "mode"),
                                "inspect",
                                StringComparison.Ordinal))
                        {
                            result.Error = "原生检测输出不是预期的 inspect JSON。";
                        }
                        else
                        {
                            PopulateInspectResult(result, json);
                        }
                    }

                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                    if (diagnosticLog != null)
                    {
                        diagnosticLog.Write(
                            "INSPECT_EXIT",
                            "exit=" + result.ExitCode.ToString(CultureInfo.InvariantCulture));
                    }
                    if (nonEmptyLines != 1)
                    {
                        result.Ready = false;
                        result.Error = "原生检测必须只返回一行 JSON。";
                    }
                    else if (result.ExitCode != 0 || !result.Ready)
                    {
                        result.Ready = false;
                        if (string.IsNullOrEmpty(result.Error))
                        {
                            result.Error = result.RequiresGameRestart
                                ? "检测到当前游戏进程已有驻留 Bridge/idle slot 被占用；必须完全重启游戏后才能再次执行。"
                                : "原生检测未通过：" + result.Status;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                result.Ready = false;
                result.ExitCode = -1;
                result.Error = exception.Message;
                ReportStage(
                    diagnosticLog,
                    stageChanged,
                    "INSPECT_EXCEPTION",
                    "只读 inspect 异常：" + exception.Message);
            }

            lock (stderrLock)
            {
                if (!result.Ready && stderr.Length > 0)
                {
                    result.Error = string.IsNullOrEmpty(result.Error)
                        ? stderr.ToString().Trim()
                        : result.Error + " | " + stderr.ToString().Trim();
                }
            }

            ReportStage(
                diagnosticLog,
                stageChanged,
                result.Ready ? "INSPECT_READY" : "INSPECT_NOT_READY",
                result.Ready
                    ? "原生 inspect 已通过，准备启动所选批次。"
                    : "原生 inspect 未通过：" + result.Error);
            return result;
        }

        private static void PopulateInspectResult(
            NativeInspectResult result,
            Dictionary<string, object> json)
        {
            result.Ready = NativeJson.BooleanValue(json, "ready");
            result.Status = NativeJson.StringValue(json, "status") ?? string.Empty;
            result.GameProcessId = NativeJson.IntegerValue(json, "game_pid", 0);
            result.GameGeneration = NativeJson.Int64Value(json, "game_generation", 0L);
            result.DetailFlags = NativeJson.IntegerValue(json, "detail_flags", 0);
            result.FirstFailurePoint = NativeJson.IntegerValue(
                json,
                "first_failure_point",
                -1);
            result.RequiresGameRestart = string.Equals(
                    result.Status,
                    "EASY_NOT_INSTALLED",
                    StringComparison.Ordinal)
                && (result.DetailFlags & 512) != 0
                && result.FirstFailurePoint == 39;
        }

        internal static NativeInspectResult ParseInspectResultForTests(string line)
        {
            NativeInspectResult result = new NativeInspectResult
            {
                Status = string.Empty,
                RawJson = line ?? string.Empty,
                Error = string.Empty
            };
            Dictionary<string, object> json;
            if (NativeJson.TryParse(line, out json)
                && string.Equals(
                    NativeJson.StringValue(json, "mode"),
                    "inspect",
                    StringComparison.Ordinal))
            {
                PopulateInspectResult(result, json);
            }
            return result;
        }

        public NativeBatchResult RunBatch(
            NativeBatchKind kind,
            Func<bool> confirmCurrentCityMenu,
            Action<string> jsonReceived,
            NativeBatchDiagnosticLog diagnosticLog,
            Action<string> stageChanged)
        {
            if (confirmCurrentCityMenu == null)
            {
                throw new ArgumentNullException("confirmCurrentCityMenu");
            }

            ReportStage(
                diagnosticLog,
                stageChanged,
                "BATCH_BEGIN",
                "正在启动 " + kind + " 原生批次。日志：" + LogPath(diagnosticLog));

            NativeBatchProtocol protocol = new NativeBatchProtocol(kind);
            string runtimeDirectory;
            string controllerPath;
            string validationError;
            if (!TryResolveAndValidateRuntime(
                    out runtimeDirectory,
                    out controllerPath,
                    out validationError))
            {
                protocol.Fail(validationError);
                NativeBatchResult rejected = protocol.Complete(-1, string.Empty);
                ReportStage(
                    diagnosticLog,
                    stageChanged,
                    "BATCH_RUNTIME_REJECTED",
                    "批次运行文件校验失败：" + validationError);
                return rejected;
            }

            StringBuilder stderr = new StringBuilder();
            object stderrLock = new object();
            int exitCode = -1;
            bool controllerProcessStarted = false;
            try
            {
                ReportStage(
                    diagnosticLog,
                    stageChanged,
                    "BATCH_PROCESS_STARTING",
                    "正在启动隐藏的批次 controller。 ");
                ProcessStartInfo startInfo = CreateStartInfo(
                    runtimeDirectory,
                    controllerPath,
                    ArgumentsFor(kind),
                    true);
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        protocol.Fail("原生批次进程未能启动。");
                        NativeBatchResult missing = protocol.Complete(-1, string.Empty);
                        ReportStage(
                            diagnosticLog,
                            stageChanged,
                            "BATCH_PROCESS_NULL",
                            missing.Error);
                        return missing;
                    }

                    controllerProcessStarted = true;

                    ReportStage(
                        diagnosticLog,
                        stageChanged,
                        "BATCH_PROCESS_STARTED",
                        "批次 controller 已启动，controller_pid="
                            + process.Id.ToString(CultureInfo.InvariantCulture));

                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (args.Data != null)
                        {
                            lock (stderrLock)
                            {
                                stderr.AppendLine(args.Data);
                            }
                            if (diagnosticLog != null)
                            {
                                diagnosticLog.Write("CONTROLLER_STDERR", args.Data);
                            }
                            ReportStage(
                                diagnosticLog,
                                stageChanged,
                                "CONTROLLER_STDERR_SEEN",
                                "controller stderr：" + args.Data);
                        }
                    };
                    process.BeginErrorReadLine();
                    string line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        if (line.Length == 0)
                        {
                            continue;
                        }

                        if (diagnosticLog != null)
                        {
                            diagnosticLog.Write("CONTROLLER_STDOUT", line);
                        }

                        if (jsonReceived != null)
                        {
                            jsonReceived(line);
                        }

                        NativeProtocolAction action = protocol.AcceptLine(line);
                        if (action == NativeProtocolAction.ConfirmCurrentCityMenu)
                        {
                            ReportStage(
                                diagnosticLog,
                                stageChanged,
                                "WAITING_FOR_USER_MENU_SIGNAL",
                                "controller 正在等待：请打开当前城市菜单并完成第二次确认。 ");
                            bool approved;
                            try
                            {
                                if (diagnosticLog != null)
                                {
                                    diagnosticLog.Write("MENU_CONFIRM_CALLBACK_BEGIN", "waiting=true");
                                }
                                approved = confirmCurrentCityMenu();
                                if (diagnosticLog != null)
                                {
                                    diagnosticLog.Write(
                                        approved ? "MENU_CONFIRM_APPROVED" : "MENU_CONFIRM_DECLINED",
                                        "approved=" + approved);
                                }
                            }
                            catch (Exception exception)
                            {
                                protocol.Fail("菜单确认界面失败：" + exception.Message);
                                approved = false;
                                ReportStage(
                                    diagnosticLog,
                                    stageChanged,
                                    "MENU_CONFIRM_EXCEPTION",
                                    "菜单确认界面异常，将不发送信号：" + exception.Message);
                            }

                            if (protocol.RecordMenuDecision(approved))
                            {
                                if (diagnosticLog != null)
                                {
                                    diagnosticLog.Write("MENU_SIGNAL_WRITE_BEGIN", MenuSignal);
                                }
                                process.StandardInput.WriteLine(MenuSignal);
                                process.StandardInput.Flush();
                                ReportStage(
                                    diagnosticLog,
                                    stageChanged,
                                    "MENU_SIGNAL_SENT",
                                    "唯一菜单继续信号已写入并 Flush；等待原生批次结果。 ");
                            }
                            else
                            {
                                if (diagnosticLog != null)
                                {
                                    diagnosticLog.Write(
                                        "STDIN_CLOSE_MENU_DECLINED",
                                        "signal_count=" + protocol.SignalCount.ToString(CultureInfo.InvariantCulture));
                                }
                                process.StandardInput.Close();
                            }
                        }
                        else if (action == NativeProtocolAction.ProtocolFailure)
                        {
                            if (diagnosticLog != null)
                            {
                                diagnosticLog.Write("STDIN_CLOSE_PROTOCOL_FAILURE", line);
                            }
                            process.StandardInput.Close();
                        }
                    }

                    if (!process.HasExited)
                    {
                        if (diagnosticLog != null)
                        {
                            diagnosticLog.Write("STDIN_CLOSE_STDOUT_EOF", "post_signal=true");
                        }
                        process.StandardInput.Close();
                    }

                    process.WaitForExit();
                    exitCode = process.ExitCode;
                    if (diagnosticLog != null)
                    {
                        diagnosticLog.Write(
                            "BATCH_PROCESS_EXIT",
                            "exit=" + exitCode.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            catch (Exception exception)
            {
                protocol.Fail(exception.Message);
                ReportStage(
                    diagnosticLog,
                    stageChanged,
                    "BATCH_EXCEPTION",
                    "批次 controller 异常：" + exception.Message);
            }

            string stderrText;
            lock (stderrLock)
            {
                stderrText = stderr.ToString().Trim();
            }

            NativeBatchResult result = protocol.Complete(exitCode, stderrText);
            result.ControllerProcessStarted = controllerProcessStarted;
            result.RequiresGameRestart = result.RequiresGameRestart
                || controllerProcessStarted;
            ReportStage(
                diagnosticLog,
                stageChanged,
                "BATCH_FINAL",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "state={0} exit={1} executed={2} skipped={3} rebind={4} restart={5} error={6}",
                    result.State,
                    result.ExitCode,
                    result.Executed,
                    result.Skipped,
                    result.RebindRequired,
                    result.RequiresGameRestart,
                    result.Error ?? string.Empty));
            return result;
        }

        internal static string ArgumentsFor(NativeBatchKind kind)
        {
            switch (kind)
            {
                case NativeBatchKind.Basic:
                    return BasicArguments;
                case NativeBatchKind.Wealthy:
                    return WealthyArguments;
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        private static void ReportStage(
            NativeBatchDiagnosticLog diagnosticLog,
            Action<string> stageChanged,
            string stage,
            string message)
        {
            if (diagnosticLog != null)
            {
                diagnosticLog.Write(stage, message);
            }
            if (stageChanged != null)
            {
                stageChanged(message);
            }
        }

        private static string LogPath(NativeBatchDiagnosticLog diagnosticLog)
        {
            return diagnosticLog == null ? "未创建" : diagnosticLog.FilePath;
        }

        internal static ProcessStartInfo InspectStartInfoForTests(string baseDirectory)
        {
            return CreateClosedStartInfo(baseDirectory, InspectArguments, false);
        }

        internal static ProcessStartInfo BatchStartInfoForTests(
            string baseDirectory,
            NativeBatchKind kind)
        {
            return CreateClosedStartInfo(baseDirectory, ArgumentsFor(kind), true);
        }

        internal static string DescribeJsonLine(string line)
        {
            Dictionary<string, object> json;
            if (!NativeJson.TryParse(line, out json))
            {
                return "[原生输出异常] " + line;
            }

            string phase = NativeJson.StringValue(json, "phase");
            if (string.Equals(phase, "WAITING_FOR_USER_MENU_SIGNAL", StringComparison.Ordinal))
            {
                return "原生控制器已就绪：等待打开当前城市菜单。";
            }

            int nativeId = NativeJson.IntegerValue(json, "native_id", -1);
            string commandName = CommandName(nativeId);
            if (string.Equals(phase, "STEP_REQUEST_PUBLISHED", StringComparison.Ordinal))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "执行 {0}；选人 [{1}]；触发={2}",
                    commandName,
                    NativeJson.ArrayText(json, "top5"),
                    NativeJson.StringValue(json, "trigger") ?? "?");
            }

            if (string.Equals(phase, "STEP_SKIPPED", StringComparison.Ordinal))
            {
                int contextStatus = NativeJson.IntegerValue(json, "context_status", -1);
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "跳过 {0}；原因={1}（状态码={2}）",
                    commandName,
                    SkipReason(contextStatus),
                    contextStatus);
            }

            if (string.Equals(phase, "BATCH_COMPLETE", StringComparison.Ordinal))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "批次完成：执行 {0}，跳过 {1}。",
                    NativeJson.IntegerValue(json, "executed", 0),
                    NativeJson.IntegerValue(json, "skipped", 0));
            }

            if (string.Equals(phase, "BATCH_REBIND_REQUIRED", StringComparison.Ordinal))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "当前城市绑定已变化；停止前已执行 {0}，跳过 {1}，不会重试。",
                    NativeJson.IntegerValue(json, "executed", 0),
                    NativeJson.IntegerValue(json, "skipped", 0));
            }

            if (json.ContainsKey("result"))
            {
                return "原生控制器结果：" + NativeJson.IntegerValue(json, "result", -1);
            }

            return "[原生] " + line;
        }

        private static string CommandName(int nativeId)
        {
            switch (nativeId)
            {
                case 0:
                    return "巡察";
                case 1:
                    return "商业";
                case 2:
                    return "开垦";
                case 3:
                    return "修筑";
                case 5:
                    return "训练";
                default:
                    return "命令 " + nativeId.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string SkipReason(int contextStatus)
        {
            switch (contextStatus)
            {
                case 12:
                    return "资金不足";
                case 13:
                case 22:
                case 23:
                case 24:
                case 26:
                    return "该项数值已经完成";
                case 14:
                    return "本旬已经执行";
                case 17:
                    return "当前可用武将少于 5 人";
                case 25:
                    return "训练目标没有兵力";
                default:
                    return "原生条件不满足";
            }
        }

        private static ProcessStartInfo CreateClosedStartInfo(
            string baseDirectory,
            string arguments,
            bool batch)
        {
            string fullBase = Path.GetFullPath(baseDirectory);
            string runtimeDirectory = Path.GetFullPath(Path.Combine(fullBase, "runtime"));
            string controllerPath = Path.GetFullPath(
                Path.Combine(runtimeDirectory, "controller.exe"));
            return CreateStartInfo(runtimeDirectory, controllerPath, arguments, batch);
        }

        private static ProcessStartInfo CreateStartInfo(
            string runtimeDirectory,
            string controllerPath,
            string arguments,
            bool batch)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = controllerPath;
            startInfo.WorkingDirectory = runtimeDirectory;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardInput = batch;
            return startInfo;
        }

        private static bool TryResolveAndValidateRuntime(
            out string runtimeDirectory,
            out string controllerPath,
            out string error)
        {
            string baseDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            runtimeDirectory = Path.GetFullPath(Path.Combine(baseDirectory, "runtime"));
            controllerPath = Path.GetFullPath(Path.Combine(runtimeDirectory, "controller.exe"));
            error = string.Empty;
            if (!controllerPath.StartsWith(
                    runtimeDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "原生运行目录无效。";
                return false;
            }

            return VerifyFile(controllerPath, ControllerSha256, out error)
                && VerifyFile(
                    Path.Combine(runtimeDirectory, "bridge_s8_basic_batch.dll"),
                    BasicBridgeSha256,
                    out error)
                && VerifyFile(
                    Path.Combine(runtimeDirectory, "bridge_s8_wealthy_batch.dll"),
                    WealthyBridgeSha256,
                    out error);
        }

        private static bool VerifyFile(string path, string expectedHash, out string error)
        {
            error = string.Empty;
            if (!File.Exists(path))
            {
                error = "缺少原生运行文件：" + Path.GetFileName(path);
                return false;
            }

            string actualHash;
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                actualHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }

            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "原生运行文件哈希不匹配：" + Path.GetFileName(path);
                return false;
            }

            return true;
        }
    }

    internal sealed class NativeBatchProtocol
    {
        private readonly string expectedMode;
        private readonly List<string> lines;
        private NativeControllerState state;
        private bool waitingSeen;
        private bool menuDecisionRecorded;
        private bool signalSent;
        private bool completeSeen;
        private bool finalSeen;
        private bool finalResultZero;
        private bool rebindRequired;
        private bool restartRequired;
        private int gameProcessId;
        private string error;
        private int executed;
        private int skipped;

        internal NativeBatchProtocol(NativeBatchKind kind)
        {
            if (kind == NativeBatchKind.Basic)
            {
                expectedMode = "s8-basic-batch";
            }
            else if (kind == NativeBatchKind.Wealthy)
            {
                expectedMode = "s8-wealthy-batch";
            }
            else
            {
                throw new ArgumentOutOfRangeException("kind");
            }
            lines = new List<string>();
            state = NativeControllerState.Starting;
            error = string.Empty;
        }

        internal int SignalCount
        {
            get { return signalSent ? 1 : 0; }
        }

        internal NativeControllerState State
        {
            get { return state; }
        }

        internal NativeProtocolAction AcceptLine(string line)
        {
            lines.Add(line ?? string.Empty);
            Dictionary<string, object> json;
            if (string.IsNullOrEmpty(line) || !NativeJson.TryParse(line, out json))
            {
                return ProtocolFailure("原生控制器输出了非 JSON 内容。");
            }

            if (!string.Equals(
                    NativeJson.StringValue(json, "mode"),
                    expectedMode,
                    StringComparison.Ordinal))
            {
                return ProtocolFailure("原生控制器 mode 与请求不一致。");
            }

            string phase = NativeJson.StringValue(json, "phase");
            int lineGameProcessId = NativeJson.IntegerValue(json, "game_pid", 0);
            if (lineGameProcessId > 0)
            {
                gameProcessId = lineGameProcessId;
            }
            restartRequired = restartRequired
                || NativeJson.IntegerValue(json, "restart_required", 0) != 0;
            if (string.Equals(phase, "WAITING_FOR_USER_MENU_SIGNAL", StringComparison.Ordinal))
            {
                if (waitingSeen)
                {
                    return ProtocolFailure("原生控制器重复请求菜单信号。");
                }

                waitingSeen = true;
                state = NativeControllerState.WaitingForCurrentCityMenu;
                return NativeProtocolAction.ConfirmCurrentCityMenu;
            }

            if (!waitingSeen)
            {
                return ProtocolFailure("原生控制器在菜单握手前输出了批次事件。");
            }

            if (string.Equals(phase, "BATCH_REBIND_REQUIRED", StringComparison.Ordinal))
            {
                rebindRequired = true;
                executed = NativeJson.IntegerValue(json, "executed", executed);
                skipped = NativeJson.IntegerValue(json, "skipped", skipped);
                state = NativeControllerState.RebindRequired;
            }
            else if (string.Equals(phase, "BATCH_COMPLETE", StringComparison.Ordinal))
            {
                completeSeen = true;
                executed = NativeJson.IntegerValue(json, "executed", 0);
                skipped = NativeJson.IntegerValue(json, "skipped", 0);
                rebindRequired = NativeJson.BooleanValue(json, "rebind_required");
            }

            if (json.ContainsKey("result"))
            {
                finalSeen = true;
                finalResultZero = NativeJson.IntegerValue(json, "result", -1) == 0;
            }

            return NativeProtocolAction.None;
        }

        internal bool RecordMenuDecision(bool approved)
        {
            if (state != NativeControllerState.WaitingForCurrentCityMenu
                || menuDecisionRecorded)
            {
                ProtocolFailure("菜单确认只能处理一次。");
                return false;
            }

            menuDecisionRecorded = true;
            if (!approved)
            {
                state = NativeControllerState.Declined;
                return false;
            }

            signalSent = true;
            state = NativeControllerState.Running;
            return true;
        }

        internal void Fail(string message)
        {
            ProtocolFailure(message);
        }

        internal NativeBatchResult Complete(int exitCode, string stderr)
        {
            NativeControllerState finalState;
            if (state == NativeControllerState.Declined)
            {
                finalState = NativeControllerState.Declined;
            }
            else if (rebindRequired || exitCode == 23)
            {
                finalState = NativeControllerState.RebindRequired;
            }
            else if (string.IsNullOrEmpty(error)
                && waitingSeen
                && menuDecisionRecorded
                && signalSent
                && completeSeen
                && finalSeen
                && finalResultZero
                && exitCode == 0)
            {
                finalState = NativeControllerState.Succeeded;
            }
            else
            {
                finalState = NativeControllerState.Failed;
            }

            state = finalState;
            return new NativeBatchResult
            {
                State = finalState,
                ExitCode = exitCode,
                Executed = executed,
                Skipped = skipped,
                RebindRequired = finalState == NativeControllerState.RebindRequired,
                RawJsonLines = lines.AsReadOnly(),
                Stderr = stderr ?? string.Empty,
                Error = error,
                GameProcessId = gameProcessId,
                RequiresGameRestart = restartRequired
            };
        }

        private NativeProtocolAction ProtocolFailure(string message)
        {
            if (string.IsNullOrEmpty(error))
            {
                error = message ?? "原生控制器协议失败。";
            }

            if (state != NativeControllerState.Declined
                && state != NativeControllerState.RebindRequired)
            {
                state = NativeControllerState.Failed;
            }

            return NativeProtocolAction.ProtocolFailure;
        }
    }

    internal static class NativeJson
    {
        internal static bool TryParse(string line, out Dictionary<string, object> json)
        {
            json = null;
            try
            {
                json = new JavaScriptSerializer().DeserializeObject(line)
                    as Dictionary<string, object>;
                return json != null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        internal static string StringValue(Dictionary<string, object> json, string name)
        {
            object value;
            return json != null && json.TryGetValue(name, out value)
                ? value as string
                : null;
        }

        internal static bool BooleanValue(Dictionary<string, object> json, string name)
        {
            object value;
            return json != null && json.TryGetValue(name, out value)
                && value is bool && (bool)value;
        }

        internal static int IntegerValue(
            Dictionary<string, object> json,
            string name,
            int fallback)
        {
            object value;
            if (json == null || !json.TryGetValue(name, out value) || value == null)
            {
                return fallback;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        internal static long Int64Value(
            Dictionary<string, object> json,
            string name,
            long fallback)
        {
            object value;
            if (json == null || !json.TryGetValue(name, out value) || value == null)
            {
                return fallback;
            }

            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        internal static string ArrayText(Dictionary<string, object> json, string name)
        {
            object value;
            object[] items = json != null && json.TryGetValue(name, out value)
                ? value as object[]
                : null;
            if (items == null)
            {
                return string.Empty;
            }

            string[] text = new string[items.Length];
            for (int index = 0; index < items.Length; index++)
            {
                text[index] = Convert.ToString(items[index], CultureInfo.InvariantCulture);
            }

            return string.Join(",", text);
        }
    }
}
