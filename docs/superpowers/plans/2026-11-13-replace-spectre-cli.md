# Replace Spectre.Console.Cli with System.CommandLine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove `Spectre.Console.Cli` (and `Spectre.Console`) so the CLI layer is reflection-free and no longer blocks Native AOT, without changing the user-visible CLI contract.

**Architecture:** `BusynetApp.RunAsync` builds a `System.CommandLine` command tree by hand (no attribute/reflection binding), parses, maps parse errors to exit 2, and runs the matched command through a plain `ClaudeLimitsCommand` resolved from our own `ServiceProvider`. Settings become records read from `ParseResult`; the Spectre `ITypeRegistrar`/`ITypeResolver`/`ICommandInterceptor` shims disappear. The existing CLI tests are the behaviour spec and keep their assertions.

**Tech Stack:** .NET 11, `System.CommandLine` 3.0.0 (stable, published 2026-11-10, no dependencies), `Microsoft.Extensions.DependencyInjection`, xunit.v3.

**Spec:** `docs/superpowers/specs/2026-11-13-claude-limits-design.md` ("Cli" section and "Testing"; both name Spectre and are updated in Task 3). Motivation: AOT analysis in the conversation of 2026-11-13. Spectre.Console.Cli is a hard AOT blocker, and a `PublishAot` build of the current code crashes on startup with `Could not get settings type for command of type 'ClaudeLimitsCommand'`.

## Global Constraints

- Target framework `net11.0`; `TreatWarningsAsErrors=true`; `RestorePackagesWithLockFile=true` and CI restores with `--locked-mode`, so every `packages.lock.json` must be regenerated and committed.
- Package versions live only in `Directory.Packages.props` (central management).
- Exit codes: success/Ctrl+C `0`; expected failure (`BusynetException`) one line on stderr, `1` (full exception with `--verbose`); other exceptions printed in full, `1`; usage errors `2`.
- Never echo `--host` / `--token` values: messages stay exactly `Invalid --host value.` and `Invalid --token value.`; `--interval` message stays `--interval must be at least 1 second.`
- stdout is for help/version only; every error goes to stderr.
- `--version` prints the informational version without its `+commit` suffix (matches `\A\d+\.\d+\.\d+\S*\r?\n\z`). Aliases stay `-v, --version` and `-h, --help`.
- CLI surface otherwise unchanged: `busynet claude limits [--interval <seconds>] [--host <host>] [--token <token>] [--verbose]`, default interval 180.
- Branch: feature branch in the main clone (no worktree). `PackAsTool` stays; this plan does **not** enable `PublishAot` in the csproj (see Out of scope).

## Review Focus

1. `--host` / `--token` / `--interval` given with no value. Expected: usage error, exit 2, bar never contacted. (Today Spectre accepts `--host` with no value, exits 1 after trying the default bar. Pinned in Task 2.)
2. A token that starts with `-` (`--token=-abc`). Expected: accepted via the `=` form and reaches the connection. (Pinned in Task 2.)
3. `busynet` with no args and `busynet claude` with no subcommand. Expected: help on stdout, exit 0 for the root; usage error, exit 2 for `claude` alone (today `claude` alone exits 1). (Pinned in Task 3.)
4. Ctrl+C while a command is running. Expected: exit 0, nothing on stderr; System.CommandLine's own SIGINT/process-exit handler must not take over and exit 130 or hang 2s. (Pinned in Task 3.)
5. `--verbose` on a failing run. Expected: full exception on stderr, because verbosity is set before the command resolves. Providers are disposed (loggers flushed) on every path, including parse errors. (Pinned in Task 3.)

---

### Task 1: Settings records and option validation

**Files:**
- Modify: `Directory.Packages.props` (add `<PackageVersion Include="System.CommandLine" Version="3.0.0" />`; the Spectre entry is removed in Task 3)
- Modify: `src/Busynet.Cli/Busynet.Cli.csproj` (add `<PackageReference Include="System.CommandLine" />`; the Spectre reference stays until Task 3)
- Modify: `src/Busynet.Cli/packages.lock.json` (regenerate)
- Create: `src/Busynet.Cli/Infrastructure/BarSettings.cs`
- Create: `src/Busynet.Cli/Infrastructure/BarOptions.cs`
- Create: `src/Busynet.Cli/Features/ClaudeLimits/ClaudeLimitsOptions.cs`
- Test: `tests/Busynet.Cli.Tests/OptionParsingTests.cs`

