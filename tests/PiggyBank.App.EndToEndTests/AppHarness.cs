using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace PiggyBank.App.EndToEndTests;

/// <summary>
/// Launches the real WPF app in an isolated data directory, exposes the
/// FlaUI <see cref="Application"/> and <see cref="UIA3Automation"/> for the
/// test, and tears everything down on dispose.
/// </summary>
/// <remarks>
/// Relies on <c>PiggyBank_DATA_ROOT</c> being honoured by
/// <c>AppHost.Build</c>. Each test gets a fresh temp folder so profiles,
/// DB, and logs don't leak between runs.
/// </remarks>
public sealed class AppHarness : IDisposable
{
    private readonly string _dataRoot;
    private bool _disposed;

    public Application App { get; }
    public UIA3Automation Automation { get; }

    /// <summary>Default timeout for every wait. First-run app startup has
    /// to run EF migrations + seed the 32-category catalog, which blows past
    /// 20s on cold machines. 45s is generous enough to absorb that plus
    /// JIT + automation tree population, without masking a true hang.</summary>
    public TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(45);

    private AppHarness(string dataRoot, Application app, UIA3Automation automation)
    {
        _dataRoot = dataRoot;
        App = app;
        Automation = automation;
    }

    public static AppHarness Launch()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            "PiggyBank.E2E",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        var exePath = ResolveAppExe();
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        };
        psi.EnvironmentVariables["PiggyBank_DATA_ROOT"] = dataRoot;

        var app = Application.Launch(psi);
        var automation = new UIA3Automation();
        return new AppHarness(dataRoot, app, automation);
    }

    /// <summary>Waits for any top-level window owned by the app whose
    /// AutomationId matches <paramref name="automationId"/>.</summary>
    public Window WaitForWindow(string automationId)
        => WaitForWindow(automationId, DefaultTimeout);

    public Window WaitForWindow(string automationId, TimeSpan timeout)
    {
        var found = Retry.WhileNull(
            () =>
            {
                if (App.HasExited) return null;
                try
                {
                    foreach (var window in App.GetAllTopLevelWindows(Automation))
                    {
                        if (TryMatch(window, automationId)) return window;
                    }
                }
                catch
                {
                    // The automation tree can throw transient
                    // ElementNotAvailableException during WPF window
                    // init. Swallow and let the retry tick re-query.
                }
                return null;
            },
            timeout,
            TimeSpan.FromMilliseconds(250)).Result;

        if (found is not null) return found;

        // Diagnostic on timeout: did the process die, and what windows did we see?
        var diag = new List<string>();
        if (App.HasExited)
        {
            diag.Add($"App process exited with code {App.ExitCode} before the target window appeared.");
            diag.Add("This usually means a startup exception — check %LocalAppData%\\PiggyBank\\Logs.");
        }
        else
        {
            try
            {
                var windows = App.GetAllTopLevelWindows(Automation);
                diag.Add($"App still running. Top-level windows seen: {windows.Length}.");
                foreach (var w in windows)
                {
                    var id = w.Properties.AutomationId.IsSupported ? w.AutomationId : "<no id>";
                    diag.Add($"  - title='{w.Title}' automationId='{id}'");
                }
            }
            catch (Exception ex)
            {
                diag.Add($"Failed to enumerate top-level windows: {ex.Message}");
            }
        }

        throw new TimeoutException(
            $"No top-level window with AutomationId '{automationId}' appeared within {timeout}.\n"
            + string.Join("\n", diag));
    }

    private static bool TryMatch(Window window, string automationId)
    {
        try
        {
            return window.Properties.AutomationId.IsSupported
                && window.AutomationId == automationId;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Finds a descendant by AutomationId, retrying until the default
    /// timeout. Returns the raw <see cref="AutomationElement"/> — callers cast
    /// to the concrete control type (Button, TextBox, ListBox, ...).</summary>
    public static AutomationElement WaitForElement(AutomationElement root, string automationId, TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(15);
        var found = Retry.WhileNull(
            () => root.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            actualTimeout,
            TimeSpan.FromMilliseconds(150)).Result;

        return found ?? throw new TimeoutException(
            $"Descendant element '{automationId}' not found within {actualTimeout}.");
    }

    /// <summary>Clicks a WPF-UI <c>ui:Button</c> by AutomationId. We use
    /// <c>Click()</c> (real mouse click at the clickable point) rather than
    /// <c>Invoke()</c> because the UIA Invoke pattern doesn't reliably fire
    /// <c>CommunityToolkit.Mvvm</c> RelayCommands on WPF-UI buttons — the
    /// Command binding sits behind the Click event, not the Invoke pattern.</summary>
    public static void ClickButton(AutomationElement root, string automationId)
    {
        BringAppToForeground(root);
        var button = WaitForElement(root, automationId).AsButton();
        // Wait for the button to be enabled — a freshly-rendered dialog may
        // briefly have disabled buttons until bindings settle. A disabled
        // Click() silently no-ops, which is why dialogs sometimes just sat
        // there doing nothing.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!button.IsEnabled && DateTime.UtcNow < deadline)
            Thread.Sleep(100);
        button.Click();
        // Give the command a moment to run before the caller queries state.
        Thread.Sleep(200);
    }

    /// <summary>Sets text on an element that is either itself the edit surface
    /// (WPF-UI's NumberBox / TextBox exposed as Edit control) OR has a single
    /// <c>Edit</c> descendant. Uses keyboard input (focus + Ctrl+A + type)
    /// rather than the <c>ValuePattern</c> setter because WPF-UI's custom
    /// TextBox doesn't always fire the <c>UpdateSourceTrigger=PropertyChanged</c>
    /// binding when <c>Text</c> is set via UIA — the VM sees an empty string
    /// and <c>CanExecute</c> stays false on the Save/Create button.</summary>
    public static void SetText(AutomationElement root, string automationId, string value)
    {
        var el = WaitForElement(root, automationId);
        var target = el.ControlType == FlaUI.Core.Definitions.ControlType.Edit
            ? el
            : el.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit))
              ?? el;

        Exception? lastFailure = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                // Primary path: atomic ValuePattern.SetValue. Bypasses keyboard
                // entirely, so focus theft and NumberBox SmallChange handling
                // can't corrupt the input. Works for WPF TextBox (raises
                // TextChanged which propagates the OneWay-to-source binding
                // with UpdateSourceTrigger=PropertyChanged) and WPF-UI NumberBox.
                if (target.Patterns.Value.IsSupported
                    && !target.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault)
                {
                    target.Patterns.Value.Pattern.SetValue(value);
                    Thread.Sleep(100);

                    // Tab out to commit the binding. WPF-UI NumberBox (and
                    // any TextBox with UpdateSourceTrigger=LostFocus, which
                    // is the default) only pushes its Value through a
                    // TwoWay binding when focus leaves. Without this Tab,
                    // the underlying VM observable stays at its default
                    // and CanExecute on the dependent button returns false.
                    BringAppToForeground(root);
                    target.Click();
                    Thread.Sleep(100);
                    FlaUI.Core.Input.Keyboard.Press(
                        FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB);
                    Thread.Sleep(150);

                    var actual = TryReadValue(target);
                    if (actual is not null && Normalise(actual) == Normalise(value))
                        return;
                }

                // Fallback: keyboard typing, in case ValuePattern is unsupported
                // or the binding didn't commit.
                BringAppToForeground(root);
                target.Click();
                Thread.Sleep(150);
                FlaUI.Core.Input.Keyboard.TypeSimultaneously(
                    FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
                    FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
                FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DELETE);
                FlaUI.Core.Input.Keyboard.Type(value);
                Thread.Sleep(250);

                var fallbackActual = TryReadValue(target);
                if (fallbackActual is not null && Normalise(fallbackActual) == Normalise(value))
                    return;

                lastFailure = new InvalidOperationException(
                    $"SetText attempt {attempt}: wrote '{value}' to '{automationId}' but read back '{fallbackActual}'.");
            }
            catch (Exception ex) { lastFailure = ex; }
            Thread.Sleep(200);
        }

        throw lastFailure ?? new InvalidOperationException(
            $"SetText failed for '{automationId}' after 3 attempts.");
    }

    private static string? TryReadValue(AutomationElement el)
    {
        try
        {
            if (el.Patterns.Value.IsSupported)
                return el.Patterns.Value.Pattern.Value.ValueOrDefault;
        }
        catch { }
        try { return el.AsTextBox().Text; } catch { }
        return null;
    }

    private static string Normalise(string s) =>
        s?.Replace(",", "").Replace(" ", "").TrimEnd('\r', '\n').TrimStart('-').TrimStart('-')
        ?? string.Empty;

    /// <summary>Walks up from <paramref name="element"/> to its owning top-level
    /// window and forces it to foreground via Win32 <c>SetForegroundWindow</c>.
    /// Called before every input operation because other apps (Teams, IDE,
    /// notification toasts) routinely steal focus on a real desktop, which
    /// silently redirects our keyboard input to the wrong window.</summary>
    private static void BringAppToForeground(AutomationElement element)
    {
        try
        {
            var current = element;
            while (current is not null && current.ControlType != FlaUI.Core.Definitions.ControlType.Window)
            {
                current = current.Parent;
            }
            if (current is null) return;
            var hwnd = current.Properties.NativeWindowHandle.IsSupported
                ? current.Properties.NativeWindowHandle.Value
                : IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;

            // AttachThreadInput + SetForegroundWindow dance — the naive
            // SetForegroundWindow call is ignored by Windows unless the
            // calling thread owns the current foreground window. Attaching
            // our thread to the foreground thread grants the permission.
            var currentForeground = GetForegroundWindow();
            var foregroundThread = GetWindowThreadProcessId(currentForeground, out _);
            var ourThread = GetCurrentThreadId();
            if (foregroundThread != ourThread)
                AttachThreadInput(ourThread, foregroundThread, true);
            try
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (foregroundThread != ourThread)
                    AttachThreadInput(ourThread, foregroundThread, false);
            }
            Thread.Sleep(100);
        }
        catch
        {
            // Best-effort — if P/Invoke fails, the test still runs; it just
            // might be flaky under focus steal. Don't crash on the helper.
        }
    }

    // --- Win32 interop for foreground control ---

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static string ResolveAppExe()
    {
        // The test runner's base directory sits inside .\tests\*.Tests\bin\...
        // The App exe lives in a sibling folder. Walk up and find it.
        var testBin = AppContext.BaseDirectory;
        var search = Directory.GetParent(testBin);

        while (search is not null)
        {
            var candidate = Path.Combine(
                search.FullName,
                "src", "PiggyBank.App", "bin",
                Path.GetFileName(testBin)!.Contains("Release") ? "Release" : "Debug",
                "net10.0-windows", "PiggyBank.App.exe");

            if (File.Exists(candidate))
                return candidate;

            search = search.Parent;
        }

        // Fallback — probe the solution root env var if tests set one.
        var slnRoot = Environment.GetEnvironmentVariable("PiggyBank_SLN_ROOT");
        if (!string.IsNullOrEmpty(slnRoot))
        {
            var guess = Path.Combine(slnRoot,
                "src", "PiggyBank.App", "bin", "Debug",
                "net10.0-windows", "PiggyBank.App.exe");
            if (File.Exists(guess)) return guess;
        }

        throw new FileNotFoundException(
            "Could not locate PiggyBank.App.exe. Build the solution in Debug first " +
            "(`dotnet build`). If running outside the solution, set PiggyBank_SLN_ROOT.");
    }

    /// <summary>Test code can set this to keep the temp data root after
    /// Dispose for post-mortem DB inspection. Defaults to true so normal
    /// green runs clean up.</summary>
    public bool CleanupOnDispose { get; set; } = true;

    public string DataRoot => _dataRoot;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { if (!App.HasExited) App.Close(); } catch { /* swallow */ }
        try { Automation.Dispose(); } catch { /* swallow */ }
        try { App.Dispose(); } catch { /* swallow */ }
        try
        {
            if (CleanupOnDispose && Directory.Exists(_dataRoot))
                Directory.Delete(_dataRoot, recursive: true);
        }
        catch { /* swallow — isolated temp dir, OS will reap eventually */ }
    }
}
