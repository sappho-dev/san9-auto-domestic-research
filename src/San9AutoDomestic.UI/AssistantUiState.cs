using System;

namespace San9AutoDomestic.UI
{
    public enum AssistantUiStateKind
    {
        WaitingForGame = 0,
        Inspecting = 1,
        ReadOnlyReady = 2,
        ExecutionUnavailable = 3,
        Faulted = 4
    }

    public enum BatchActivityState
    {
        Unavailable = 0,
        Running = 1,
        StopRequested = 2,
        Stopped = 3
    }

    public enum AssistantUiTone
    {
        Neutral = 0,
        Information = 1,
        Warning = 2,
        Success = 3,
        Error = 4
    }

    public sealed class AssistantUiState
    {
        private AssistantUiState(
            AssistantUiStateKind kind,
            BatchActivityState batchActivity,
            string detail,
            int? processId,
            DateTimeOffset? completedUtc,
            string targetSummary,
            string citySummary,
            bool snapshotAvailable)
        {
            Kind = kind;
            BatchActivity = batchActivity;
            Detail = detail ?? string.Empty;
            ProcessId = processId;
            CompletedUtc = completedUtc;
            TargetSummary = targetSummary ?? string.Empty;
            CitySummary = citySummary ?? string.Empty;
            SnapshotAvailable = snapshotAvailable;
        }

        public AssistantUiStateKind Kind { get; private set; }

        public BatchActivityState BatchActivity { get; private set; }

        public string Detail { get; private set; }

        public int? ProcessId { get; private set; }

        public DateTimeOffset? CompletedUtc { get; private set; }

        public string TargetSummary { get; private set; }

        public string CitySummary { get; private set; }

        public bool SnapshotAvailable { get; private set; }

        public static AssistantUiState WaitingForGame(string detail)
        {
            return new AssistantUiState(
                AssistantUiStateKind.WaitingForGame,
                BatchActivityState.Unavailable,
                detail,
                null,
                null,
                "等待游戏上线后验证",
                "暂无只读快照",
                false);
        }

        public static AssistantUiState Inspecting(int processId)
        {
            return new AssistantUiState(
                AssistantUiStateKind.Inspecting,
                BatchActivityState.Unavailable,
                "正在执行严格只读检测；扫描完成后进程句柄会立即释放。",
                processId,
                null,
                "正在验证精确版本…",
                "正在读取只读快照…",
                false);
        }

        public static AssistantUiState ReadOnlyReady(
            int? processId,
            DateTimeOffset? completedUtc,
            string targetSummary,
            string citySummary)
        {
            return new AssistantUiState(
                AssistantUiStateKind.ReadOnlyReady,
                BatchActivityState.Unavailable,
                "只读快照可用于静态预览；无感执行开发中，当前未开放。",
                processId,
                completedUtc,
                targetSummary,
                citySummary,
                true);
        }

        public static AssistantUiState ExecutionUnavailable(
            string detail,
            int? processId,
            DateTimeOffset? completedUtc,
            string targetSummary,
            string citySummary,
            bool snapshotAvailable)
        {
            return new AssistantUiState(
                AssistantUiStateKind.ExecutionUnavailable,
                BatchActivityState.Unavailable,
                detail,
                processId,
                completedUtc,
                targetSummary,
                citySummary,
                snapshotAvailable);
        }

        public static AssistantUiState Faulted(string detail)
        {
            return new AssistantUiState(
                AssistantUiStateKind.Faulted,
                BatchActivityState.Unavailable,
                detail,
                null,
                null,
                "未知；请重新检测",
                "暂无可用快照",
                false);
        }

        public AssistantUiState WithBatchActivity(BatchActivityState batchActivity)
        {
            return new AssistantUiState(
                Kind,
                batchActivity,
                Detail,
                ProcessId,
                CompletedUtc,
                TargetSummary,
                CitySummary,
                SnapshotAvailable);
        }
    }

    public sealed class AssistantUiPresentation
    {
        public string BannerText { get; internal set; }

        public AssistantUiTone BannerTone { get; internal set; }

        public string ConnectionText { get; internal set; }

        public AssistantUiTone ConnectionTone { get; internal set; }

        public string TargetText { get; internal set; }

        public AssistantUiTone TargetTone { get; internal set; }

        public string CityText { get; internal set; }

        public AssistantUiTone CityTone { get; internal set; }

        public bool RefreshEnabled { get; internal set; }

        public bool PreviewEnabled { get; internal set; }

        public string PreviewButtonText { get; internal set; }

        public bool StopEnabled { get; internal set; }

        public string StopButtonText { get; internal set; }
    }

    public static class AssistantUiPresenter
    {
        public static AssistantUiPresentation Present(
            AssistantUiState state,
            bool configurationReady)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }

            AssistantUiPresentation presentation = new AssistantUiPresentation();
            presentation.RefreshEnabled = state.Kind != AssistantUiStateKind.Inspecting;
            presentation.PreviewEnabled = false;
            presentation.PreviewButtonText = "预览全部方案（只读）";