The old Spectre classes (`BarCommandSettings`, `ClaudeLimitsSettings`) stay in place and keep the build green until Task 2; the new types get distinct names (`BarSettings`, `ClaudeLimitsOptions`) so both coexist. `ClaudeLimitsSettings` is renamed in Task 2.

**Interfaces:**
- Produces:
  - `public record BarSettings(string? Host, string? Token, bool Verbose)` in `Busynet.Cli.Infrastructure`.
  - `public sealed class BarOptions` with `Option<string?> Host`, `Option<string?> Token`, `Option<bool> Verbose` (names `--host`, `--token`, `--verbose`, descriptions copied from the old `[Description]` attributes), `void AddTo(Command command)` and `BarSettings Read(ParseResult result)`. Validators reject bad values with the exact constant messages above, and the validation logic (`BarConnection.Resolve` probe for host; `HttpRequestMessage.Headers.Add("X-API-Token", token.Trim())` probe for token; both skipped for null/whitespace) is moved here from `BarCommandSettings`.
  - `public sealed record ClaudeLimitsSettings(string? Host, string? Token, bool Verbose, int Interval) : BarSettings(Host, Token, Verbose)` is **deferred to Task 2**. In Task 1 the options class is `public sealed class ClaudeLimitsOptions(BarOptions bar)` with `Option<int> Interval` (`--interval`, default `180` via `DefaultValueFactory`, validator error `--interval must be at least 1 second.` when `< 1`), `void AddTo(Command)` (adds bar options and interval) and `(BarSettings Bar, int Interval) Read(ParseResult)`.

- [ ] **Step 1: Write the failing tests** in `OptionParsingTests.cs`. Build a throwaway `Command("t")` through `ClaudeLimitsOptions.AddTo`, then `command.Parse(args)`. Tests, each asserting `parse.Errors` and `Read` values:
  - `Defaults_are_null_host_null_token_not_verbose_interval_180`
  - `Reads_all_options` (`--host 10.0.0.1:8080 --token abc --verbose --interval 5`)
  - `Interval_below_one_is_error_with_exact_message` (`0`, `-1`; `Assert.Equal("--interval must be at least 1 second.", error.Message)`)
  - `Interval_not_a_number_is_error`
  - `Invalid_host_is_error_without_echo` (`"bad host["`: message equals `Invalid --host value.`)
  - `Invalid_token_is_error_without_echo` (`"SECRET\nx"`: message equals `Invalid --token value.`, does not contain `SECRET`)
  - `Option_without_value_is_error` (theory over `--host`, `--token`, `--interval`) → Review Focus 1
  - `Token_starting_with_dash_is_accepted_in_equals_form` (`--token=-abc`) → Review Focus 2
  - `Whitespace_host_and_token_are_accepted_as_unset` (matches the old `IsNullOrWhiteSpace` guards)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Busynet.Cli.Tests --filter-class "*OptionParsingTests"`
Expected: compile FAIL (`BarOptions` / `ClaudeLimitsOptions` undefined).

- [ ] **Step 3: Add the package and implement the three files** per the Interfaces block. Verify the exact `System.CommandLine` 3.0.0 member names while writing (`Option<T>`, `Validators`, `DefaultValueFactory`, `ParseResult.Errors`, `GetValue`) against the package, and adapt. Our own signatures above are what later tasks depend on. Regenerate locks: `dotnet restore --force-evaluate`.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Busynet.Cli.Tests --filter-class "*OptionParsingTests"`
Expected: all PASS. If a "Review Focus" test shows System.CommandLine cannot express the expected behaviour (for example `--token` missing value is silently null), fix it in the option definition (arity `ExactlyOne`), not in the test.

- [ ] **Step 5: Commit**

