$ErrorActionPreference = 'Stop'
$enc = New-Object System.Text.UTF8Encoding($false)
$root = 'E:\Project\pinaypalbackupmanager'

# C# block template. Single-quoted here-string so PowerShell never interpolates
# the C# $"..." strings or the braces inside them.
$blockTemplate = @'
        // -- Auto-refresh -------------------------------------------------
        // The tab used to refresh only when opened, so figures went stale and
        // the user had to press Refresh. The timer runs only while this control
        // is attached to the visual tree, so hidden tabs cost nothing.
        private DispatcherTimer? _autoRefreshTimer;
        private bool _autoRefreshInFlight;

        private void StartAutoRefresh()
        {
            StopAutoRefresh();

            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(__SECONDS__)
            };

            _autoRefreshTimer.Tick += async (_, _) =>
            {
                // Skip rather than queue: these refreshes hit disk and can take a
                // while, and overlapping runs would fight over the same controls.
                if (_autoRefreshInFlight)
                    return;

                _autoRefreshInFlight = true;
                try
                {
                    await __METHOD__();
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[UI] __CLASS__ auto-refresh failed: {ex.Message}", "Warning", "SYSTEM");
                }
                finally
                {
                    _autoRefreshInFlight = false;
                }
            };

            _autoRefreshTimer.Start();
        }

        private void StopAutoRefresh()
        {
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer = null;
        }

'@

$cfg = @(
    @{ Name = 'StatisticsControl';         Method = 'RefreshStatisticsAsync';  Secs = 30 },
    @{ Name = 'HealthCheckControl';        Method = 'RunHealthCheckAsync';     Secs = 30 },
    @{ Name = 'ErrorReportViewerControl';  Method = 'LoadErrorReportsAsync';   Secs = 15 },
    @{ Name = 'PerformanceMetricsControl'; Method = 'RefreshMetricsAsync';     Secs = 30 }
)

foreach ($c in $cfg) {
    $file = Join-Path $root ("UI\UserControls\" + $c.Name + '.axaml.cs')
    $lines = [System.Collections.ArrayList]([System.IO.File]::ReadAllLines($file))

    if (-not (($lines -join "`n") -match 'using Avalonia.Threading;')) {
        $usings = @(0..($lines.Count - 1) | Where-Object { $lines[$_] -match '^using ' })
        $at = $usings[-1] + 1
        $lines.Insert($at, 'using Avalonia.Threading;')
        $lines.Insert($at + 1, '')
    }

    $ctorIdx = @(0..($lines.Count - 1) | Where-Object { $lines[$_] -match ("public " + $c.Name + '\(\)') })[0]
    $loadIdx = @(0..($lines.Count - 1) | Where-Object { $lines[$_] -match 'AvaloniaXamlLoader\.Load\(this\);|InitializeComponent\(\);' })[0]

    $lines.Insert($loadIdx + 1, '            AttachedToVisualTree += (_, _) => StartAutoRefresh();')
    $lines.Insert($loadIdx + 2, '            DetachedFromVisualTree += (_, _) => StopAutoRefresh();')

    $block = $blockTemplate.Replace('__SECONDS__', [string]$c.Secs).Replace('__METHOD__', $c.Method).Replace('__CLASS__', $c.Name)
    $blockLines = $block -split "`r?`n"

    $ctorIdx = @(0..($lines.Count - 1) | Where-Object { $lines[$_] -match ("public " + $c.Name + '\(\)') })[0]
    for ($i = $blockLines.Count - 1; $i -ge 0; $i--) {
        [void]$lines.Insert($ctorIdx, $blockLines[$i])
    }

    [System.IO.File]::WriteAllLines($file, $lines, $enc)
    Write-Output ("{0,-28} auto-refresh {1}s  OK" -f $c.Name, $c.Secs)
}
