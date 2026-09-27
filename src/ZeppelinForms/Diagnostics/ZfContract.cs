using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ZeppelinForms.Diagnostics;

/// <summary>
/// An internal framework contract was violated: not a user error, but a sign
/// that a control's or platform's code does something the rest of the code
/// doesn't expect.
/// </summary>
public sealed class ZfContractException(string message) : InvalidOperationException(message);

/// <summary>
/// Checks of internal contracts. Rules like "a pooled brush must not be held
/// across someone else's draw call" used to live in comments, that is, were
/// followed exactly until the first new control.
/// </summary>
/// <remarks>
/// All methods are marked [Conditional("DEBUG")]: in a release build the calls
/// are cut out by the compiler together with evaluating the arguments, so a check
/// can be placed on a hot path too.
///
/// That is exactly why no work the code itself needs may be moved into them:
/// in release it simply won't happen.
/// </remarks>
public static class ZfContract
{
    /// <summary>What to do on a violation. By default — an exception: the contract
    /// is violated by framework code, and continuing after that means debugging
    /// consequences instead of the cause.</summary>
    public static ContractViolationBehavior Behavior { get; set; }
        = ContractViolationBehavior.Throw;

    /// <summary>A contract violation. The subscription is needed by tests: to check
    /// that a violation happened without failing the run.</summary>
    public static event EventHandler<string>? Violated;

    [Conditional("DEBUG")]
    public static void Require(
        bool condition,
        string message,
        [CallerMemberName] string? member = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        if (condition) return;

        Report($"{message}\n  in {member}, {System.IO.Path.GetFileName(file)}:{line}");
    }

    /// <summary>An unconditional violation: the code got where it shouldn't have.</summary>
    [Conditional("DEBUG")]
    public static void Fail(
        string message,
        [CallerMemberName] string? member = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        Report($"{message}\n  in {member}, {System.IO.Path.GetFileName(file)}:{line}");
    }

    private static void Report(string message)
    {
        Violated?.Invoke(null, message);

        switch (Behavior)
        {
            case ContractViolationBehavior.Throw:
                throw new ZfContractException(message);

            case ContractViolationBehavior.Log:
                Debug.WriteLine($"[ZF] contract violated: {message}");
                break;

            case ContractViolationBehavior.Silent:
                break;
        }
    }
}

public enum ContractViolationBehavior
{
    /// <summary>Throw <see cref="ZfContractException"/>.</summary>
    Throw,

    /// <summary>Write to the debug output and continue. Needed where the violation
    /// is already known and fixed separately, and failing the run is undesirable.</summary>
    Log,

    /// <summary>Only the event, without output. For tests that provoke
    /// the violation on purpose.</summary>
    Silent,
}