```bash
git switch -c refactor/system-commandline
git add Directory.Packages.props src/Busynet.Cli tests/Busynet.Cli.Tests/OptionParsingTests.cs
git commit -m "refactor(cli): add System.CommandLine option definitions and validation"
```

---

### Task 2: App shell, command, error mapping (the switch-over)

**Files:**
- Modify: `src/Busynet.Cli/BusynetApp.cs`
- Modify: `src/Busynet.Cli/Features/ClaudeLimits/ClaudeLimitsCommand.cs`
- Modify: `src/Busynet.Cli/Features/ClaudeLimits/ClaudeLimitsSettings.cs` (becomes the record described in Task 1; `DefaultInterval = 180` const stays here and `ClaudeLimitsOptions` uses it)
- Modify: `src/Busynet.Cli/Infrastructure/ErrorHandler.cs`
- Modify: `src/Busynet.Cli/Infrastructure/ServiceRegistration.cs`
- Modify: `src/Busynet.Cli/Program.cs`
- Modify: `tests/Busynet.Cli.Tests/AppHarness.cs`
- Delete: `src/Busynet.Cli/Infrastructure/BarCommandSettings.cs`, `TypeRegistrar.cs`, `TypeResolver.cs`, `VerbosityInterceptor.cs`
- Test: `tests/Busynet.Cli.Tests/ExitCodeTests.cs`, `ClaudeLimitsCommandTests.cs` (existing, assertions unchanged), `tests/Busynet.Cli.Tests/UsageErrorTests.cs` (new)

**Interfaces:**
- Consumes: `BarOptions`, `BarSettings`, `ClaudeLimitsOptions` from Task 1.
- Produces:
  - `BusynetApp.RunAsync(string[] args, IServiceCollection services, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken) : Task<int>`. This replaces the `IAnsiConsole? console` parameter with `stdout`, which receives help and version. `Name`, `Version` stay.
  - `ClaudeLimitsCommand.ExecuteAsync(ClaudeLimitsSettings settings, CancellationToken cancellationToken) : Task<int>`. It is no longer an `AsyncCommand`; the body is unchanged. `ClaudeLimitsCommand.Description` and a `public static Command Define(IServiceProvider provider)` that creates the `limits` command with the options and an action which (a) sets `provider.GetRequiredService<Verbosity>().IsVerbose = settings.Verbose`, then (b) resolves `ClaudeLimitsCommand` and returns `ExecuteAsync`. Setting verbosity before resolving matters: the logging filter and `ErrorHandler` read it.
  - `ErrorHandler.Handle(Exception exception, IServiceProvider? provider) : int`. The `CommandAppException` case is removed; `UsageError = 2` and `Failure = 1` constants stay and are used by `BusynetApp` for parse errors.
  - `AddBusynet` registers `services.AddTransient<ClaudeLimitsCommand>()`.
  - `AppHarness`: drops `Console`/`AnsiConsole`; `ConsoleOut` (a `StringWriter`) is passed as `stdout`; `RunAsync(params string[] args)` is unchanged for callers.

