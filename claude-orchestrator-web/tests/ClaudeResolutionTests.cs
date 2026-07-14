using System.IO;
using ClaudeOrchestrator.Services;

namespace ClaudeOrchestrator.Tests;

/// <summary>
/// Tests for PtySession's claude-executable resolution, which must work regardless of
/// how Claude Code was installed (npm global vs. native install.ps1) and regardless of
/// whether the backend process inherited a freshly-updated PATH.
///
/// node-pty (ConPTY) can only spawn a real claude.exe — not npm's claude.ps1/.cmd shims
/// — so resolution must return a concrete .exe path, never the bare name "claude", when
/// a real executable exists.
/// </summary>
public class ClaudeResolutionTests
{
    private const string UserProfile = @"C:\Users\dev";
    private const string NativeExe   = @"C:\Users\dev\.local\bin\claude.exe";

    // Build a PATH string from directories using the host path separator.
    private static string Path(params string[] dirs) => string.Join(System.IO.Path.PathSeparator, dirs);

    // A file-exists predicate backed by a fixed set of "present" paths.
    private static Func<string, bool> Exists(params string[] present)
    {
        var set = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    // ── PATH / native-install resolution ─────────────────────────────────────

    [Fact]
    public void FromPath_FindsRealClaudeExeOnPath()
    {
        var claudeOnPath = @"C:\tools\claude.exe";
        var result = PtySession.ResolveClaudeFromPath(
            Path(@"C:\tools"), UserProfile, Exists(claudeOnPath));

        Assert.Equal((claudeOnPath, System.Array.Empty<string>()), result);
    }

    [Fact]
    public void FromPath_FindsNativeInstallLocation_WhenNotOnPath()
    {
        // Native installer put claude.exe in ~\.local\bin but this process's PATH is stale.
        var result = PtySession.ResolveClaudeFromPath(
            Path(@"C:\Windows\System32"), UserProfile, Exists(NativeExe));

        Assert.Equal((NativeExe, System.Array.Empty<string>()), result);
    }

    [Fact]
    public void FromPath_IgnoresShims_ReturnsNullWhenOnlyPs1CmdPresent()
    {
        // npm puts claude.ps1/.cmd (but no claude.exe) on PATH. node-pty can't run those,
        // so PATH resolution must NOT claim success here — it should fall through to npm.
        var npmDir = @"C:\Users\dev\AppData\Roaming\npm";
        var result = PtySession.ResolveClaudeFromPath(
            Path(npmDir),
            UserProfile,
            Exists(System.IO.Path.Combine(npmDir, "claude.ps1"),
                   System.IO.Path.Combine(npmDir, "claude.cmd")));

        Assert.Null(result);
    }

    [Fact]
    public void FromPath_ReturnsNull_WhenNothingFound()
    {
        var result = PtySession.ResolveClaudeFromPath(
            Path(@"C:\Windows\System32", @"C:\tools"), UserProfile, Exists(/* nothing */));

        Assert.Null(result);
    }

    [Fact]
    public void FromPath_SkipsEmptyPathSegments()
    {
        // Trailing/duplicate separators produce empty segments — must not throw or false-match.
        var result = PtySession.ResolveClaudeFromPath(
            Path("", @"C:\tools", ""), UserProfile, Exists(NativeExe));

        // Nothing on PATH; falls back to native location.
        Assert.Equal((NativeExe, System.Array.Empty<string>()), result);
    }

    // ── npm-global resolution ────────────────────────────────────────────────

    [Fact]
    public void FromNpm_FindsNativeBinaryInPackage()
    {
        var npmRoot = @"C:\Users\dev\AppData\Roaming\npm\node_modules";
        var exe = System.IO.Path.Combine(npmRoot, "@anthropic-ai", "claude-code", "bin", "claude.exe");

        var result = PtySession.ResolveClaudeFromNpm(npmRoot, Exists(exe));

        Assert.Equal((exe, System.Array.Empty<string>()), result);
    }

    [Fact]
    public void FromNpm_FallsBackToCliJs_ForOlderVersions()
    {
        var npmRoot = @"C:\npm\node_modules";
        var cliJs = System.IO.Path.Combine(npmRoot, "@anthropic-ai", "claude-code", "cli.js");

        var result = PtySession.ResolveClaudeFromNpm(npmRoot, Exists(cliJs));

        Assert.Equal(("node", new[] { cliJs }), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromNpm_ReturnsNull_WhenNpmRootMissing(string? npmRoot)
    {
        // Simulates 'npm root -g' failing (npm not on the backend's PATH).
        var result = PtySession.ResolveClaudeFromNpm(npmRoot, Exists(NativeExe));
        Assert.Null(result);
    }

    [Fact]
    public void FromNpm_ReturnsNull_WhenPackageAbsent()
    {
        var result = PtySession.ResolveClaudeFromNpm(@"C:\npm\node_modules", Exists(/* nothing */));
        Assert.Null(result);
    }

    // ── The reported bug: native install present, npm absent ─────────────────

    [Fact]
    public void NativeInstall_WithoutNpm_ResolvesToRealExe_NotBareClaude()
    {
        // Reproduces the "[pty-proxy] Failed to spawn 'claude': File not found" case:
        // colleague installed via install.ps1 (native), so npm has nothing. Resolution
        // must still yield a concrete claude.exe, never the un-spawnable bare "claude".
        var fromPath = PtySession.ResolveClaudeFromPath(
            Path(@"C:\Windows\System32"), UserProfile, Exists(NativeExe));
        var fromNpm  = PtySession.ResolveClaudeFromNpm(npmRoot: "", Exists(NativeExe));

        Assert.NotNull(fromPath);
        Assert.Null(fromNpm);
        Assert.Equal(NativeExe, fromPath!.Value.cmd);
        Assert.NotEqual("claude", fromPath.Value.cmd);
    }
}