            switch (state.Kind)
            {
                case AssistantUiStateKind.WaitingForGame:
                    presentation.BannerText = "等待游戏 · 助手保持运行";
                    presentation.BannerTone = AssistantUiTone.Neutral;
                    presentation.ConnectionText = string.IsNullOrWhiteSpace(state.Detail)
                        ? "未发现精确目标游戏；启动游戏后会自动检测。"
                        : state.Detail;
                    presentation.ConnectionTone = AssistantUiTone.Neutral;
                    presentation.TargetText = state.TargetSummary;
                    presentation.TargetTone = AssistantUiTone.Neutral;
                    presentation.CityText = state.CitySummary;
                    presentation.CityTone = AssistantUiTone.Neutral;
                    break;

                case AssistantUiStateKind.Inspecting:
                    presentation.BannerText = "游戏在线 · 正在只读检测";
                    presentation.BannerTone = AssistantUiTone.Information;
                    presentation.ConnectionText = string.Format(
                        "PID {0} 正在检测；助手不会保留进程连接。",
                        state.ProcessId.HasValue ? state.ProcessId.Value.ToString() : "?");
                    presentation.ConnectionTone = AssistantUiTone.Information;
                    presentation.TargetText = state.TargetSummary;
                    presentation.TargetTone = AssistantUiTone.Information;
                    presentation.CityText = state.CitySummary;
                    presentation.CityTone = AssistantUiTone.Information;
                    break;

                case AssistantUiStateKind.ReadOnlyReady:
                    presentation.BannerText = "游戏在线 · 只读预览 · 无感执行开发中（未开放）";
                    presentation.BannerTone = AssistantUiTone.Warning;
                    presentation.ConnectionText = FormatCompletedConnection(state);
                    presentation.ConnectionTone = AssistantUiTone.Success;
                    presentation.TargetText = state.TargetSummary;
                    presentation.TargetTone = AssistantUiTone.Success;
                    presentation.CityText = state.CitySummary;
                    presentation.CityTone = AssistantUiTone.Success;
                    presentation.PreviewEnabled = configurationReady && state.SnapshotAvailable;
                    break;

                case AssistantUiStateKind.ExecutionUnavailable:
                    presentation.BannerText = state.SnapshotAvailable
                        ? "游戏在线 · 只读结果受阻 · 无感执行未开放"
                        : "只读检测未通过 · 助手保持运行";
                    presentation.BannerTone = AssistantUiTone.Warning;
                    presentation.ConnectionText = string.IsNullOrWhiteSpace(state.Detail)
                        ? "当前检测未通过；可点击“重新检测”。"
                        : state.Detail;
                    presentation.ConnectionTone = AssistantUiTone.Warning;
                    presentation.TargetText = state.TargetSummary;
                    presentation.TargetTone = AssistantUiTone.Warning;
                    presentation.CityText = state.CitySummary;
                    presentation.CityTone = state.SnapshotAvailable
                        ? AssistantUiTone.Warning
                        : AssistantUiTone.Neutral;
                    presentation.PreviewEnabled = configurationReady && state.SnapshotAvailable;
                    presentation.PreviewButtonText = state.SnapshotAvailable
                        ? "查看只读阻断详情"
                        : "预览全部方案（只读）";
                    break;

                case AssistantUiStateKind.Faulted:
                    presentation.BannerText = "助手遇到异常 · 窗口保持运行";
                    presentation.BannerTone = AssistantUiTone.Error;
                    presentation.ConnectionText = string.IsNullOrWhiteSpace(state.Detail)
                        ? "检测异常；助手仍在运行，可点击“重新检测”。"
                        : state.Detail;
                    presentation.ConnectionTone = AssistantUiTone.Error;
                    presentation.TargetText = state.TargetSummary;
                    presentation.TargetTone = AssistantUiTone.Error;
                    presentation.CityText = state.CitySummary;
                    presentation.CityTone = AssistantUiTone.Error;
                    break;

                default:
                    throw new InvalidOperationException("Unknown UI state: " + state.Kind);
            }

            if (!configurationReady)
            {
                presentation.BannerText += " · 配置未就绪";
                presentation.PreviewEnabled = false;
            }

            ApplyBatchActivity(state.BatchActivity, presentation);
            return presentation;
        }

        private static string FormatCompletedConnection(AssistantUiState state)
        {
            string completed = state.CompletedUtc.HasValue
                ? state.CompletedUtc.Value.ToLocalTime().ToString("HH:mm:ss")
                : "时间未知";
            return string.Format(
                "PID {0} 只读快照完成于 {1}（句柄已释放）",
                state.ProcessId.HasValue ? state.ProcessId.Value.ToString() : "?",
                completed);
        }

        private static void ApplyBatchActivity(
            BatchActivityState activity,
            AssistantUiPresentation presentation)
        {
            switch (activity)
            {
                case BatchActivityState.Unavailable:
                    presentation.StopButtonText = "无感执行未开放";
                    presentation.StopEnabled = false;
                    break;

                case BatchActivityState.Running:
                    presentation.StopButtonText = "请求停止";
                    presentation.StopEnabled = true;
                    presentation.RefreshEnabled = false;
                    presentation.PreviewEnabled = false;
                    presentation.BannerText = "批处理进行中 · 可在安全边界请求停止";
                    presentation.BannerTone = AssistantUiTone.Warning;
                    break;

                case BatchActivityState.StopRequested:
                    presentation.StopButtonText = "正在安全停止…";
                    presentation.StopEnabled = false;
                    presentation.RefreshEnabled = false;
                    presentation.PreviewEnabled = false;
                    presentation.BannerText = "已请求安全停止 · 等待当前命令边界";
                    presentation.BannerTone = AssistantUiTone.Warning;
                    break;

                case BatchActivityState.Stopped:
                    presentation.StopButtonText = "已停止";
                    presentation.StopEnabled = false;
                    presentation.RefreshEnabled = true;
                    presentation.PreviewEnabled = false;
                    presentation.BannerText = "批处理已停止 · 助手保持运行";
                    presentation.BannerTone = AssistantUiTone.Neutral;
                    break;

                default:
                    throw new InvalidOperationException("Unknown batch activity: " + activity);
            }
        }
    }
}