- [ ] **Step 1: Update the harness and add the failing tests.** Change `AppHarness` to the new `RunAsync` signature (this fails to compile first; that is the red state for the existing suite). Add `UsageErrorTests.cs`, all through `AppHarness` with `FakeBarFactory`/`ScriptedUsage` as in `ExitCodeTests.UseFakes`:
  - `Interval_zero_exits_2_with_exact_message_on_stderr_and_empty_stdout`
  - `Interval_not_a_number_exits_2_one_line_stderr`
  - `Unknown_option_exits_2` and `Unknown_command_exits_2`
  - `Host_option_without_value_exits_2_and_never_touches_bar` (`Assert.Empty(bar.Connections)`) → Review Focus 1
  - `Token_equals_form_starting_with_dash_reaches_connection` (`bar.Connections.Single().Token == "-abc"`; use `ScriptedUsage` that throws a `ClaudeTransientException` to end the run with exit 1) → Review Focus 2
  - `Short_version_alias_prints_version` (`-v`)
  - `Verbose_failure_prints_full_exception`: `ScriptedUsage` throws `ClaudeTransientException("boom")`; with `--verbose` stderr contains `ClaudeTransientException` and `boom`; without it stderr is exactly `boom` + newline → Review Focus 5
  - `Unexpected_exception_prints_in_full_and_exits_1` (`InvalidOperationException` from usage)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Busynet.Cli.Tests`
Expected: compile FAIL (`RunAsync` signature mismatch), then, once only the harness compiles against a stub, `UsageErrorTests` FAIL.

- [ ] **Step 3: Implement `BusynetApp.RunAsync`.**
  1. `provider = services.BuildServiceProvider()` inside `using`/`await using`, so loggers are flushed on every exit path.
  2. Root `RootCommand`; description from the current app. Replace the built-in version option with a custom `-v`/`--version` option whose action writes `Version` (the stripped informational version) to `stdout`. Built-in `-h/--help` stays.
  3. Add a `claude` command (description "Claude Code on the bar.") with `ClaudeLimitsCommand.Define(provider)` as the `limits` child. `claude` itself has no action, so `busynet claude` is a parse error.
  4. `var parse = root.Parse(args)`. If `parse.Errors.Count > 0`, write each error message on its own line to `stderr` and return `ErrorHandler.UsageError`. Otherwise run `parse.InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr, ProcessTerminationTimeout = null /* we own Ctrl+C */ }, cancellationToken)` inside `try`, with `catch (Exception e)` → `new ErrorHandler(stderr, cancellationToken).Handle(e, provider)`. Keep the `OperationCanceledException when token cancelled → 0` behaviour inside `Handle`.
  5. No args: print root help to `stdout`, return 0 (decide by `args.Length == 0`; do not rely on System.CommandLine's error).

  Then: `ClaudeLimitsCommand` as above; delete the four Spectre shim files; update `Program.cs` for the new signature (`Console.Out`, `Console.Error`); `ErrorHandler` per the Interfaces block (it gets the provider and resolves `Verbosity` with `GetService<Verbosity>()`).
  Do **not** remove the package reference yet; `grep -rn "Spectre" src tests --include='*.cs'` must show no remaining usages, which Task 3 enforces.

- [ ] **Step 4: Run the full suite**

Run: `dotnet build -c Release -warnaserror && dotnet test -c Release`
Expected: all PASS, including the pre-existing `ExitCodeTests` and `ClaudeLimitsCommandTests` with their assertions untouched (Version, Help `--interval` in stdout, exact stderr lines, exit codes).

- [ ] **Step 5: Commit**

```bash
git add -A src/Busynet.Cli tests/Busynet.Cli.Tests
git commit -m "refactor(cli): run commands through System.CommandLine instead of Spectre"
```

---

### Task 3: Root/group behaviour, Ctrl+C pin, drop Spectre, docs, AOT smoke

**Files:**
- Modify: `Directory.Packages.props` (remove `Spectre.Console.Cli`)
- Modify: `src/Busynet.Cli/Busynet.Cli.csproj` (remove `Spectre.Console.Cli` reference)
- Modify: `src/*/packages.lock.json`, `tests/*/packages.lock.json` (regenerate)
- Modify: `docs/superpowers/specs/2026-11-13-claude-limits-design.md` (lines naming Spectre: stack table row, project-layout comment, "Cli" section, "Testing" bullet; describe System.CommandLine, DI built by `BusynetApp`, usage errors as exit 2)
- Modify: `README.md` only if its option/help text no longer matches `busynet claude limits --help`
- Test: `tests/Busynet.Cli.Tests/RootCommandTests.cs`

**Interfaces:** Consumes `AppHarness`, `BusynetApp.RunAsync` from Task 2.

- [ ] **Step 1: Write the failing tests** in `RootCommandTests.cs` (through `AppHarness`):
  - `No_args_prints_help_to_stdout_exit_0` (stdout contains `claude`; stderr empty) → Review Focus 3
  - `Claude_without_subcommand_is_usage_error_exit_2` (stderr non-empty; stdout empty) → Review Focus 3
  - `Claude_help_lists_limits_exit_0`
  - `Ctrl_C_while_watching_exits_0_with_empty_stderr`: fake usage seeds successfully, `FakeBarFactory` button stream never completes, `app.Cts.Cancel()` after the first display call, assert exit 0 and `Stderr == ""` → Review Focus 4
  - `Parse_error_still_disposes_provider`: register a singleton `IDisposable` probe via `app.Services`; after a parse-error run (`--bogus`) the probe is disposed → Review Focus 5

- [ ] **Step 2: Run to verify failure/pass state**

Run: `dotnet test tests/Busynet.Cli.Tests --filter-class "*RootCommandTests"`
Expected: any test failing here exposes a Task 2 gap; fix it in `BusynetApp`, not in the test. (The Ctrl+C test is a pin: it may already pass.)

- [ ] **Step 3: Remove Spectre.** Delete the package version and reference. Run `dotnet restore --force-evaluate`, then `grep -rniE "spectre" --include='*.cs' --include='*.csproj' --include='*.props' --include='*.json' . | grep -v '/obj/' | grep -v '/bin/'` and expect only docs/spec history. Update the spec and README per the Files block.

- [ ] **Step 4: Full verification, mirroring CI**

Run:
```
dotnet restore --locked-mode
dotnet build -c Release --no-restore -warnaserror
dotnet test -c Release --no-build
dotnet pack src/Busynet.Cli -c Release -o ./nupkg
```
Expected: all green. Then the tool smoke, as CI does: install from `./nupkg` into a temp `--tool-path`, `busynet --version` equals `0.1.0`, `busynet --help` and `busynet claude limits --help` exit 0.

- [ ] **Step 5: AOT smoke of the CLI layer (verification only, nothing committed)**

Run:
```
dotnet publish src/Busynet.Cli -c Release -r osx-arm64 -p:PublishAot=true -p:TreatWarningsAsErrors=false -o <scratchpad>/aot
<scratchpad>/aot/busynet --version
<scratchpad>/aot/busynet claude limits --help
<scratchpad>/aot/busynet claude limits --interval 0; echo $?
```
Expected: `0.1.0`, help text, then the interval message with exit `2`. No IL3050 warning from `BusynetApp`, and no Spectre warnings. The remaining AOT warnings are Refit `RestService.For` (IL2026) and the JSON polymorphism in `BarJson`/`DisplayElement`. Those belong to a separate plan. Revert any `packages.lock.json` churn the RID publish caused (`git checkout -- '**/packages.lock.json'`, then re-run the Step 4 restore to confirm lock files are still consistent).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(cli): remove Spectre.Console.Cli; update spec and locks"
```

