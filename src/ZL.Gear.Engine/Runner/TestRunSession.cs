using System;
using System.Diagnostics;
using System.Threading;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 单次测试 Run 的运行态作用域（对标 OpenTAP PlanRun / TestPlanRun 实例级状态）。
    /// </summary>
    /// <remarks>
    /// <para>生命周期：<c>Start → 执行 → Dispose</c>；不可跨 Run 复用。</para>
    /// <para>从 <see cref="SequenceExecutor"/> 实例字段迁出的运行态：</para>
    /// <list type="bullet">
    ///   <item><see cref="RunId"/> — 追溯与 <see cref="SequenceExecutor.Stop(Guid)"/> 定位</item>
    ///   <item>链接 CTS — 外部 token + 用户 Stop 合并取消</item>
    ///   <item><see cref="Stopwatch"/> — Run 级耗时</item>
    ///   <item><see cref="StepIntervalMs"/> — 步骤间保护间隔（可被 globalContext 覆盖）</item>
    /// </list>
    /// <para>待办（T-P0-02b）：Quarantine 标记、Run 级 Diagnostics 隔离仍挂在 Executor 层。</para>
    /// </remarks>
    internal sealed class TestRunSession : IDisposable
    {
        private readonly CancellationTokenSource _linkedCts;
        private bool _disposed;

        /// <summary>本 Run 全局唯一 Id，写入 <see cref="Core.Runner.TestRunResult.RunId"/>。</summary>
        public Guid RunId { get; } = Guid.NewGuid();

        /// <summary>Run 级计时器；在设备租约成功后 Restart。</summary>
        public Stopwatch Stopwatch { get; } = new Stopwatch();

        /// <summary>步骤进度回调（UI / MES）；可为 null。</summary>
        public IProgress<StepRunResult> Progress { get; }

        /// <summary>
        /// 本 Run 的步骤间隔（毫秒）；可在启动时由 globalContext["TestStepInterval"] 覆盖默认值。
        /// </summary>
        public int StepIntervalMs { get; set; }

        /// <summary>链接取消令牌：外部 CancellationToken ∪ 用户 Stop。</summary>
        public CancellationToken CancellationToken => _linkedCts.Token;

        private TestRunSession(CancellationToken externalToken, IProgress<StepRunResult> progress, int stepIntervalMs)
        {
            Progress = progress;
            StepIntervalMs = stepIntervalMs;
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        }

        /// <summary>创建并进入 Run 作用域（由 ExecuteCoreAsync 在 single-flight 护栏内调用）。</summary>
        public static TestRunSession Start(
            CancellationToken externalToken,
            IProgress<StepRunResult> progress,
            int defaultStepIntervalMs)
        {
            return new TestRunSession(externalToken, progress, defaultStepIntervalMs);
        }

        /// <summary>用户或 Executor.Stop 请求取消本 Run。</summary>
        public void RequestStop()
        {
            if (!_linkedCts.IsCancellationRequested)
            {
                _linkedCts.Cancel();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (!_linkedCts.IsCancellationRequested)
                {
                    _linkedCts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // 已释放，忽略
            }

            _linkedCts.Dispose();
        }
    }
}
