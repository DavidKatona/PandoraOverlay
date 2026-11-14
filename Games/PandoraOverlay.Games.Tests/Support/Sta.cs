using System.Reflection;
using System.Runtime.ExceptionServices;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// A test body on a thread of its own in the single-threaded apartment, which
/// WPF needs for anything it shows or renders (an element, a bitmap, a
/// dispatcher timer), with any exception brought back to the test's thread
/// with its own stack. Copied from the app's tests (TheThemeOnScreen.OnSta):
/// the two test projects share no code.
/// </summary>
internal static class Sta
{
    public static void Run(Action test)
    {
        ArgumentNullException.ThrowIfNull(test);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure is TargetInvocationException { InnerException: { } inner } ? inner : failure).Throw();
    }
}