---

## Out of scope (follow-up plan)

- Source-generated `JsonSerializerContext` for `BarJson` (polymorphic `DisplayElement`), and verifying/replacing Refit's `RestService.For` under AOT.
- Adding `PublishAot`/`IsAotCompatible` and per-RID release binaries to CI. `PackAsTool` and AOT are mutually exclusive in one project, so the distribution model (GitHub release binaries or Homebrew vs `dotnet tool`) needs deciding first.

## Self-review

- **Spec coverage:** every Spectre use found by grep (`BusynetApp`, `ClaudeLimitsCommand`, `ClaudeLimitsSettings`, `BarCommandSettings`, `ErrorHandler`, `TypeRegistrar`, `TypeResolver`, `VerbosityInterceptor`, `ServiceRegistration` usings, `AppHarness`) is handled in Tasks 2 and 3. The exit-code contract, the no-echo rule, `--version` formatting and Ctrl+C are each covered by a test.
- **Deliberate behaviour changes:** `busynet claude` alone goes from exit 1 to usage error 2; an option given without a value goes from silently ignored (exit 1, bar contacted) to usage error 2. Both are bug fixes and are noted in the commit bodies.
- **Type consistency:** `BarSettings`, `BarOptions`, `ClaudeLimitsOptions` (Task 1) feed `ClaudeLimitsSettings` and `ClaudeLimitsCommand.Define` (Task 2). `RunAsync(args, services, stdout, stderr, ct)` is used identically in the harness, `Program.cs` and the tests.
- **Risk:** the System.CommandLine 3.0.0 API names were not checked against the package when this plan was written. Task 1 Step 3 requires verifying them first, and only our own signatures are contractual.
